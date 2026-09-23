using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Tests;

public class FollowUpTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private static Lead Lead(int daysAgo, Action<Lead>? tweak = null)
    {
        var lead = new Lead
        {
            Status = LeadStatus.Gonderildi,
            SentAt = Now.AddDays(-daysAgo)
        };
        tweak?.Invoke(lead);
        return lead;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(14, 14)]
    public void Bekleme_suresi_son_temastan_sayilir(int daysAgo, int expected)
    {
        Assert.Equal(expected, Lead(daysAgo).WaitingDays(Now));
    }

    [Fact]
    public void Hic_temas_yoksa_bekleme_suresi_yok()
    {
        var lead = new Lead();

        Assert.Null(lead.WaitingDays(Now));
        Assert.False(lead.NeedsFollowUp(5, Now));
    }

    [Fact]
    public void En_son_gonderilen_eposta_bekleme_suresini_belirler()
    {
        var lead = Lead(20, l => l.SentEmails.Add(new SentEmail { SentAt = Now.AddDays(-2) }));

        Assert.Equal(2, lead.WaitingDays(Now));
        Assert.False(lead.NeedsFollowUp(5, Now));
    }

    [Fact]
    public void Sadece_iletisim_kurulduysa_da_sayac_isler()
    {
        var lead = new Lead { Status = LeadStatus.Incelendi, ContactedAt = Now.AddDays(-9) };

        Assert.Equal(9, lead.WaitingDays(Now));
        Assert.True(lead.NeedsFollowUp(5, Now));
    }

    [Theory]
    [InlineData(4, false)]   // esik dolmadi
    [InlineData(5, true)]    // esik tam doldu
    [InlineData(9, true)]
    public void Esik_dolunca_takip_gerekir(int daysAgo, bool expected)
    {
        Assert.Equal(expected, Lead(daysAgo).NeedsFollowUp(5, Now));
    }

    [Fact]
    public void Cevap_gelmisse_takip_gerekmez()
    {
        Assert.False(Lead(30, l => l.RepliedAt = Now.AddDays(-1)).NeedsFollowUp(5, Now));
    }

    [Fact]
    public void Kapanmis_leadler_takip_listesine_girmez()
    {
        Assert.False(Lead(30, l => l.Status = LeadStatus.Ilgilenmedi).NeedsFollowUp(5, Now));
        Assert.False(Lead(30, l => l.Status = LeadStatus.Ilgilendi).NeedsFollowUp(5, Now));
    }

    [Fact]
    public void Erteleme_suresi_dolana_kadar_takip_gerekmez()
    {
        var snoozed = Lead(30, l => l.SnoozedUntil = Now.AddDays(2));
        var expired = Lead(30, l => l.SnoozedUntil = Now.AddDays(-1));

        Assert.False(snoozed.NeedsFollowUp(5, Now));
        Assert.True(expired.NeedsFollowUp(5, Now));
    }
}
