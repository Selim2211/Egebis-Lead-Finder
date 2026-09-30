using System.Data.Common;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services.Security;

/// <summary>
/// Acilista, sifreleme oncesinden kalan duz metin kritik alanlari sifreler ve arama indekslerini
/// doldurur. Tekrar calismasi guvenlidir: yalnizca henuz sifrelenmemis degerlere dokunur. Kayitlar
/// dogrudan SQL ile guncellenir; boylece "degisti" sayilip Salesforce senkronu veya e-posta
/// dogrulamasi yeniden tetiklenmez.
/// </summary>
public static class EncryptionBackfill
{
    /// <summary>Tablo, sifrelenecek kolonlar ve (varsa) kaynak kolondan uretilen kor indeks kolonu.</summary>
    private sealed record Target(string Table, string[] Columns, string? HashSource = null, string? HashColumn = null);

    private static readonly Target[] Targets =
    {
        new("Contacts", new[] { "Email", "Phone", "SourceUrl" }, "Email", "EmailHash"),
        new("SentEmails", new[] { "ToAddress" }, "ToAddress", "ToAddressHash"),
        new("Users", new[] { "Email", "FullName" })
    };

    public static async Task<int> RunAsync(ApplicationDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var enc = FieldEncryption.Current ?? throw new InvalidOperationException("Şifreleme anahtarı yüklenmedi.");
        var total = 0;

        await VerifyKeyAsync(db, enc, logger, ct);

        foreach (var t in Targets)
            total += await EncryptTableAsync(db, enc, t, ct);

        total += await EncryptSettingsAsync(db, enc, ct);

        if (total > 0)
            logger.LogInformation("Şifreleme: {Count} kayıttaki kritik alanlar şifrelendi.", total);
        return total;
    }

    /// <summary>Veritabanindaki sifreli bir deger bu anahtarla cozulemiyorsa yuksek sesle uyarir.</summary>
    private static async Task VerifyKeyAsync(ApplicationDbContext db, FieldEncryption enc, ILogger logger, CancellationToken ct)
    {
        var sample = await ScalarAsync(db,
            $"SELECT \"Value\" FROM \"AppSettings\" WHERE \"Value\" LIKE '{FieldEncryption.Prefix}%' " +
            $"UNION ALL SELECT \"Email\" FROM \"Contacts\" WHERE \"Email\" LIKE '{FieldEncryption.Prefix}%' LIMIT 1", ct);
        if (sample is null) return;

        try
        {
            enc.Decrypt(sample);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            logger.LogCritical("Şifreleme anahtarı veritabanındaki verilerle eşleşmiyor (anahtar {KeyId}). " +
                "Doğru anahtar dosyasını geri yükleyin; aksi halde şifreli alanlar okunamaz.", enc.KeyId);
        }
    }

    private static async Task<int> EncryptTableAsync(ApplicationDbContext db, FieldEncryption enc, Target t, CancellationToken ct)
    {
        var pending = string.Join(" OR ", t.Columns.Select(c => $"(\"{c}\" <> '' AND \"{c}\" NOT LIKE '{FieldEncryption.Prefix}%')"));
        if (t.HashColumn is not null)
            pending += $" OR (\"{t.HashSource}\" <> '' AND \"{t.HashColumn}\" IS NULL)";

        // Id sirasiyla sayfalanir: her satir bir calismada en fazla bir kez islenir. Islendikten sonra da
        // "bekliyor" gorunen satir (ör. yalnizca bosluktan olusan e-posta: indeksi hep bos) donguye sokmaz.
        var columns = string.Join(", ", t.Columns.Select(c => $"\"{c}\""));
        var updated = 0;
        var lastId = 0;

        while (true)
        {
            var select = $"SELECT \"Id\", {columns} FROM \"{t.Table}\" WHERE \"Id\" > {lastId} AND ({pending}) ORDER BY \"Id\" LIMIT 500";
            var rows = await ReadRowsAsync(db, select, t.Columns.Length, ct);
            if (rows.Count == 0) break;
            lastId = rows[^1].Id;

            foreach (var (id, values) in rows)
            {
                // Bos degerler de yazilabilsin diye parametreler tipli (text) gonderilir.
                var sets = new List<string>();
                var args = new List<Npgsql.NpgsqlParameter>();
                void Set(string column, string? value)
                {
                    var name = $"p{args.Count}";
                    sets.Add($"\"{column}\" = @{name}");
                    args.Add(new Npgsql.NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value });
                }

                for (var i = 0; i < t.Columns.Length; i++)
                {
                    var plain = enc.Decrypt(values[i]); // zaten sifreliyse coz, degilse aynen
                    Set(t.Columns[i], enc.Encrypt(plain));
                    if (t.Columns[i] == t.HashSource) Set(t.HashColumn!, enc.Index(plain));
                }
                args.Add(new Npgsql.NpgsqlParameter("id", id));
                await db.Database.ExecuteSqlRawAsync(
                    $"UPDATE \"{t.Table}\" SET {string.Join(", ", sets)} WHERE \"Id\" = @id", args, ct);
                updated++;
            }

            if (rows.Count < 500) break;
        }

        return updated;
    }

    private static async Task<int> EncryptSettingsAsync(ApplicationDbContext db, FieldEncryption enc, CancellationToken ct)
    {
        var rows = await db.AppSettings.ToListAsync(ct);
        var changed = 0;
        foreach (var row in rows.Where(r => SettingKeys.IsSecret(r.Key) && !string.IsNullOrEmpty(r.Value) && !FieldEncryption.IsEncrypted(r.Value)))
        {
            row.Value = enc.Encrypt(row.Value);
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    private static async Task<List<(int Id, string?[] Values)>> ReadRowsAsync(ApplicationDbContext db, string sql, int columns, CancellationToken ct)
    {
        var result = new List<(int, string?[])>();
        var conn = db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            await using DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var values = new string?[columns];
                for (var i = 0; i < columns; i++) values[i] = reader.IsDBNull(i + 1) ? null : reader.GetString(i + 1);
                result.Add((reader.GetInt32(0), values));
            }
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
        return result;
    }

    private static async Task<string?> ScalarAsync(ApplicationDbContext db, string sql, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            await using DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return await cmd.ExecuteScalarAsync(ct) as string;
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }
}
