using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.Inbox;
using EgebisLeadFinder.Services.Sequences;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class InboxTests
{
    // ---------------- Cevabin yalniz yeni yazilan kismi

    [Fact]
    public void Clean_cuts_the_gmail_turkish_quote()
    {
        var body = "Merhaba,\nTeklifinizle ilgileniyoruz, salı arayabilir misiniz?\n\nAli\n\n" +
                   "6 Kas 2026 Per, 10:12 tarihinde Egebis <pazarlama@egebis.com.tr> şunu yazdı:\n> Sayın Ali Bey,\n> SAP ...";
        Assert.Equal("Merhaba,\nTeklifinizle ilgileniyoruz, salı arayabilir misiniz?\n\nAli", ReplyText.Clean(body));
    }

    [Fact]
    public void Clean_cuts_english_and_outlook_quotes_and_quoted_lines()
    {
        Assert.Equal("Thanks, send the price list.", ReplyText.Clean("Thanks, send the price list.\r\n\r\nOn Thu, Nov 6, 2026 at 10:12 AM Egebis <a@b.com> wrote:\r\n> hello"));
        Assert.Equal("Uygun değiliz.", ReplyText.Clean("Uygun değiliz.\n\nKimden: Egebis <a@b.com>\nGönderildi: 6 Kasım 2026\nKime: ali@firma.com\nKonu: SAP"));
        Assert.Equal("Tamam.", ReplyText.Clean("Tamam.\n> eski satır\n> eski satır"));
        Assert.Equal("Tamam.", ReplyText.Clean("Tamam.\n\n-----Original Message-----\nFrom: x"));
    }

    [Fact]
    public void Clean_keeps_a_body_without_quote_and_preview_is_one_line()
    {
        Assert.Equal("Sadece bu satır.\nVe bu.", ReplyText.Clean("Sadece bu satır.\nVe bu.\n\n\n"));
        Assert.Equal("a b c", ReplyText.Preview("a\n  b\tc"));
        Assert.EndsWith("…", ReplyText.Preview(new string('x', 400)));
    }

    // ---------------- Gelen cevap: sohbete kayit + bildirim

    private sealed record World(ApplicationDbContext Db, ReplyDetectionService Replies, NotificationService Notifications, InboxService Inbox, Lead Lead, AppUser Sender, AppUser Other);

    private static async Task<World> Setup(bool sentBySequence = false)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var sender = new AppUser { UserName = "selim", PasswordHash = "x", FullName = "Selim" };
        var other = new AppUser { UserName = "ayse", PasswordHash = "x", FullName = "Ayşe" };
        var passive = new AppUser { UserName = "eski", PasswordHash = "x", IsActive = false };
        db.Users.AddRange(sender, other, passive);
        var company = new Company { Name = "Arçelik", Domain = "arcelik.com.tr" };
        var contact = new Contact { Company = company, Name = "Ahmet Yılmaz", Title = "Satın Alma Müdürü", Email = "ahmet@arcelik.com.tr" };
        var lead = new Lead { Company = company, Contact = contact, SentAt = DateTime.UtcNow.AddDays(-1) };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        db.SentEmails.Add(new SentEmail
        {
            LeadId = lead.Id, ToAddress = "ahmet@arcelik.com.tr", Subject = "SAP entegrasyonu", Body = "Sayın Ahmet Bey, ...",
            SentAt = DateTime.UtcNow.AddDays(-1), MessageId = "<abc123@egebis.com.tr>",
            SentByUserId = sentBySequence ? null : sender.Id, SequenceStepId = sentBySequence ? 1 : null
        });
        await db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var notifications = new NotificationService(db, cache);
        var sequences = new SequenceService(db, null!, null!, null!, null!, NullLogger<SequenceService>.Instance);
        var replies = new ReplyDetectionService(db, null!, sequences, NullLogger<ReplyDetectionService>.Instance, notifications);
        return new World(db, replies, notifications, new InboxService(db, notifications), lead, sender, other);
    }

    private static InboundMessage Reply(DateTime? at = null, string id = "<r1@arcelik.com.tr>") => new(
        "ahmet@arcelik.com.tr", "Re: SAP entegrasyonu", at ?? DateTime.UtcNow, new[] { "<abc123@egebis.com.tr>" }, null,
        "Merhaba, ilgileniyoruz. Salı uygun.\n\nOn Wed, Nov 5, 2026 Egebis wrote:\n> Sayın Ahmet Bey", id, "Ahmet Yılmaz");

    [Fact]
    public async Task Reply_is_stored_without_the_quote_and_notifies_the_sender()
    {
        var w = await Setup();
        Assert.True(await w.Replies.HandleAsync(Reply(), CancellationToken.None));
        await w.Db.SaveChangesAsync();

        var inbound = Assert.Single(w.Db.InboundEmails);
        Assert.Equal("Merhaba, ilgileniyoruz. Salı uygun.", inbound.Body);
        Assert.Equal(InboundEmailKind.Reply, inbound.Kind);
        Assert.NotNull(inbound.SentEmailId);                       // cevaplanan mailimize baglandi
        Assert.Equal("r1@arcelik.com.tr", inbound.MessageId);

        var n = Assert.Single(w.Db.Notifications);                  // yalniz maili gonderene
        Assert.Equal(w.Sender.Id, n.UserId);
        Assert.Equal("Arçelik", n.CompanyName);
        Assert.Equal("Ahmet Yılmaz", n.PersonName);
        Assert.Contains("ilgileniyoruz", n.Preview);
        Assert.NotNull(w.Db.Leads.Single().RepliedAt);
        Assert.Equal(1, await w.Notifications.UnreadCountAsync(w.Sender.Id));
        Assert.Equal(0, await w.Notifications.UnreadCountAsync(w.Other.Id));
    }

    [Fact]
    public async Task Same_message_twice_is_stored_and_notified_once()
    {
        var w = await Setup();
        await w.Replies.HandleAsync(Reply(), CancellationToken.None);
        await w.Db.SaveChangesAsync();
        await w.Replies.HandleAsync(Reply(), CancellationToken.None);
        await w.Db.SaveChangesAsync();
        Assert.Single(w.Db.InboundEmails);
        Assert.Single(w.Db.Notifications);
    }

    [Fact]
    public async Task Sequence_mail_reply_notifies_every_active_user()
    {
        var w = await Setup(sentBySequence: true);
        await w.Replies.HandleAsync(Reply(), CancellationToken.None);
        await w.Db.SaveChangesAsync();
        Assert.Equal(new[] { w.Sender.Id, w.Other.Id }.OrderBy(x => x), w.Db.Notifications.Select(n => n.UserId).OrderBy(x => x));
    }

    [Fact]
    public async Task Old_reply_found_by_backfill_goes_to_the_chat_but_does_not_notify()
    {
        var w = await Setup();
        w.Lead.SentAt = DateTime.UtcNow.AddDays(-20);
        foreach (var e in w.Db.SentEmails) e.SentAt = DateTime.UtcNow.AddDays(-20);
        await w.Db.SaveChangesAsync();

        await w.Replies.HandleAsync(Reply(DateTime.UtcNow.AddDays(-10)), CancellationToken.None);
        await w.Db.SaveChangesAsync();
        Assert.Single(w.Db.InboundEmails);
        Assert.Empty(w.Db.Notifications);
    }

    [Fact]
    public async Task Auto_reply_is_shown_but_not_a_reply()
    {
        var w = await Setup();
        var auto = Reply() with { Subject = "Otomatik yanıt: izindeyim", MessageId = "<auto@x>" };
        Assert.False(await w.Replies.HandleAsync(auto, CancellationToken.None));
        await w.Db.SaveChangesAsync();
        Assert.Equal(InboundEmailKind.AutoReply, Assert.Single(w.Db.InboundEmails).Kind);
        Assert.Empty(w.Db.Notifications);
        Assert.Null(w.Db.Leads.Single().RepliedAt);
    }

    // ---------------- Mesajlar ekrani

    [Fact]
    public async Task Conversations_and_thread_show_both_sides_and_opening_marks_read()
    {
        var w = await Setup();
        await w.Replies.HandleAsync(Reply(), CancellationToken.None);
        await w.Db.SaveChangesAsync();

        var list = await w.Inbox.ConversationsAsync(InboxFilters.All, null);
        var c = Assert.Single(list);
        Assert.Equal("Arçelik", c.CompanyName);
        Assert.Equal("Ahmet Yılmaz", c.ContactName);
        Assert.True(c.LastFromThem);
        Assert.Equal(1, c.Unread);
        Assert.Single(await w.Inbox.ConversationsAsync(InboxFilters.Replied, null));
        Assert.Empty(await w.Inbox.ConversationsAsync(InboxFilters.Waiting, null));
        Assert.Single(await w.Inbox.ConversationsAsync(InboxFilters.All, "ahmet"));
        Assert.Empty(await w.Inbox.ConversationsAsync(InboxFilters.All, "vestel"));

        var thread = await w.Inbox.ThreadAsync(w.Lead.Id, w.Sender.Id);
        Assert.NotNull(thread);
        Assert.Equal(2, thread!.Messages.Count);
        Assert.True(thread.Messages[0].Ours);                       // once bizim mailimiz
        Assert.False(thread.Messages[1].Ours);                      // sonra cevap
        Assert.True(thread.Messages[1].Unread);

        Assert.Equal(0, await w.Inbox.UnreadRepliesAsync());        // sohbet acilinca okundu
        Assert.Equal(0, await w.Notifications.UnreadCountAsync(w.Sender.Id));
    }
}
