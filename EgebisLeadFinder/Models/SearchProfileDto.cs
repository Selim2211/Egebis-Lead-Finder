using System.Text.Json;
using EgebisLeadFinder.Data;

namespace EgebisLeadFinder.Models;

/// <summary>Arama profilinin ekrandaki hali: Goster/Duzenle pencereleri ve kaydetme cevabi bunu kullanir.</summary>
public record SearchProfileDto(
    int Id, string Name, string? Industry, string? NaceCode, string? NaceProducts, string? City,
    string RegionKey, string RegionName, string[] RegionKeys, int? MaxCompanies, bool IsPublic, bool CanEdit, string? OwnerName, string CreatedAt)
{
    public static SearchProfileDto From(SearchProfile p, int? me, bool isAdmin)
    {
        var region = SearchRegions.Get(p.RegionKey ?? SearchRegions.ByCountry(p.Country)?.Key);
        var many = SearchRegions.ParseKeys(p.RegionKeys);
        var keys = many.Count > 1 ? many.Select(r => r.Key).ToArray() : new[] { region.Key };
        return new SearchProfileDto(p.Id, p.Name, p.Industry, p.NaceCode, p.NaceProducts, many.Count > 1 ? null : p.City, region.Key,
            many.Count > 1 ? SearchRegions.Describe(many) : region.Name, keys,
            p.MaxCompanies, p.IsPublic, p.CanEdit(me, isAdmin),
            p.OwnerUserId is not null && p.OwnerUserId != me ? p.Owner?.DisplayName ?? "başka kullanıcı" : null,
            p.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
    }

    /// <summary>HTML data-profile ozniteligi icin JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);
}

/// <summary>Arama Profili penceresinin baslangic verileri.</summary>
public record SearchProfileModalModel(SearchRegion Region, int DefaultMax, string? PrefillIndustry);
