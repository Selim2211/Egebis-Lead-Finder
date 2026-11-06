using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using EgebisLeadFinder.Data;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services.Diagnostics;

/// <summary>
/// Sistem Teshisi: sunucuya erisimi olmayan yonetici icin, sunucudaki yavasligin kaynagini
/// uygulamanin icinden olcer (bellek/swap, disk yazma gecikmesi, veritabani gecikmesi, DNS ve dis
/// servis erisimi, son istek sureleri) ve son loglari okur. Ciktida sifre/anahtar yer almaz.
/// </summary>
public partial class DiagnosticsService
{
    private static readonly DateTime StartedAt = DateTime.UtcNow;
    private static readonly string[] ExternalHosts =
    {
        "https://fonts.googleapis.com", "https://cdn.jsdelivr.net", "https://api.groq.com",
        "https://generativelanguage.googleapis.com", "https://google.serper.dev", "https://api.apollo.io"
    };

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly RequestTimings _timings;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public DiagnosticsService(ApplicationDbContext db, IHttpClientFactory httpFactory, RequestTimings timings, IWebHostEnvironment env, IConfiguration config)
    {
        _config = config;
        _db = db;
        _httpFactory = httpFactory;
        _timings = timings;
        _env = env;
    }

    public async Task<string> RunAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Lead Finder sistem teşhisi — {DateTime.Now:dd.MM.yyyy HH:mm:ss} (sunucu saati), UTC {DateTime.UtcNow:HH:mm:ss}");

        Section(sb, "Uygulama");
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        sb.AppendLine($"Sürüm: {version}   Ortam: {_env.EnvironmentName}   .NET {Environment.Version}");
        sb.AppendLine($"İşletim sistemi: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})   Konteyner: {(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ? "evet" : "hayır")}");
        sb.AppendLine($"Çalışma süresi: {Human(DateTime.UtcNow - StartedAt)}   İşlemci sayısı: {Environment.ProcessorCount}");
        using (var p = Process.GetCurrentProcess())
            sb.AppendLine($"Uygulama belleği: {p.WorkingSet64 / 1048576} MB   GC heap: {GC.GetTotalMemory(false) / 1048576} MB   Thread: {p.Threads.Count}");
        ThreadPool.GetAvailableThreads(out var freeWorkers, out _);
        ThreadPool.GetMaxThreads(out var maxWorkers, out _);
        sb.AppendLine($"Thread pool kuyruğu: {ThreadPool.PendingWorkItemCount}   meşgul worker: {maxWorkers - freeWorkers}");

        if (OperatingSystem.IsLinux()) await LinuxSection(sb, ct);

        Section(sb, "Disk");
        DiskSection(sb);

        Section(sb, "Oturum anahtarları (\"Beni hatırla\" bunlara bağlı)");
        KeysSection(sb);

        Section(sb, "Veritabanı");
        await DatabaseSection(sb, ct);

        Section(sb, "Sunucudan dış servislere erişim (DNS + HTTPS)");
        var results = await Task.WhenAll(ExternalHosts.Select(h => ProbeAsync(h, ct)));
        foreach (var r in results) sb.AppendLine(r);

        Section(sb, "Son isteklerin sunucu işleme süresi (ağ hariç)");
        RequestSection(sb);

        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title) => sb.AppendLine().AppendLine($"==== {title} ====");

    private static async Task LinuxSection(StringBuilder sb, CancellationToken ct)
    {
        Section(sb, "Sunucu yükü (Linux)");
        var load = Read("/proc/loadavg")?.Split(' ');
        if (load is { Length: >= 4 })
            sb.AppendLine($"Yük ortalaması (1/5/15 dk): {load[0]} {load[1]} {load[2]}   (işlemci sayısı {Environment.ProcessorCount}; yük bunu aşarsa işler sıra bekler)   " +
                          $"şu an çalışan/toplam iş parçacığı: {load[3]}");

        // 2 sn boyunca makinenin TUM islemci zamani nereye gidiyor? (uygulama mi, baska programlar mi,
        // disk beklemesi mi, sanal makinede "steal" = fiziksel sunucunun baska VM'lere verdigi zaman mi)
        var before = CpuTimes();
        var vmBefore = VmStat();
        var diskBefore = DiskStats();
        var ioBefore = SelfIo();
        var procBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var sw = Stopwatch.StartNew();
        await Task.Delay(2000, ct);
        var after = CpuTimes();
        var vmAfter = VmStat();
        var diskAfter = DiskStats();
        var ioAfter = SelfIo();
        var appCpu = (Process.GetCurrentProcess().TotalProcessorTime - procBefore).TotalMilliseconds / (sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100;
        if (before is not null && after is not null)
        {
            var d = after.Zip(before, (a, b) => (double)(a - b)).ToArray();
            var total = d.Sum();
            double Pct(params int[] idx) => total > 0 ? idx.Sum(i => d[i]) / total * 100 : 0;
            // /proc/stat cpu: user nice system idle iowait irq softirq steal
            var busy = Pct(0, 1, 2, 5, 6);
            sb.AppendLine($"İşlemci (2 sn ölçüm, tüm makine): dolu %{busy:0} (kullanıcı %{Pct(0, 1):0}, sistem %{Pct(2, 5, 6):0}), " +
                          $"disk bekleme %{Pct(4):0}, steal %{Pct(7):0}, boşta %{Pct(3):0}");
            sb.AppendLine($"Bu uygulamanın payı: %{appCpu:0.0}  → makinedeki yükün {(busy - appCpu > 30 ? "BÜYÜK KISMI BAŞKA PROGRAMLARDAN" : "kaynağı belirsiz")}");
            if (Pct(7) > 10) sb.AppendLine("UYARI: steal yüksek — sanal makinenin bulunduğu fiziksel sunucu aşırı yüklü (BT/sanallaştırma tarafı).");
        }
        // Bellek <-> disk trafigi: swap'e yazilip okunuyorsa (pswpin/out) ya da dosyadan sayfa okunuyorsa
        // (pgmajfault) bos duran programlar disk beklerken takilir.
        if (vmBefore is not null && vmAfter is not null)
        {
            long Dv(string k) => vmAfter.GetValueOrDefault(k) - vmBefore.GetValueOrDefault(k);
            sb.AppendLine($"Bellek trafiği (2 sn): swap'ten okunan {Dv("pswpin")} sayfa, swap'e yazılan {Dv("pswpout")} sayfa, diskten okunan büyük sayfa hatası {Dv("pgmajfault")}");
        }
        var status = Read("/proc/self/status");
        if (status is not null)
            sb.AppendLine($"Bu uygulamanın swap'e atılmış belleği: {Regex.Match(status, @"VmSwap:\s+(\d+) kB").Groups[1].Value} kB (0 değilse uygulamanın sayfaları diskte; her istekte geri okunur)");
        if (ioBefore is not null && ioAfter is not null)
            sb.AppendLine($"Bu uygulamanın disk G/Ç'si (2 sn): okunan {(ioAfter.Value.Read - ioBefore.Value.Read) / 1024} KB, yazılan {(ioAfter.Value.Write - ioBefore.Value.Write) / 1024} KB");
        if (diskBefore is not null && diskAfter is not null)
            foreach (var (dev, a) in diskAfter.OrderBy(k => k.Key))
            {
                if (!diskBefore.TryGetValue(dev, out var b)) continue;
                var ios = (a.Reads - b.Reads) + (a.Writes - b.Writes);
                if (ios == 0) continue;
                // diskstats: okuma/yazma sayisi ve toplam bekleme suresi (ms) -> islem basina ortalama gecikme
                var rLat = a.Reads > b.Reads ? (double)(a.ReadMs - b.ReadMs) / (a.Reads - b.Reads) : 0;
                var wLat = a.Writes > b.Writes ? (double)(a.WriteMs - b.WriteMs) / (a.Writes - b.Writes) : 0;
                var busyPct = (a.IoMs - b.IoMs) / 2000.0 * 100;
                sb.AppendLine($"Disk {dev}: {a.Reads - b.Reads} okuma (ort. {rLat:0.0} ms), {a.Writes - b.Writes} yazma (ort. {wLat:0.0} ms), meşgul %{Math.Min(100, busyPct):0}  → {(wLat > 50 || rLat > 50 ? "YAVAŞ DİSK" : "normal")}");
            }
        var mem = Read("/proc/meminfo");
        if (mem is not null)
        {
            long Kb(string name) => long.TryParse(Regex.Match(mem, name + @":\s+(\d+)").Groups[1].Value, out var v) ? v : 0;
            sb.AppendLine($"Bellek: toplam {Kb("MemTotal") / 1024} MB, kullanılabilir {Kb("MemAvailable") / 1024} MB   " +
                          $"Swap: toplam {Kb("SwapTotal") / 1024} MB, kullanılan {(Kb("SwapTotal") - Kb("SwapFree")) / 1024} MB");
        }
        var memMax = Read("/sys/fs/cgroup/memory.max")?.Trim();
        var memCur = Read("/sys/fs/cgroup/memory.current")?.Trim();
        if (memCur is not null)
            sb.AppendLine($"Konteyner bellek: kullanılan {long.Parse(memCur) / 1048576} MB, sınır {(memMax is null or "max" ? "yok" : long.Parse(memMax) / 1048576 + " MB")}");
        var cpuMax = Read("/sys/fs/cgroup/cpu.max")?.Trim();
        if (cpuMax is not null) sb.AppendLine($"Konteyner CPU sınırı: {(cpuMax.StartsWith("max") ? "yok" : cpuMax)}");
        var cpuStat = Read("/sys/fs/cgroup/cpu.stat");
        if (cpuStat is not null)
        {
            var throttled = Regex.Match(cpuStat, @"nr_throttled (\d+)").Groups[1].Value;
            var periods = Regex.Match(cpuStat, @"nr_periods (\d+)").Groups[1].Value;
            sb.AppendLine($"CPU kısıtlaması: {throttled} / {periods} periyotta kısıldı");
        }
        // PSI: kaynak beklerken gecen zaman yuzdesi. io avg10 > 10 ise disk darbogazi demektir.
        foreach (var r in new[] { "cpu", "memory", "io" })
        {
            var psi = Read($"/proc/pressure/{r}");
            if (psi is null) continue;
            var some = Regex.Match(psi, @"some avg10=([\d.]+) avg60=([\d.]+) avg300=([\d.]+)");
            if (some.Success)
                sb.AppendLine($"Bekleme baskısı {r,-6}: son 10 sn %{some.Groups[1].Value}, 1 dk %{some.Groups[2].Value}, 5 dk %{some.Groups[3].Value}");
        }
        sb.AppendLine("(Bekleme baskısı: işlemlerin CPU/bellek/disk beklerken geçirdiği süre. %10 üstü darboğaz demektir.)");
    }

    /// <summary>
    /// Oturum cerezi bu anahtarlarla sifrelenir. Anahtarlar kalici klasorde degilse (ya da deploy'da
    /// klasor/volume silinirse) her yeniden baslatmada herkesin oturumu duser, "beni hatirla" calismaz.
    /// </summary>
    private void KeysSection(StringBuilder sb)
    {
        var path = _config["DataProtection:KeysPath"];
        sb.AppendLine($"Anahtar klasörü ayarı: {(string.IsNullOrWhiteSpace(path) ? "YOK (anahtarlar yalnızca bellekte; her yeniden başlatmada oturumlar düşer)" : path)}");
        sb.AppendLine($"Uygulama başlangıcı: {StartedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (!Directory.Exists(path)) { sb.AppendLine("Klasör YOK."); return; }
            var files = new DirectoryInfo(path).GetFiles("key-*.xml").OrderBy(f => f.CreationTimeUtc).ToList();
            sb.AppendLine($"Anahtar dosyası: {files.Count} adet" + (files.Count > 0
                ? $", en eskisi {files[0].CreationTime:dd.MM.yyyy HH:mm}, en yenisi {files[^1].CreationTime:dd.MM.yyyy HH:mm}" : " (hiç yok)"));
            var probe = Path.Combine(path, ".yazma-testi-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            sb.AppendLine("Klasöre yazılabiliyor: evet");
            if (files.Count > 0 && files[0].CreationTimeUtc > DateTime.UtcNow.AddDays(-1))
                sb.AppendLine("DİKKAT: en eski anahtar 1 günden yeni → anahtarlar yakın zamanda sıfırlanmış olabilir (deploy'da volume silinmiş mi?). Bu durumda herkes oturumdan düşer.");
        }
        catch (Exception ex)
        {
            sb.AppendLine("Anahtar klasörü okunamadı/yazılamadı: " + ex.Message);
        }
    }

    private void DiskSection(StringBuilder sb)
    {
        try
        {
            var root = _env.ContentRootPath;
            var drive = new DriveInfo(Path.GetPathRoot(root) ?? "/");
            var pct = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
            sb.AppendLine($"Uygulama diski: {drive.AvailableFreeSpace / 1073741824.0:0.0} GB boş / {drive.TotalSize / 1073741824.0:0.0} GB (doluluk %{pct:0})");

            var file = Path.Combine(Path.GetTempPath(), "lf-disktest-" + Guid.NewGuid().ToString("N"));
            var buffer = new byte[1024 * 1024];
            Random.Shared.NextBytes(buffer);
            var sw = Stopwatch.StartNew();
            using (var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                for (var i = 0; i < 8; i++) fs.Write(buffer);
                fs.Flush(true);
            }
            var writeMs = sw.Elapsed.TotalMilliseconds;
            File.Delete(file);
            sb.AppendLine($"Disk yazma testi (8 MB, senkron): {writeMs:0} ms  → {(writeMs > 1500 ? "YAVAŞ" : writeMs > 400 ? "orta" : "iyi")}");

            var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            if (Directory.Exists(logDir))
            {
                var files = new DirectoryInfo(logDir).GetFiles("*.log");
                sb.AppendLine($"Uygulama log klasörü: {files.Length} dosya, {files.Sum(f => f.Length) / 1048576.0:0.0} MB");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("Disk ölçülemedi: " + ex.Message);
        }
    }

    private async Task DatabaseSection(StringBuilder sb, CancellationToken ct)
    {
        try
        {
            var conn = _db.Database.GetDbConnection();
            sb.AppendLine($"Sunucu: {conn.DataSource}   Veritabanı: {conn.Database}");
            var sw = Stopwatch.StartNew();
            await conn.OpenAsync(ct);
            sb.AppendLine($"Bağlantı açma: {sw.Elapsed.TotalMilliseconds:0} ms");

            var times = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                sw.Restart();
                await Scalar(conn, "select 1", ct);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            sb.AppendLine($"Basit sorgu (5 kez): ortalama {times.Average():0.0} ms, en kötü {times.Max():0.0} ms");

            // Yazma gecikmesi: PostgreSQL her kayit icin diske senkron (fsync) yazar. Yavas diskte ilk belirti budur.
            var commits = new List<double>();
            for (var i = 0; i < 3; i++)
            {
                sw.Restart();
                await using var tx = await conn.BeginTransactionAsync(ct);
                await using (var c = conn.CreateCommand())
                {
                    c.Transaction = tx;
                    c.CommandText = "create temp table if not exists lf_diag(i int); insert into lf_diag values (1);";
                    await c.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                commits.Add(sw.Elapsed.TotalMilliseconds);
            }
            sb.AppendLine($"Veritabanı yazma+onay (3 kez): ortalama {commits.Average():0.0} ms, en kötü {commits.Max():0.0} ms  → {(commits.Max() > 100 ? "YAVAŞ (disk yazması bekliyor)" : "normal")}");
            sb.AppendLine($"Veritabanı blok okuması: {await Scalar(conn, "select blks_read || ' diskten / ' || blks_hit || ' bellekten' from pg_stat_database where datname = current_database()", ct)}");

            sw.Restart();
            var companies = await _db.Companies.CountAsync(ct);
            sb.AppendLine($"Firma sayımı: {companies} firma, {sw.Elapsed.TotalMilliseconds:0} ms");
            sb.AppendLine($"Veritabanı boyutu: {await Scalar(conn, "select pg_size_pretty(pg_database_size(current_database()))", ct)}");
            sb.AppendLine($"Açık bağlantı: {await Scalar(conn, "select count(*) from pg_stat_activity where datname = current_database()", ct)}");
            sb.AppendLine($"Önbellek isabeti: %{await Scalar(conn, "select round(100.0 * sum(blks_hit) / nullif(sum(blks_hit) + sum(blks_read), 0), 1) from pg_stat_database where datname = current_database()", ct)}");
            sb.AppendLine($"Şu an çalışan sorgu: {await Scalar(conn, "select count(*) from pg_stat_activity where datname = current_database() and state = 'active' and pid <> pg_backend_pid()", ct)}   " +
                          $"en uzun süren: {await Scalar(conn, "select coalesce(round(extract(epoch from max(now() - query_start)) * 1000), 0) || ' ms' from pg_stat_activity where datname = current_database() and state = 'active' and pid <> pg_backend_pid()", ct)}");
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "select relname, n_live_tup, n_dead_tup from pg_stat_user_tables order by n_live_tup desc limit 6";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                var rows = new List<string>();
                while (await reader.ReadAsync(ct)) rows.Add($"{reader.GetString(0)} {reader.GetInt64(1)} (ölü {reader.GetInt64(2)})");
                sb.AppendLine("En büyük tablolar: " + string.Join(", ", rows));
            }
            await conn.CloseAsync();
        }
        catch (Exception ex)
        {
            sb.AppendLine("Veritabanı ölçülemedi: " + ex.Message);
        }
    }

    private static async Task<string?> Scalar(System.Data.Common.DbConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (await cmd.ExecuteScalarAsync(ct))?.ToString();
    }

    private async Task<string> ProbeAsync(string url, CancellationToken ct)
    {
        var host = new Uri(url).Host;
        var sw = Stopwatch.StartNew();
        string dns;
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, ct);
            dns = $"DNS {sw.Elapsed.TotalMilliseconds:0} ms ({addrs.Count(a => a.AddressFamily == AddressFamily.InterNetwork)} IPv4, {addrs.Count(a => a.AddressFamily == AddressFamily.InterNetworkV6)} IPv6)";
        }
        catch (Exception ex)
        {
            return $"{host,-36} DNS ÇÖZÜLEMEDİ ({sw.Elapsed.TotalMilliseconds:0} ms): {ex.Message}";
        }

        sw.Restart();
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var ms = sw.Elapsed.TotalMilliseconds;
            return $"{host,-36} {dns}  HTTPS {ms:0} ms (HTTP {(int)resp.StatusCode}){(ms > 2000 ? "  → YAVAŞ" : "")}";
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return $"{host,-36} {dns}  HTTPS ZAMAN AŞIMI (10 sn)";
        }
        catch (Exception ex)
        {
            return $"{host,-36} {dns}  HTTPS HATA ({sw.Elapsed.TotalMilliseconds:0} ms): {ex.GetBaseException().Message}";
        }
    }

    private void RequestSection(StringBuilder sb)
    {
        var list = _timings.Snapshot();
        if (list.Count == 0)
        {
            sb.AppendLine("Henüz kayıt yok (uygulama yeni başladı).");
            return;
        }
        var ms = list.Select(e => e.Ms).OrderBy(x => x).ToList();
        double P(double q) => ms[Math.Min(ms.Count - 1, (int)(q * ms.Count))];
        sb.AppendLine($"Son {list.Count} istek ({list[0].At.ToLocalTime():dd.MM HH:mm} – {list[^1].At.ToLocalTime():HH:mm}): " +
                      $"ortanca {P(0.5):0} ms, %95 {P(0.95):0} ms, en yavaş {ms[^1]:0} ms, 1 sn üstü {ms.Count(x => x > 1000)} istek");
        sb.AppendLine("En yavaş 10 istek:");
        foreach (var e in list.OrderByDescending(e => e.Ms).Take(10))
            sb.AppendLine($"  {e.At.ToLocalTime():dd.MM HH:mm:ss}  {e.Ms,7:0} ms  {e.Status}  {e.Method} {Truncate(e.Path, 80)}");
    }

    /// <summary>Son log satirlari (en yeni dosyadan). onlyProblems: yalnizca WRN/ERR/FTL ve devam satirlari.</summary>
    public string ReadLog(int lines, bool onlyProblems)
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        if (!Directory.Exists(dir)) return "Log klasörü bulunamadı: " + dir;
        var files = new DirectoryInfo(dir).GetFiles("egebis-*.log").OrderByDescending(f => f.Name).Take(2).Reverse().ToList();
        if (files.Count == 0) return "Log dosyası yok.";

        var all = new List<string>();
        foreach (var f in files)
        {
            using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > 8 * 1024 * 1024) fs.Seek(-8 * 1024 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(fs);
            all.AddRange(reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')));
        }

        if (onlyProblems)
        {
            var kept = new List<string>();
            var inProblem = false;
            foreach (var l in all)
            {
                var isEntry = EntryStart().IsMatch(l);
                if (isEntry) inProblem = ProblemLevel().IsMatch(l);
                if (inProblem) kept.Add(l);
            }
            all = kept;
        }

        var tail = all.Where(l => l.Length > 0).TakeLast(lines).Select(Mask);
        return $"{string.Join(", ", files.Select(f => f.Name))} — son {lines} satır{(onlyProblems ? " (yalnızca uyarı/hata)" : "")}\n\n"
               + string.Join('\n', tail);
    }

    /// <summary>Loga yanlislikla dusmus olabilecek anahtar/sifre benzeri degerleri maskeler.</summary>
    public static string Mask(string line) => SecretPattern().Replace(line, m => m.Groups[1].Value + "***");

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private static string Human(TimeSpan t) => t.TotalDays >= 1 ? $"{(int)t.TotalDays} gün {t.Hours} sa" : t.TotalHours >= 1 ? $"{(int)t.TotalHours} sa {t.Minutes} dk" : $"{t.Minutes} dk";

    private static Dictionary<string, long>? VmStat()
    {
        var text = Read("/proc/vmstat");
        if (text is null) return null;
        var d = new Dictionary<string, long>();
        foreach (var line in text.Split('\n'))
        {
            var p = line.Split(' ');
            if (p.Length == 2 && long.TryParse(p[1], out var v)) d[p[0]] = v;
        }
        return d;
    }

    private static (long Read, long Write)? SelfIo()
    {
        var text = Read("/proc/self/io");
        if (text is null) return null;
        long V(string k) => long.TryParse(Regex.Match(text, k + @":\s+(\d+)").Groups[1].Value, out var x) ? x : 0;
        return (V("read_bytes"), V("write_bytes"));
    }

    private record DiskStat(long Reads, long ReadMs, long Writes, long WriteMs, long IoMs);

    private static Dictionary<string, DiskStat>? DiskStats()
    {
        var text = Read("/proc/diskstats");
        if (text is null) return null;
        var d = new Dictionary<string, DiskStat>();
        foreach (var line in text.Split('\n'))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 13) continue;
            var name = p[2];
            if (name.StartsWith("loop") || name.StartsWith("ram")) continue;
            if (long.TryParse(p[3], out var r) && long.TryParse(p[6], out var rms) && long.TryParse(p[7], out var w)
                && long.TryParse(p[10], out var wms) && long.TryParse(p[12], out var io))
                d[name] = new DiskStat(r, rms, w, wms, io);
        }
        return d;
    }

    private static long[]? CpuTimes()
    {
        var line = Read("/proc/stat")?.Split('\n').FirstOrDefault(l => l.StartsWith("cpu "));
        if (line is null) return null;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(long.Parse).ToArray();
        return parts.Length == 8 ? parts : null;
    }

    private static string? Read(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}")]
    private static partial Regex EntryStart();

    [GeneratedRegex(@"\[(WRN|ERR|FTL)\]")]
    private static partial Regex ProblemLevel();

    [GeneratedRegex(@"(?i)((?:api[_-]?key|x-api-key|key|password|pwd|token|secret|bearer)\s*[=:]?\s*""?)[A-Za-z0-9_\-\.]{12,}")]
    private static partial Regex SecretPattern();
}
