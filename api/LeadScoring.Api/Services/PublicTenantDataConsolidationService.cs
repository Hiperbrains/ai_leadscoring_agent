using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeadScoring.Api.Services;

/// <summary>
/// One-time style migration: copy legacy per-tenant schema rows into <c>public</c> and normalize tenant registry keys.
/// </summary>
public class PublicTenantDataConsolidationService(
    IConfiguration configuration,
    MasterDbContext masterDb,
    LeadResolutionService leadResolutionService,
    ILogger<PublicTenantDataConsolidationService> logger)
{
    public async Task ConsolidateAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Hiperbrains")
            ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.");

        await EnsurePublicMigrationsAsync(connectionString, cancellationToken);

        var tenants = await masterDb.Tenants.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var tenant in tenants)
        {
            if (!IsLegacySchemaName(tenant.DatabaseName))
            {
                continue;
            }

            var legacySchema = tenant.DatabaseName.Trim();
            await CopyCompanyProductConfigsAsync(connectionString, legacySchema, tenant.CompanyName, cancellationToken);
            await BackfillPublicLeadsFromLegacySchemaAsync(
                connectionString,
                legacySchema,
                tenant.CompanyName,
                cancellationToken);

            var tenantEntity = await masterDb.Tenants.FirstAsync(t => t.Id == tenant.Id, cancellationToken);
            tenantEntity.DatabaseName = TenantConnectionStringBuilder.ToTenantDatabaseKey(tenant.Id);
            await masterDb.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Normalized tenant {TenantId} ({Company}) from legacy schema {LegacySchema} to shared public data.",
                tenant.Id,
                tenant.CompanyName,
                legacySchema);
        }

        await BackfillOrphanPublicLeadsAsync(connectionString, tenants, cancellationToken);
        await leadResolutionService.DedupePublicLeadsByEmailAsync(cancellationToken);
        await RepointAllLegacyLeadTrackingAsync(connectionString, cancellationToken);

        if (configuration.GetValue("DataMigration:DropLegacyTenantSchemas", false))
        {
            await DropLegacyTenantSchemasAsync(connectionString, cancellationToken);
        }
    }

    /// <summary>
    /// Drops <c>tenant_*</c> schemas only. Never drops <c>public</c>.
    /// Enable with <c>DataMigration:DropLegacyTenantSchemas</c> after backup and verification.
    /// </summary>
    public async Task DropLegacyTenantSchemasAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var listCmd = new NpgsqlCommand(
            """
            SELECT nspname
            FROM pg_namespace
            WHERE nspname LIKE 'tenant\_%' ESCAPE '\'
            ORDER BY nspname
            """,
            conn);

        var schemas = new List<string>();
        await using (var reader = await listCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                schemas.Add(reader.GetString(0));
            }
        }

        foreach (var schema in schemas)
        {
            await using var dropCmd = new NpgsqlCommand(
                $"""DROP SCHEMA IF EXISTS "{schema.Replace("\"", "\"\"", StringComparison.Ordinal)}" CASCADE""",
                conn);
            await dropCmd.ExecuteNonQueryAsync(cancellationToken);
            logger.LogWarning("Dropped legacy tenant schema {Schema}.", schema);
        }

        if (schemas.Count > 0)
        {
            logger.LogInformation(
                "Dropped {Count} legacy tenant_* schema(s). public schema was not modified.",
                schemas.Count);
        }
    }

    private async Task RepointAllLegacyLeadTrackingAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var emailsCmd = new NpgsqlCommand(
            """
            SELECT DISTINCT LOWER(TRIM("Email"))
            FROM public."Leads"
            WHERE "Email" IS NOT NULL AND TRIM("Email") <> ''
            """,
            conn);

        var emails = new List<string>();
        await using (var reader = await emailsCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                emails.Add(reader.GetString(0));
            }
        }

        foreach (var email in emails)
        {
            await leadResolutionService.FindByEmailAsync(email, cancellationToken);
        }

        logger.LogInformation(
            "Finished repointing legacy tenant lead ids to public.Leads for {Count} email(s).",
            emails.Count);
    }

    private static bool IsLegacySchemaName(string databaseName) =>
        databaseName.StartsWith("tenant_", StringComparison.OrdinalIgnoreCase);

    private static async Task EnsurePublicMigrationsAsync(string connectionString, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<LeadScoringDbContext>()
            .UseNpgsql(connectionString, npg =>
                npg.MigrationsHistoryTable("__EFMigrationsHistory", TenantConnectionStringBuilder.SharedSchemaName))
            .AddInterceptors(new PublicSchemaConnectionInterceptor())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using var db = new LeadScoringDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
    }

    private async Task CopyCompanyProductConfigsAsync(
        string connectionString,
        string legacySchema,
        string companyName,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var existsCmd = new NpgsqlCommand(
            """
            SELECT EXISTS (
              SELECT 1 FROM pg_tables
              WHERE schemaname = @schema AND tablename = 'CompanyProductConfigs'
            )
            """,
            conn);
        existsCmd.Parameters.AddWithValue("schema", legacySchema);
        var schemaTableExists = Convert.ToBoolean(
            await existsCmd.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (!schemaTableExists)
        {
            return;
        }

        var escapedSchema = legacySchema.Replace("\"", "\"\"", StringComparison.Ordinal);
        await using var migrateCmd = new NpgsqlCommand(
            $"""
            INSERT INTO public."CompanyProductConfigs"
                ("Id", "CompanyName", "ProductName", "ProductId", "ProductEventConfigJson", "StageThresholdsJson", "CreatedAtUtc", "ProductUrl")
            SELECT
                t."Id",
                COALESCE(NULLIF(TRIM(t."CompanyName"), ''), @companyName),
                t."ProductName",
                t."ProductId",
                t."ProductEventConfigJson",
                t."StageThresholdsJson",
                t."CreatedAtUtc",
                t."ProductUrl"
            FROM "{escapedSchema}"."CompanyProductConfigs" t
            WHERE NOT EXISTS (
                SELECT 1 FROM public."CompanyProductConfigs" p
                WHERE p."CompanyName" = COALESCE(NULLIF(TRIM(t."CompanyName"), ''), @companyName)
                  AND p."ProductName" = t."ProductName"
                  AND p."ProductId" = t."ProductId"
            )
            ON CONFLICT ("Id") DO NOTHING
            """,
            conn);
        migrateCmd.Parameters.AddWithValue("companyName", companyName.Trim());

        try
        {
            var inserted = await migrateCmd.ExecuteNonQueryAsync(cancellationToken);
            if (inserted > 0)
            {
                logger.LogInformation(
                    "Copied {Count} company product config(s) from {LegacySchema} into public for {Company}.",
                    inserted,
                    legacySchema,
                    companyName);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            logger.LogDebug(ex, "Skipping config copy from {LegacySchema}; table missing.", legacySchema);
        }
    }

    private async Task BackfillPublicLeadsFromLegacySchemaAsync(
        string connectionString,
        string legacySchema,
        string companyName,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var existsCmd = new NpgsqlCommand(
            """
            SELECT EXISTS (
              SELECT 1 FROM pg_tables
              WHERE schemaname = @schema AND tablename = 'Leads'
            )
            """,
            conn);
        existsCmd.Parameters.AddWithValue("schema", legacySchema);
        var schemaTableExists = Convert.ToBoolean(
            await existsCmd.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (!schemaTableExists)
        {
            return;
        }

        var escapedSchema = legacySchema.Replace("\"", "\"\"", StringComparison.Ordinal);
        await using var updateCmd = new NpgsqlCommand(
            $"""
            UPDATE public."Leads" AS p
            SET
                "CompanyName" = COALESCE(NULLIF(TRIM(t."CompanyName"), ''), @companyName),
                "ProductId" = COALESCE(p."ProductId", t."ProductId", {TenantLeadScope.ScopedProductId})
            FROM "{escapedSchema}"."Leads" AS t
            WHERE LOWER(TRIM(p."Email")) = LOWER(TRIM(t."Email"))
              AND (p."CompanyName" IS NULL OR TRIM(p."CompanyName") = '')
            """,
            conn);
        updateCmd.Parameters.AddWithValue("companyName", companyName.Trim());

        try
        {
            var updated = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
            if (updated > 0)
            {
                logger.LogInformation(
                    "Backfilled CompanyName on {Count} public lead(s) from schema {LegacySchema} ({Company}).",
                    updated,
                    legacySchema,
                    companyName);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            logger.LogDebug(ex, "Skipping lead backfill from {LegacySchema}; table missing.", legacySchema);
        }
    }

    private async Task BackfillOrphanPublicLeadsAsync(
        string connectionString,
        IReadOnlyList<Tenant> tenants,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        try
        {
            await using var fromEventsCmd = new NpgsqlCommand(
                """
                UPDATE public."Leads" AS l
                SET "CompanyName" = inferred.company_name
                FROM (
                    SELECT DISTINCT ON (e."LeadId")
                        e."LeadId",
                        NULLIF(TRIM(e."MetadataJson"::json ->> 'companyName'), '') AS company_name
                    FROM public."Events" AS e
                    WHERE e."LeadId" IS NOT NULL
                      AND e."MetadataJson" IS NOT NULL
                      AND e."MetadataJson"::json ? 'companyName'
                    ORDER BY e."LeadId", e."TimestampUtc" DESC
                ) AS inferred
                WHERE l."Id" = inferred."LeadId"
                  AND inferred.company_name IS NOT NULL
                  AND (l."CompanyName" IS NULL OR TRIM(l."CompanyName") = '')
                """,
                conn);

            var fromEvents = await fromEventsCmd.ExecuteNonQueryAsync(cancellationToken);
            if (fromEvents > 0)
            {
                logger.LogInformation(
                    "Backfilled CompanyName on {Count} public lead(s) from event metadata.",
                    fromEvents);
            }
        }
        catch (PostgresException ex)
        {
            logger.LogDebug(ex, "Skipping event-metadata lead backfill (invalid JSON or missing column).");
        }

        if (tenants.Count == 1)
        {
            var onlyCompany = tenants[0].CompanyName.Trim();
            await using var singleTenantCmd = new NpgsqlCommand(
                """
                UPDATE public."Leads"
                SET
                    "CompanyName" = @companyName,
                    "ProductId" = COALESCE("ProductId", @defaultProductId)
                WHERE "CompanyName" IS NULL OR TRIM("CompanyName") = ''
                """,
                conn);
            singleTenantCmd.Parameters.AddWithValue("companyName", onlyCompany);
            singleTenantCmd.Parameters.AddWithValue("defaultProductId", TenantLeadScope.ScopedProductId);
            var singleTenant = await singleTenantCmd.ExecuteNonQueryAsync(cancellationToken);
            if (singleTenant > 0)
            {
                logger.LogInformation(
                    "Assigned remaining {Count} unscoped public lead(s) to sole tenant company {Company}.",
                    singleTenant,
                    onlyCompany);
            }
        }

        await using var productIdCmd = new NpgsqlCommand(
            $"""
            UPDATE public."Leads"
            SET "ProductId" = {TenantLeadScope.ScopedProductId}
            WHERE "ProductId" IS NULL
              AND "CompanyName" IS NOT NULL
              AND TRIM("CompanyName") <> ''
            """,
            conn);
        var productIds = await productIdCmd.ExecuteNonQueryAsync(cancellationToken);
        if (productIds > 0)
        {
            logger.LogInformation(
                "Set default ProductId on {Count} public lead(s) for dashboard visibility.",
                productIds);
        }
    }
}
