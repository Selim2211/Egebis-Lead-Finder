using System.Text;
using System.Text.Json;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>"Biz ne arıyoruz?" profilinden e-posta taslaklari yazar (genel tanitim, segment basina, takip).</summary>
public interface ITemplateWriterAi
{
    /// <param name="onlyKeys">Verilirse yalnizca bu anahtarlarin taslaklari yazilir (bos = hepsi).</param>
    Task<TemplateDraftResult> DraftTemplatesAsync(BusinessProfile profile, IReadOnlyCollection<string>? onlyKeys = null, CancellationToken ct = default);
}

public class TemplateDraft
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public class TemplateDraftResult
{
    public List<TemplateDraft> Templates { get; set; } = new();
    public string? Error { get; set; }
}

public partial class GeminiAiService : ITemplateWriterAi
{
    /// <summary>Tek cagrida yazilacak en fazla segment taslagi (yanit uzunlugu ve kota icin).</summary>
    public const int MaxSegmentTemplates = 6;

    public async Task<TemplateDraftResult> DraftTemplatesAsync(BusinessProfile profile, IReadOnlyCollection<string>? onlyKeys = null, CancellationToken ct = default)
    {
        if (!profile.IsConfigured)
            return new TemplateDraftResult { Error = "Önce “Biz ne arıyoruz?” ekranında şirket profilinizi doldurun." };

        var keys = TemplateKeysFor(profile);
        if (onlyKeys is { Count: > 0 })
            keys = keys.Where(k => onlyKeys.Contains(k.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        if (keys.Count == 0) return new TemplateDraftResult { Error = "Yazılacak taslak yok." };
        var schema = new
        {
            type = "object",
            properties = new
            {
                templates = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            key = new { type = "string", @enum = keys.Select(k => k.Key).ToArray() },
                            name = new { type = "string", description = "Kısa taslak adı" },
                            subject = new { type = "string", description = "Konu satırı, en fazla 70 karakter" },
                            body = new { type = "string", description = "Düz metin e-posta gövdesi" }
                        },
                        required = new[] { "key", "name", "subject", "body" }
                    }
                }
            },
            required = new[] { "templates" }
        };

        var (json, error) = await AskJsonAsync(profile.ToPromptBlock() + "\n\n" + TemplateWriterPrompt(profile),
            BuildTemplateBrief(keys), schema, 0.4, ct);
        return error is not null ? new TemplateDraftResult { Error = error } : ParseTemplateDrafts(json!, keys.Select(k => k.Key));
    }

    /// <summary>Yazilacak taslaklar: genel tanitim, her segment (en fazla <see cref="MaxSegmentTemplates"/>), takip.</summary>
    public static List<(string Key, string Description)> TemplateKeysFor(BusinessProfile profile)
    {
        var keys = new List<(string, string)> { (EmailTemplate.GeneralKey, "Genel tanıtım: şirketimizi ve ana ürün/hizmetlerimizi tanıtan ilk temas maili") };
        keys.AddRange(profile.Segments
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .Take(MaxSegmentTemplates)
            .Select(s => (EmailTemplate.SegmentKey(s.Id),
                $"Segment \"{s.Name}\" için ilk temas maili{(string.IsNullOrWhiteSpace(s.Description) ? "" : ": " + s.Description)}")));
        keys.Add((EmailTemplate.FollowUpKey, "Takip: ilk maile cevap vermeyen kişiye kısa hatırlatma"));
        return keys;
    }

    public static string BuildTemplateBrief(IEnumerable<(string Key, string Description)> keys)
    {
        var sb = new StringBuilder("Her biri için bir taslak yaz (key alanına anahtarı aynen yaz):\n");
        foreach (var (key, description) in keys) sb.AppendLine($"- {key} — {description}");
        return sb.ToString();
    }

    public static string TemplateWriterPrompt(BusinessProfile profile) => $$"""
        {{profile.DisplayName}} satış ekibi için e-posta taslakları yazıyorsun. Taslaklar her potansiyel
        müşteriye otomatik doldurulacak. Şu yer tutucuları süslü parantezleriyle AYNEN kullan:
        {CONTACT_NAME} (alıcının adı soyadı), {COMPANY_NAME} (alıcının firması), {INDUSTRY}
        (alıcı firmanın sektörü), {CITY} (alıcı firmanın şehri). Başka yer tutucu kullanma.

        Kurallar:
        - "Sayın {CONTACT_NAME}," ile başla. İlk cümlede {COMPANY_NAME} ve {INDUSTRY} geçsin.
        - Yalnızca yukarıdaki şirket tanımındaki ürün/hizmetleri anlat; tanımda olmayan hizmet,
          rakam, referans veya ödül uydurma.
        - İlk temas mailleri 90-150 kelime; takip maili 50-80 kelime ve önceki maile atıf yapsın.
        - Tek net çağrı: kısa (15-20 dakikalık) bir görüşme ya da bilgi/katalog talebi.
        - Sonda "Saygılarımızla," ve alt satırda {{profile.DisplayName}}.
        - Konu satırı en fazla 70 karakter, kişisel ve tıklama tuzağı olmayan.
        - name alanı Taslak Düzenleyici'de görünecek kısa ad (ör. "Genel Tanıtım", segment adı, "Takip Maili").
        - body düz metindir: HTML, markdown veya köşeli parantezli yer tutucu kullanma.
        - Türkçe yaz.
        """;

    public static TemplateDraftResult ParseTemplateDrafts(string json, IEnumerable<string> allowedKeys)
    {
        var allowed = new HashSet<string>(allowedKeys, StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("templates", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return new TemplateDraftResult { Error = "Yapay zekâ taslak döndürmedi." };

            var result = new TemplateDraftResult();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in arr.EnumerateArray())
            {
                string Str(string p) => item.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
                var key = Str("key");
                var draft = new TemplateDraft { Key = key, Name = Str("name"), Subject = Str("subject"), Body = Str("body") };
                // Bilinmeyen anahtar, tekrar eden anahtar ya da bos taslak alinmaz.
                if (!allowed.Contains(key) || !seen.Add(key) || draft.Subject.Length == 0 || draft.Body.Length == 0) continue;
                if (draft.Name.Length == 0) draft.Name = key;
                result.Templates.Add(draft);
            }
            if (result.Templates.Count == 0) result.Error = "Yapay zekâ kullanılabilir taslak döndürmedi.";
            return result;
        }
        catch (JsonException)
        {
            return new TemplateDraftResult { Error = "Yapay zekâ yanıtı çözümlenemedi." };
        }
    }
}
