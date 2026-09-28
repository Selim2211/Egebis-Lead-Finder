using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvaluationNote",
                table: "Companies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EvaluationStatus",
                table: "Companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Eski kayitlar: analizi olmayan firma incelenemedi, analizi olup 0 puan alan elendi.
            migrationBuilder.Sql(@"
UPDATE ""Companies"" SET ""EvaluationStatus"" = 2,
    ""EvaluationNote"" = LEFT(COALESCE('Site okunamadı veya analiz yapılamadı: ' || ""ProcessingError"", 'Yapay zekâ analizi yapılamadı.'), 500)
WHERE ""AiAnalysis"" IS NULL;

UPDATE ""Companies"" SET ""EvaluationStatus"" = 1,
    ""EvaluationNote"" = LEFT(NULLIF(""AiAnalysis""::jsonb ->> 'reason', ''), 500)
WHERE ""AiAnalysis"" IS NOT NULL AND ""Score"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvaluationNote",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "EvaluationStatus",
                table: "Companies");
        }
    }
}
