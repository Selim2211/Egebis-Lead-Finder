using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class NewsLiveAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NewsBoost",
                table: "Companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NewsBoostAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NewsScanCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RefId = table.Column<int>(type: "integer", nullable: false),
                    LastScannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsScanCursors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsScanCursors_Kind_RefId",
                table: "NewsScanCursors",
                columns: new[] { "Kind", "RefId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewsScanCursors");

            migrationBuilder.DropColumn(
                name: "NewsBoost",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NewsBoostAt",
                table: "Companies");
        }
    }
}
