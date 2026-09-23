using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesTrackingAndContactProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContactedAt",
                table: "Leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentStartDate",
                table: "Contacts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Headline",
                table: "Contacts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Contacts",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContactedAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailSentAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProjectStartedAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactedAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "EmploymentStartDate",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "Headline",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "ContactedAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "EmailSentAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ProjectStartedAt",
                table: "Companies");
        }
    }
}
