using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchProfileDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultSearchProfileId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxCompanies",
                table: "SearchProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_DefaultSearchProfileId",
                table: "Users",
                column: "DefaultSearchProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_SearchProfiles_DefaultSearchProfileId",
                table: "Users",
                column: "DefaultSearchProfileId",
                principalTable: "SearchProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_SearchProfiles_DefaultSearchProfileId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_DefaultSearchProfileId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DefaultSearchProfileId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MaxCompanies",
                table: "SearchProfiles");
        }
    }
}
