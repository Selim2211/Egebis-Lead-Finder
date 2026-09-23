using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class EmailEditorImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Placement",
                table: "EmailImages");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "EmailImages");

            migrationBuilder.RenameColumn(
                name: "IncludeByDefault",
                table: "EmailImages",
                newName: "InLibrary");

            migrationBuilder.AddColumn<string>(
                name: "BodyHtml",
                table: "SentEmails",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BodyHtml",
                table: "SentEmails");

            migrationBuilder.RenameColumn(
                name: "InLibrary",
                table: "EmailImages",
                newName: "IncludeByDefault");

            migrationBuilder.AddColumn<int>(
                name: "Placement",
                table: "EmailImages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "EmailImages",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
