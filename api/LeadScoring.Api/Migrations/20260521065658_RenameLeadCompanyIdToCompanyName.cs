using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations;

/// <inheritdoc />
public partial class RenameLeadCompanyIdToCompanyName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Tenant schema: "__EFMigrationsHistory" runs under search_path set by TenantSchemaConnectionInterceptor.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema() AND table_name = 'Leads' AND column_name = 'CompanyId'
              ) THEN
                ALTER TABLE "Leads" RENAME COLUMN "CompanyId" TO "CompanyName";
              ELSIF NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema() AND table_name = 'Leads' AND column_name = 'CompanyName'
              ) THEN
                ALTER TABLE "Leads" ADD COLUMN "CompanyName" text NULL;
              END IF;
            END $$;
            """);

        // Shared dashboard pool under public."Leads" (PublicCompanyDbContext). Runs once per tenant migration apply but is idempotent.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'Leads' AND column_name = 'CompanyId'
              ) THEN
                ALTER TABLE public."Leads" RENAME COLUMN "CompanyId" TO "CompanyName";
              ELSIF NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'Leads' AND column_name = 'CompanyName'
              ) THEN
                ALTER TABLE public."Leads" ADD COLUMN "CompanyName" text NULL;
              END IF;
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema() AND table_name = 'Leads' AND column_name = 'CompanyName'
              )
                 AND NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema() AND table_name = 'Leads' AND column_name = 'CompanyId'
              ) THEN
                ALTER TABLE "Leads" RENAME COLUMN "CompanyName" TO "CompanyId";
              END IF;
            END $$;
            """);

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'Leads' AND column_name = 'CompanyName'
              )
                 AND NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'Leads' AND column_name = 'CompanyId'
              ) THEN
                ALTER TABLE public."Leads" RENAME COLUMN "CompanyName" TO "CompanyId";
              END IF;
            END $$;
            """);
    }
}
