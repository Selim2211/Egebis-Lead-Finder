using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>
/// E-posta metnine gomulen gorsel. Kutuphanedekiler (logo vb.) tekrar tekrar eklenebilir;
/// metne dogrudan yapistirilanlar kutuphanede listelenmez ama e-posta gecmisi icin saklanir.
/// </summary>
public class EmailImage
{
    public const long MaxBytes = 2 * 1024 * 1024;

    public static readonly string[] AllowedContentTypes = { "image/png", "image/jpeg", "image/gif" };

    public int Id { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = "image/png";

    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>"Kayitli gorseller" listesinde gorunsun mu (yapistirilanlar false).</summary>
    public bool InLibrary { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
