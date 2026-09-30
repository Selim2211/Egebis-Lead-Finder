using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Tests;

/// <summary>Faz-II madde 2: kullaniciya ozel favori listesi, istege bagli herkese acik.</summary>
public class FavoriteTests
{
    private static async Task<(ApplicationDbContext Db, AppUser Ali, AppUser Ayse, Company Firma)> SeedAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var ali = new AppUser { UserName = "ali", FullName = "Ali Kaya", PasswordHash = "x" };
        var ayse = new AppUser { UserName = "ayse", PasswordHash = "x" };
        var firma = new Company { Name = "Ege Plastik", Domain = "egeplastik.com" };
        db.AddRange(ali, ayse, firma);
        await db.SaveChangesAsync();
        return (db, ali, ayse, firma);
    }

    [Fact]
    public async Task Yildiz_ekler_ve_tekrar_basinca_cikarir()
    {
        var (db, ali, _, firma) = await SeedAsync();
        await using var _ = db;
        var service = new FavoriteService(db);

        Assert.True(await service.ToggleAsync(ali.Id, firma.Id));
        Assert.True(await service.IsFavoriteAsync(ali.Id, firma.Id));
        Assert.Equal(new[] { firma.Id }, await service.FavoriteIdsAsync(ali.Id, new[] { firma.Id, 999 }));

        Assert.False(await service.ToggleAsync(ali.Id, firma.Id));
        Assert.False(await service.IsFavoriteAsync(ali.Id, firma.Id));
        Assert.Null(await service.ToggleAsync(ali.Id, 999));
    }

    [Fact]
    public async Task Liste_varsayilan_kisiye_ozel_herkese_acilinca_gorunur()
    {
        var (db, ali, ayse, firma) = await SeedAsync();
        await using var _ = db;
        var service = new FavoriteService(db);
        await service.ToggleAsync(ali.Id, firma.Id);

        // Ali'nin listesi ozel: Ayse goremez, listelerde de cikmaz.
        Assert.False(await service.CanViewAsync(ayse.Id, ali.Id));
        Assert.Equal(new[] { ayse.Id }, (await service.ListsAsync(ayse.Id)).Select(l => l.UserId));

        await service.SetPublicAsync(ali.Id, true);

        Assert.True(await service.CanViewAsync(ayse.Id, ali.Id));
        var lists = await service.ListsAsync(ayse.Id);
        Assert.Equal(ayse.Id, lists[0].UserId); // kendi listesi en ustte
        var shared = Assert.Single(lists, l => l.UserId == ali.Id);
        Assert.Equal("Ali Kaya", shared.OwnerName);
        Assert.Equal(1, shared.Count);
        Assert.Single(await service.EntriesAsync(ali.Id));
    }
}
