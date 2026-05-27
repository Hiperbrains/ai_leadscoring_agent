using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchLogCompanyName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BatchLogs_ProductId",
                schema: "public",
                table: "BatchLogs");

            migrationBuilder.DropIndex(
                name: "IX_BatchLogs_RunDate_BatchType_ProductId",
                schema: "public",
                table: "BatchLogs");

            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                schema: "public",
                table: "BatchLogs",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "BatchLogs" bl
                SET "CompanyName" = cpc."CompanyName"
                FROM "CompanyProductConfigs" cpc
                WHERE bl."ProductId" = cpc."ProductId"
                  AND bl."CompanyName" IS NULL
                  AND (SELECT COUNT(*) FROM "CompanyProductConfigs" x WHERE x."ProductId" = bl."ProductId") = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BatchLogs_CompanyName_ProductId",
                schema: "public",
                table: "BatchLogs",
                columns: new[] { "CompanyName", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_BatchLogs_RunDate_BatchType_CompanyName_ProductId",
                schema: "public",
                table: "BatchLogs",
                columns: new[] { "RunDate", "BatchType", "CompanyName", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BatchLogs_CompanyName_ProductId",
                schema: "public",
                table: "BatchLogs");

            migrationBuilder.DropIndex(
                name: "IX_BatchLogs_RunDate_BatchType_CompanyName_ProductId",
                schema: "public",
                table: "BatchLogs");

            migrationBuilder.DropColumn(
                name: "CompanyName",
                schema: "public",
                table: "BatchLogs");

            migrationBuilder.CreateIndex(
                name: "IX_BatchLogs_ProductId",
                schema: "public",
                table: "BatchLogs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchLogs_RunDate_BatchType_ProductId",
                schema: "public",
                table: "BatchLogs",
                columns: new[] { "RunDate", "BatchType", "ProductId" },
                unique: true);
        }
    }
}
