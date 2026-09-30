using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class PrivateSearchTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SearchProfiles_Name",
                table: "SearchProfiles");

            migrationBuilder.DropIndex(
                name: "IX_CompanySearchProfiles_CompanyId_SearchProfileId",
                table: "CompanySearchProfiles");

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "SearchProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "SearchProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "CompanySearchProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchProfiles_OwnerUserId_Name",
                table: "SearchProfiles",
                columns: new[] { "OwnerUserId", "Name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySearchProfiles_CompanyId_SearchProfileId_UserId",
                table: "CompanySearchProfiles",
                columns: new[] { "CompanyId", "SearchProfileId", "UserId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySearchProfiles_UserId",
                table: "CompanySearchProfiles",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySearchProfiles_Users_UserId",
                table: "CompanySearchProfiles",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SearchProfiles_Users_OwnerUserId",
                table: "SearchProfiles",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Kullanicilardan once kaydedilen sablonlar herkesin ortak kullanimindaydi: acik kalsin.
            migrationBuilder.Sql("UPDATE \"SearchProfiles\" SET \"IsPublic\" = TRUE WHERE \"OwnerUserId\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanySearchProfiles_Users_UserId",
                table: "CompanySearchProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_SearchProfiles_Users_OwnerUserId",
                table: "SearchProfiles");

            migrationBuilder.DropIndex(
                name: "IX_SearchProfiles_OwnerUserId_Name",
                table: "SearchProfiles");

            migrationBuilder.DropIndex(
                name: "IX_CompanySearchProfiles_CompanyId_SearchProfileId_UserId",
                table: "CompanySearchProfiles");

            migrationBuilder.DropIndex(
                name: "IX_CompanySearchProfiles_UserId",
                table: "CompanySearchProfiles");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "SearchProfiles");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "SearchProfiles");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "CompanySearchProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_SearchProfiles_Name",
                table: "SearchProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySearchProfiles_CompanyId_SearchProfileId",
                table: "CompanySearchProfiles",
                columns: new[] { "CompanyId", "SearchProfileId" },
                unique: true);
        }
    }
}
