using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchLogRunSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AdminMirrorSent",
                schema: "public",
                table: "BatchLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RunSource",
                schema: "public",
                table: "BatchLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminMirrorSent",
                schema: "public",
                table: "BatchLogs");

            migrationBuilder.DropColumn(
                name: "RunSource",
                schema: "public",
                table: "BatchLogs");
        }
    }
}
