using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class ReclassifyNewsRegion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bölge artık haberin KONUSUNA göre: radar haberleri yeniden sınıflanacak (boş = bekliyor; tarama turu karar verir).
            migrationBuilder.Sql(@"UPDATE ""NewsItems"" SET ""Region"" = '' WHERE ""CompanyId"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
