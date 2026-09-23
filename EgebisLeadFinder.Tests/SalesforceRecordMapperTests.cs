using System.Text.Json;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

/// <summary>Egebis kaydi -> Salesforce alanlari: derin analiz, e-posta, telefon, son mail.</summary>
public class SalesforceRecordMapperTests
{
    [Theory]
    [InlineData("ali@firma.com", "ali@firma.com")]
    [InlineData("  ali@firma.com ", "ali@firma.com")]
    [InlineData("a***@firma.com", null)]
    [InlineData("gecersiz", null)]
    [InlineData(null, null)]
    public void Gecersiz_veya_maskeli_eposta_gonderilmez(string? input, string? expected) =>
        Assert.Equal(expected, SalesforceRecordMapper.ValidEmail(input));

    [Fact]
    public void Account_derin_analiz_bolumlerini_tasir()
    {
        var rating = new CompanyRating
        {
            Summary = "Köklü üretici.",
            SalesApproach = "Genel müdüre MES ile yaklaşın.",
            Confidence = 80,
            Management = { new ManagementPerson { Name = "Tevfik Ezik", Role = "Genel Müdür", SourceUrl = "https://h/1" } },
            Opportunities = { new Opportunity { Text = "MES", Reason = "Yeni fabrika" } },
            SizeInfo = new SizeInfo { Employees = "450" }
        };
        var company = new Company
        {
            Id = 7, Name = "Haksan", EmailSentAt = DateTime.UtcNow,
            RatingJson = JsonSerializer.Serialize(rating), RatedAt = new DateTime(2026, 9, 23, 17, 0, 0, DateTimeKind.Utc)
        };

        var f = SalesforceRecordMapper.Account(company);

        Assert.Equal("Mail atıldı", f["Egebis_Stage__c"]);
        Assert.Equal(80, f["Egebis_Confidence__c"]);
        Assert.Equal("2026-09-23T17:00:00Z", f["Egebis_Analyzed_At__c"]);
        Assert.Contains("Köklü üretici.", (string)f["Egebis_AI_Summary__c"]!);
        Assert.Contains("Tevfik Ezik — Genel Müdür", (string)f["Egebis_Management__c"]!);
        Assert.Contains("MES — Yeni fabrika", (string)f["Egebis_Opportunities__c"]!);
        Assert.Contains("Çalışan: 450", (string)f["Egebis_Financials__c"]!);
        Assert.Equal("Genel müdüre MES ile yaklaşın.", f["Egebis_Sales_Approach__c"]);
    }

    [Fact]
    public void Analizsiz_firmada_derin_alanlar_bos_kalir()
    {
        var f = SalesforceRecordMapper.Account(new Company { Name = "Yeni" });

        Assert.Null(f["Egebis_Management__c"]);
        Assert.Null(f["Egebis_Risks__c"]);
        Assert.Equal("Temas yok", f["Egebis_Stage__c"]);
    }

    [Fact]
    public void Lead_eposta_telefon_ve_son_mail_tarihini_tasir()
    {
        var lead = new Lead
        {
            Company = new Company { Name = "Haksan", City = "Bursa" },
            Contact = new Contact { Name = "Ali Veli", Email = "ali@haksan.com", Phone = "+90 224 000 00 00" },
            SentEmails =
            {
                new SentEmail { SentAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc) },
                new SentEmail { SentAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc) }
            }
        };

        var f = SalesforceRecordMapper.Lead(lead);

        Assert.Equal("ali@haksan.com", f["Email"]);
        Assert.Equal("+90 224 000 00 00", f["Phone"]);
        Assert.Equal("Veli", f["LastName"]);
        Assert.Equal("2026-09-20T08:00:00Z", f["Egebis_Last_Email_At__c"]);
    }

    [Fact]
    public void Kisi_eposta_acilinca_contact_kaydina_gider()
    {
        var contact = new Contact { Id = 3, Name = "Ayşe Gül", Title = "IT Müdürü", Email = "ayse@firma.com" };

        var f = SalesforceRecordMapper.Contact(contact, "001XX");

        Assert.Equal("001XX", f["AccountId"]);
        Assert.Equal("ayse@firma.com", f["Email"]);
    }
}
