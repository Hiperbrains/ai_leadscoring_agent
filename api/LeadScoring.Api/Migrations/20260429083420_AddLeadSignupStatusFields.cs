using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadSignupStatusFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPlanSelected",
                table: "Leads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LoginDataExists",
                table: "Leads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanRenewalDate",
                table: "Leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProfileCompletion",
                table: "Leads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SelectedPlan",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SignupCompleted",
                table: "Leads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UserExists",
                table: "Leads",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPlanSelected",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LoginDataExists",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "PlanRenewalDate",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ProfileCompletion",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SelectedPlan",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SignupCompleted",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "UserExists",
                table: "Leads");
        }
    }
}
