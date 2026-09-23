using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesforceAutoSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SalesforceAttemptAt",
                table: "Leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SalesforceDirty",
                table: "Leads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SalesforceAttemptAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SalesforceDirty",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Mevcut kayitlar yeni alanlarla (derin analiz, e-posta, telefon) bir kez guncellensin.
            migrationBuilder.Sql("""UPDATE "Leads" SET "SalesforceDirty" = TRUE;""");
            migrationBuilder.Sql("""
                UPDATE "Companies" c SET "SalesforceDirty" = TRUE
                WHERE c."SalesforceId" IS NOT NULL
                   OR EXISTS (SELECT 1 FROM "Leads" l WHERE l."CompanyId" = c."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SalesforceAttemptAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SalesforceDirty",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SalesforceAttemptAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SalesforceDirty",
                table: "Companies");
        }
    }
}
