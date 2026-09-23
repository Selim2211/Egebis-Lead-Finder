using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitorFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MessageId",
                table: "SentEmails",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SequenceStepId",
                table: "SentEmails",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailCheckedAt",
                table: "Contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmailStatus",
                table: "Contacts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EmailStatusReason",
                table: "Contacts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IcpMatch",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NaceCode",
                table: "Companies",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeadSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LeadId = table.Column<int>(type: "integer", nullable: false),
                    SequenceId = table.Column<int>(type: "integer", nullable: false),
                    CurrentStep = table.Column<int>(type: "integer", nullable: false),
                    NextSendAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StopReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadSequences_EmailSequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "EmailSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeadSequences_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SequenceSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SequenceId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    DelayDays = table.Column<int>(type: "integer", nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: true),
                    UseAi = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SequenceSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SequenceSteps_EmailSequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "EmailSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SequenceSteps_EmailTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "EmailTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SentEmails_MessageId",
                table: "SentEmails",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadSequences_LeadId",
                table: "LeadSequences",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadSequences_SequenceId",
                table: "LeadSequences",
                column: "SequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadSequences_Status_NextSendAt",
                table: "LeadSequences",
                columns: new[] { "Status", "NextSendAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SequenceSteps_SequenceId",
                table: "SequenceSteps",
                column: "SequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_SequenceSteps_TemplateId",
                table: "SequenceSteps",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadSequences");

            migrationBuilder.DropTable(
                name: "SequenceSteps");

            migrationBuilder.DropTable(
                name: "EmailSequences");

            migrationBuilder.DropIndex(
                name: "IX_SentEmails_MessageId",
                table: "SentEmails");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "SentEmails");

            migrationBuilder.DropColumn(
                name: "SequenceStepId",
                table: "SentEmails");

            migrationBuilder.DropColumn(
                name: "EmailCheckedAt",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "EmailStatus",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "EmailStatusReason",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "IcpMatch",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NaceCode",
                table: "Companies");
        }
    }
}
