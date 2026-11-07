using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboundEmails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LeadId = table.Column<int>(type: "integer", nullable: false),
                    SentEmailId = table.Column<int>(type: "integer", nullable: true),
                    MessageId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FromAddress = table.Column<string>(type: "text", nullable: false),
                    FromName = table.Column<string>(type: "text", nullable: true),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboundEmails_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InboundEmails_SentEmails_SentEmailId",
                        column: x => x.SentEmailId,
                        principalTable: "SentEmails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LeadId = table.Column<int>(type: "integer", nullable: true),
                    CompanyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PersonName = table.Column<string>(type: "text", nullable: true),
                    Preview = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundEmails_LeadId_ReceivedAt",
                table: "InboundEmails",
                columns: new[] { "LeadId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundEmails_MessageId",
                table: "InboundEmails",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundEmails_ReadAt",
                table: "InboundEmails",
                column: "ReadAt");

            migrationBuilder.CreateIndex(
                name: "IX_InboundEmails_SentEmailId",
                table: "InboundEmails",
                column: "SentEmailId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadAt",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundEmails");

            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
