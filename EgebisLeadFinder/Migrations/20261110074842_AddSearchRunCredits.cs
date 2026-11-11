using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchRunCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiCalls",
                table: "SearchRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SerperCredits",
                table: "SearchRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiCalls",
                table: "SearchRuns");

            migrationBuilder.DropColumn(
                name: "SerperCredits",
                table: "SearchRuns");
        }
    }
}
