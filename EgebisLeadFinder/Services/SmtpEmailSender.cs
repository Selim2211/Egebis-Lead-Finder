using EgebisLeadFinder.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EgebisLeadFinder.Services;

/// <summary>Gonderim icin cozulmus SMTP ayarlari (once Ayarlar ekrani, sonra appsettings).</summary>
public record SmtpSettings(
    string? FromAddress,
    string FromName,
    string? Host,
    int Port,
    string Security,
    string? Username,
    string? Password)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    /// <summary>"Egebis Bilişim &lt;satis@egebis.com&gt;" gibi gosterim.</summary>
    public string? Display => string.IsNullOrWhiteSpace(FromAddress)
        ? null
        : string.IsNullOrWhiteSpace(FromName) ? FromAddress : $"{FromName} <{FromAddress}>";
}

public record SendResult(bool Sent, string? Error, string? FromAddress, string? MessageId = null);

public interface IEmailSender
{
    Task<SmtpSettings> GetSettingsAsync(CancellationToken ct = default);
    /// <param name="body">Duz metin govde (HTML gostermeyen istemciler icin de kullanilir).</param>
    /// <param name="html">Temizlenmis HTML govde; null ise duz metin gonderilir.</param>
    /// <param name="images">HTML'de /Email/Image/{id} ile gecen gorseller.</param>
    Task<SendResult> SendAsync(string to, string subject, string body, string? html = null,
        IReadOnlyList<EmailImage>? images = null, CancellationToken ct = default);
}

/// <summary>
/// SMTP uzerinden tek tek mail gonderir. Toplu gonderim yok: her mail
/// kullanici tarafindan ekranda gorulup onaylanarak gonderilir. Gonderen
/// adres ve sunucu bilgileri Ayarlar ekranindan okunur.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly ISettingsService _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(ISettingsService settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<SmtpSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        async Task<string?> Get(string key)
        {
            var value = (await _settings.GetAsync(key, ct))?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        var from = await Get(SettingKeys.SmtpFromAddress);
        var port = int.TryParse(await Get(SettingKeys.SmtpPort), out var p) && p > 0 ? p : 587;

        var security = (await Get(SettingKeys.SmtpSecurity))?.ToLowerInvariant();
        if (security is not ("starttls" or "ssl" or "auto"))
        {
            // Eski appsettings "UseStartTls" alanina veya porta gore tahmin.
            var useStartTls = await Get("Smtp:UseStartTls");
            security = port == 465 ? "ssl"
                : string.Equals(useStartTls, "false", StringComparison.OrdinalIgnoreCase) ? "auto"
                : "starttls";
        }

        var username = await Get(SettingKeys.SmtpUsername);

        return new SmtpSettings(
            FromAddress: from,
            FromName: await Get(SettingKeys.SmtpFromName) ?? "Egebis Bilişim",
            Host: await Get(SettingKeys.SmtpHost),
            Port: port,
            Security: security,
            Username: username ?? from,
            Password: await Get(SettingKeys.SmtpPassword));
    }

    public async Task<SendResult> SendAsync(string to, string subject, string body, string? html = null,
        IReadOnlyList<EmailImage>? images = null, CancellationToken ct = default)
    {
        var s = await GetSettingsAsync(ct);

        if (!s.IsConfigured)
            return new SendResult(false, "Gönderen e-posta ayarlanmamış. Ayarlar → E-posta Gönderimi bölümünü doldurun.", null);

        if (string.IsNullOrWhiteSpace(to))
            return new SendResult(false, "Alıcı e-posta adresi yok.", s.FromAddress);

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(s.FromName, s.FromAddress));
            message.To.Add(MailboxAddress.Parse(to.Trim()));
            message.Subject = subject;
            message.Body = BuildBody(body, html, images);
            // Cevap geldiginde In-Reply-To bu kimligi tasir; ReplyDetectionService eslestirir.
            var domain = s.FromAddress!.Contains('@') ? s.FromAddress[(s.FromAddress.IndexOf('@') + 1)..] : "egebis.local";
            message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(domain);

            using var client = new SmtpClient { Timeout = 20000 };

            var socket = s.Security switch
            {
                "ssl" => SecureSocketOptions.SslOnConnect,
                "auto" => SecureSocketOptions.Auto,
                _ => SecureSocketOptions.StartTls
            };

            await client.ConnectAsync(s.Host, s.Port, socket, ct);

            if (!string.IsNullOrWhiteSpace(s.Password))
                await client.AuthenticateAsync(s.Username ?? s.FromAddress, s.Password, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            _logger.LogInformation("E-posta gönderildi: {From} → {To}", s.FromAddress, to);
            return new SendResult(true, null, s.FromAddress, message.MessageId);
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning(ex, "SMTP kimlik doğrulama hatası: {From}", s.FromAddress);
            return new SendResult(false,
                "E-posta sunucusu şifreyi kabul etmedi. Gmail/Outlook kullanıyorsanız normal şifre yerine " +
                "\"uygulama şifresi\" gerekir (Ayarlar → E-posta Gönderimi).", s.FromAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E-posta gönderilemedi: {To}", to);
            return new SendResult(false, ex.Message, s.FromAddress);
        }
    }

    /// <summary>
    /// HTML yoksa duz metin. HTML varsa HTML + duz metin alternatifi; editorde metnin
    /// icine yerlestirilen gorseller cid ile ayni yerde gomulu gider (ek olarak degil).
    /// </summary>
    public static MimeEntity BuildBody(string body, string? html, IReadOnlyList<EmailImage>? images)
    {
        if (string.IsNullOrWhiteSpace(html))
            return new TextPart("plain") { Text = body };

        var builder = new BodyBuilder { TextBody = body };
        var byId = (images ?? Array.Empty<EmailImage>()).ToDictionary(i => i.Id);
        var cids = new Dictionary<int, string>();

        builder.HtmlBody = EmailHtml.ToEmailHtml(html, id =>
        {
            if (cids.TryGetValue(id, out var existing)) return existing;
            if (!byId.TryGetValue(id, out var image)) return null;

            var resource = builder.LinkedResources.Add(
                SafeFileName(image), image.Data, ContentType.Parse(image.ContentType));
            resource.ContentId = MimeKit.Utils.MimeUtils.GenerateMessageId();
            return cids[id] = resource.ContentId;
        });

        return builder.ToMessageBody();
    }

    private static string SafeFileName(EmailImage image)
    {
        var ext = image.ContentType switch
        {
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            _ => ".png"
        };
        var name = new string(image.Name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return (string.IsNullOrEmpty(name) ? $"gorsel-{image.Id}" : name) + ext;
    }
}
