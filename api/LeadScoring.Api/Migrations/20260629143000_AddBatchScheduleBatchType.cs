using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchScheduleBatchType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BatchScheduleSettings_CompanyName_ProductId",
                schema: "public",
                table: "BatchScheduleSettings");

            migrationBuilder.AddColumn<int>(
                name: "BatchType",
                schema: "public",
                table: "BatchScheduleSettings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_BatchScheduleSettings_CompanyName_ProductId_BatchType",
                schema: "public",
                table: "BatchScheduleSettings",
                columns: new[] { "CompanyName", "ProductId", "BatchType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BatchScheduleSettings_CompanyName_ProductId_BatchType",
                schema: "public",
                table: "BatchScheduleSettings");

            migrationBuilder.DropColumn(
                name: "BatchType",
                schema: "public",
                table: "BatchScheduleSettings");

            migrationBuilder.CreateIndex(
                name: "IX_BatchScheduleSettings_CompanyName_ProductId",
                schema: "public",
                table: "BatchScheduleSettings",
                columns: new[] { "CompanyName", "ProductId" },
                unique: true);
        }
    }
}
