using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RatedAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RatingJson",
                table: "Companies",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RatingSignal",
                table: "Companies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RatedAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "RatingJson",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "RatingSignal",
                table: "Companies");
        }
    }
}
