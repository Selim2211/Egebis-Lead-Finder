using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EgebisLeadFinder.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Industry = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    AiAnalysis = table.Column<string>(type: "jsonb", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    ProcessingError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TitleScore = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contacts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SelectedTemplateId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leads_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Leads_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Leads_EmailTemplates_SelectedTemplateId",
                        column: x => x.SelectedTemplateId,
                        principalTable: "EmailTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "EmailTemplates",
                columns: new[] { "Id", "Active", "Body", "Key", "Name", "Subject" },
                values: new object[,]
                {
                    { 1, true, "Sayın {CONTACT_NAME},\n\n{COMPANY_NAME} firmasının {INDUSTRY} alanındaki faaliyetlerini inceledik.\n\nEgebis olarak üretim şirketlerine yönelik SAP danışmanlığı,\nentegrasyon ve özel yazılım çözümleri geliştiriyoruz.\n\nFirmanızın mevcut yapısı ile ilgili kısa bir görüşme yaparak\nkarşılıklı olarak değerlendirebileceğimiz alanlar olup olmadığını\nkonuşmak isteriz.\n\nUygun olduğunuz bir zamanda 20 dakikalık bir görüşme\ngerçekleştirmekten memnuniyet duyarız.\n\nSaygılarımızla,\nEgebis Bilişim", "SAP", "SAP Danışmanlığı", "Üretim süreçleri ve SAP danışmanlığı hakkında" },
                    { 2, true, "Sayın {CONTACT_NAME},\n\n{COMPANY_NAME} firmasının {INDUSTRY} alanındaki üretim faaliyetlerini inceledik.\n\nEgebis olarak SAP ile mevcut sistemleriniz (üretim hattı, depo,\ne-ticaret, muhasebe) arasındaki entegrasyonları kuruyor,\nveri akışını uçtan uca otomatikleştiriyoruz.\n\nMevcut entegrasyon ihtiyaçlarınızı değerlendirmek üzere\n20 dakikalık kısa bir görüşme yapmak isteriz.\n\nSaygılarımızla,\nEgebis Bilişim", "SAP_ENTEGRASYON", "SAP Entegrasyonu", "SAP entegrasyonu ve sistem bütünleştirme hakkında" },
                    { 3, true, "Sayın {CONTACT_NAME},\n\n{COMPANY_NAME} firmasının {INDUSTRY} alanındaki çalışmalarını inceledik.\n\nEgebis olarak üretim yapan firmalara özel; iş emri takibi,\nkalite kontrol ve raporlama yazılımları geliştiriyoruz.\n\nSüreçlerinizde yazılımla iyileştirilebilecek alanları\nbirlikte değerlendirmek isteriz.\n\nSaygılarımızla,\nEgebis Bilişim", "URETIM_YAZILIMI", "Üretim Yazılımı", "Üretim süreçlerinize özel yazılım çözümleri" },
                    { 4, true, "Sayın {CONTACT_NAME},\n\n{COMPANY_NAME} firmasının {INDUSTRY} alanındaki üretim kapasitesini inceledik.\n\nEgebis olarak sahadan gerçek zamanlı veri toplayan MES /\nüretim takip sistemleri kuruyoruz: makine verimliliği (OEE),\nduruş analizi ve anlık üretim raporlaması.\n\nMevcut üretim takip yapınızı konuşmak üzere kısa bir\ngörüşme yapmaktan memnuniyet duyarız.\n\nSaygılarımızla,\nEgebis Bilişim", "MES", "MES / Üretim Takip", "Üretim takip (MES) sistemleri hakkında" },
                    { 5, true, "Sayın {CONTACT_NAME},\n\n{COMPANY_NAME} firmasının {INDUSTRY} alanındaki faaliyetlerini inceledik.\n\nEgebis olarak kurumsal yazılım, SAP danışmanlığı ve\nsüreç otomasyonu alanlarında çözümler sunuyoruz.\n\nKarşılıklı olarak değerlendirebileceğimiz alanlar olup olmadığını\nkısa bir görüşmede konuşmak isteriz.\n\nSaygılarımızla,\nEgebis Bilişim", "GENEL", "Genel Tanıtım", "Egebis Bilişim - kurumsal yazılım çözümleri" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Domain",
                table: "Companies",
                column: "Domain");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Score",
                table: "Companies",
                column: "Score");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_CompanyId",
                table: "Contacts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_CompanyId",
                table: "Leads",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_ContactId",
                table: "Leads",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_SelectedTemplateId",
                table: "Leads",
                column: "SelectedTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Status",
                table: "Leads",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "EmailTemplates");

            migrationBuilder.DropTable(
                name: "Companies");
        }
    }
}
