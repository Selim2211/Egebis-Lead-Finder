using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class UniqueCompanyDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_Domain",
                table: "Companies");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Domain",
                table: "Companies",
                column: "Domain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_Domain",
                table: "Companies");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Domain",
                table: "Companies",
                column: "Domain");
        }
    }
}
