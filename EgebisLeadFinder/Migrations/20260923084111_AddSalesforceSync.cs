using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesforceSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SalesforceId",
                table: "Leads",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesforceSyncError",
                table: "Leads",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SalesforceSyncedAt",
                table: "Leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesforceId",
                table: "Companies",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesforceSyncError",
                table: "Companies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SalesforceSyncedAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SalesforceId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SalesforceSyncError",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SalesforceSyncedAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SalesforceId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SalesforceSyncError",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SalesforceSyncedAt",
                table: "Companies");
        }
    }
}
