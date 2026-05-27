using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailTemplateCompanyName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailTemplates_Stage_ProductId_IsFollowUp",
                schema: "public",
                table: "EmailTemplates");

            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                schema: "public",
                table: "EmailTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "EmailTemplates" et
                SET "CompanyName" = cpc."CompanyName"
                FROM "CompanyProductConfigs" cpc
                WHERE et."ProductId" = cpc."ProductId"
                  AND et."CompanyName" IS NULL
                  AND et."ProductId" IS NOT NULL
                  AND (SELECT COUNT(*) FROM "CompanyProductConfigs" x WHERE x."ProductId" = et."ProductId") = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_CompanyName_ProductId",
                schema: "public",
                table: "EmailTemplates",
                columns: new[] { "CompanyName", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_CompanyName_Stage_ProductId_IsFollowUp",
                schema: "public",
                table: "EmailTemplates",
                columns: new[] { "CompanyName", "Stage", "ProductId", "IsFollowUp" },
                unique: true,
                filter: "\"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailTemplates_CompanyName_ProductId",
                schema: "public",
                table: "EmailTemplates");

            migrationBuilder.DropIndex(
                name: "IX_EmailTemplates_CompanyName_Stage_ProductId_IsFollowUp",
                schema: "public",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "CompanyName",
                schema: "public",
                table: "EmailTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Stage_ProductId_IsFollowUp",
                schema: "public",
                table: "EmailTemplates",
                columns: new[] { "Stage", "ProductId", "IsFollowUp" },
                unique: true,
                filter: "\"IsActive\" = true");
        }
    }
}
