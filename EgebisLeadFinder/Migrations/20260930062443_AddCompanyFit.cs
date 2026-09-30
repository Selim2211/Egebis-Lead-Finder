using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyFit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FitReason",
                table: "Companies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FitScore",
                table: "Companies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FitSegment",
                table: "Companies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Eski analizlerdeki yapay zeka gerekcesi "neden" alani olarak listede gorunsun.
            migrationBuilder.Sql("""
                UPDATE "Companies"
                SET "FitReason" = left("AiAnalysis"->>'reason', 500)
                WHERE "AiAnalysis" IS NOT NULL AND coalesce("AiAnalysis"->>'reason', '') <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FitReason",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "FitScore",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "FitSegment",
                table: "Companies");
        }
    }
}
