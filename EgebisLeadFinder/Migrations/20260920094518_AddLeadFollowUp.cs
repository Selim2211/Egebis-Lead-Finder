using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadFollowUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedAt",
                table: "Leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SnoozedUntil",
                table: "Leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.InsertData(
                table: "EmailTemplates",
                columns: new[] { "Id", "Active", "Body", "BodyHtml", "Key", "Name", "Subject", "UpdatedAt" },
                values: new object[] { 100, true, "Sayın {CONTACT_NAME},\n\nKısa bir süre önce {COMPANY_NAME} için gönderdiğimiz mesajı\nhatırlatmak istedik.\n\nKonu size uygun değilse bilgi vermeniz yeterli, takibi burada\nbırakalım. İlgilenmeniz halinde 20 dakikalık kısa bir görüşme\niçin uygun bir zaman önerebiliriz.\n\nSaygılarımızla,\nEgebis Bilişim", null, "TAKIP", "Takip Maili", "Önceki mesajımız hakkında — Egebis Bilişim", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "EmailTemplates",
                keyColumn: "Id",
                keyValue: 100);

            migrationBuilder.DropColumn(
                name: "RepliedAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SnoozedUntil",
                table: "Leads");
        }
    }
}
