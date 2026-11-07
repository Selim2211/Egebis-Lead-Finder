using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchProfileNaceProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NaceProducts",
                table: "SearchProfiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NaceProducts",
                table: "SearchProfiles");
        }
    }
}
