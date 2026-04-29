using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadVisitorMappingAndWebsiteDemoFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VisitorId",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeadVisitorMaps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitorId = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    CompanyName = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadVisitorMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadVisitorMaps_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_VisitorId",
                table: "Leads",
                column: "VisitorId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadVisitorMaps_LeadId_VisitorId",
                table: "LeadVisitorMaps",
                columns: new[] { "LeadId", "VisitorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadVisitorMaps_VisitorId",
                table: "LeadVisitorMaps",
                column: "VisitorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadVisitorMaps");

            migrationBuilder.DropIndex(
                name: "IX_Leads_VisitorId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "VisitorId",
                table: "Leads");
        }
    }
}
