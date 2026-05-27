using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchLogProductId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-existing model-snapshot drift: every other migration declared tables without an
            // explicit "public" schema, so the model snapshot now flags them as schema-less. The
            // tables already live in `public` (HasDefaultSchema), so we skip those rename ops here
            // and keep the migration focused on the BatchLogs change.

            migrationBuilder.DropIndex(
                name: "IX_BatchLogs_RunDate_BatchType",
                table: "BatchLogs");

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "BatchLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchLogs_ProductId",
                table: "BatchLogs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchLogs_RunDate_BatchType_ProductId",
                table: "BatchLogs",
                columns: new[] { "RunDate", "BatchType", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BatchLogs_ProductId",
                table: "BatchLogs");

            migrationBuilder.DropIndex(
                name: "IX_BatchLogs_RunDate_BatchType_ProductId",
                table: "BatchLogs");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "BatchLogs");

            migrationBuilder.CreateIndex(
                name: "IX_BatchLogs_RunDate_BatchType",
                table: "BatchLogs",
                columns: new[] { "RunDate", "BatchType" },
                unique: true);
        }
    }
}
