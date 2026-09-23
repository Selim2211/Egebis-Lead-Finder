using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Yuksek SAP potansiyelli firmalar icin ek karar verici arama katmani.
/// LinkedIn arama sonuclarindan aday kisi bulur, Apollo ile kurumsal e-postasini
/// dogrulamaya calisir. Enrichment:Enabled=false iken devre disidir.
/// </summary>
public interface IContactEnrichmentService
{
    Task<EnrichmentResult> EnrichAsync(Company company, CancellationToken ct = default);
}

public class EnrichmentResult
{
    public List<Contact> Contacts { get; init; } = new();
    public string? Error { get; init; }
    public bool Success => Error is null;

    public static EnrichmentResult Empty => new();
    public static EnrichmentResult Failed(string error) => new() { Error = error };
}
