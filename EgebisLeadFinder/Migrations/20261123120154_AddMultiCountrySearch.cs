using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiCountrySearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegionKeys",
                table: "SearchRuns",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegionKeys",
                table: "SearchProfiles",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegionKeys",
                table: "SearchRuns");

            migrationBuilder.DropColumn(
                name: "RegionKeys",
                table: "SearchProfiles");
        }
    }
}
