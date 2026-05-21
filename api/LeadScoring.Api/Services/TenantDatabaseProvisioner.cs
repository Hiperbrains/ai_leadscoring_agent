using System.Collections.Concurrent;
using LeadScoring.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeadScoring.Api.Services;

public class TenantDatabaseProvisioner(IConfiguration configuration, ILogger<TenantDatabaseProvisioner> logger)
    : ITenantDatabaseProvisioner
{
    private static readonly ConcurrentDictionary<string, byte> ReadySchemas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SchemaLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task ProvisionAsync(string schemaName, CancellationToken cancellationToken = default)
    {
        var gate = SchemaLocks.GetOrAdd(schemaName, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await ProvisionCoreAsync(schemaName, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ProvisionCoreAsync(string schemaName, CancellationToken cancellationToken)
    {
        var masterConnection = configuration.GetConnectionString("Hiperbrains")
            ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.");

        try
        {
            await RecreateSchemaAsync(masterConnection, schemaName, cancellationToken);
            await ApplyTenantMigrationsAsync(masterConnection, schemaName, cancellationToken);
            ReadySchemas[schemaName] = 1;
            logger.LogInformation("Tenant schema {SchemaName} provisioned for new company.", schemaName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to provision tenant schema {SchemaName}.", schemaName);
            await TryDropSchemaAsync(masterConnection, schemaName, cancellationToken);
            ReadySchemas.TryRemove(schemaName, out _);
            throw new AuthValidationException(
                "Could not set up your company workspace. Please try again in a moment or use a different company name.");
        }
    }

    public async Task EnsureReadyAsync(string schemaName, CancellationToken cancellationToken = default)
    {
        var masterConnection = configuration.GetConnectionString("Hiperbrains")
            ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.");

        if (ReadySchemas.ContainsKey(schemaName))
        {
            await MigratePublicCompanyProductConfigsAsync(masterConnection, schemaName, cancellationToken);
            return;
        }

        var gate = SchemaLocks.GetOrAdd(schemaName, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (ReadySchemas.ContainsKey(schemaName))
            {
                await MigratePublicCompanyProductConfigsAsync(masterConnection, schemaName, cancellationToken);
                return;
            }

            await EnsureReadyCoreAsync(schemaName, cancellationToken);

            // Tenant schema must be migrated before this INSERT — it targets columns such as StageThresholdsJson.
            await MigratePublicCompanyProductConfigsAsync(masterConnection, schemaName, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureReadyCoreAsync(string schemaName, CancellationToken cancellationToken)
    {
        var masterConnection = configuration.GetConnectionString("Hiperbrains")
            ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.");

        await EnsureSchemaExistsAsync(masterConnection, schemaName, cancellationToken);

        // Existing tenants satisfied SchemaHasCoreTablesAsync but skipped MigrateAsync forever, leaving
        // schema behind the model (e.g. CompanyId vs CompanyName on Leads). Always apply pending migrations.
        await ApplyTenantMigrationsAsync(masterConnection, schemaName, cancellationToken);

        if (!await SchemaHasCoreTablesAsync(masterConnection, schemaName, cancellationToken))
        {
            logger.LogWarning("Tenant schema {SchemaName} still incomplete; rebuilding schema.", schemaName);
            await RecreateSchemaAsync(masterConnection, schemaName, cancellationToken);
            await ApplyTenantMigrationsAsync(masterConnection, schemaName, cancellationToken);
        }

        if (!await SchemaHasCoreTablesAsync(masterConnection, schemaName, cancellationToken))
        {
            throw new InvalidOperationException($"Could not initialize tenant schema {schemaName}.");
        }

        ReadySchemas[schemaName] = 1;
        logger.LogInformation("Tenant schema {SchemaName} is ready.", schemaName);
    }

    private async Task ApplyTenantMigrationsAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<LeadScoringDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
            .AddInterceptors(new TenantSchemaConnectionInterceptor(schemaName))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using var db = new LeadScoringDbContext(options, schemaName);
        await db.Database.MigrateAsync(cancellationToken);
    }

    private static async Task EnsureSchemaExistsAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\"", conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> SchemaHasCoreTablesAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_tables
            WHERE schemaname = @schema
              AND tablename IN ('Leads', 'Events', 'CompanyProductConfigs', 'EmailTemplates')
            """,
            conn);
        cmd.Parameters.AddWithValue("schema", schemaName);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return count >= 4;
    }

    private static async Task RecreateSchemaAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using (var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE", conn))
        {
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schemaName}\"", conn))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Copies legacy rows from public."CompanyProductConfigs" into the tenant schema when the
    /// config's company name maps to this schema (pre–multi-tenant data lived in public only).
    /// </summary>
    private async Task MigratePublicCompanyProductConfigsAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var existsCmd = new NpgsqlCommand(
            """
            SELECT EXISTS (
              SELECT 1 FROM pg_tables
              WHERE schemaname = 'public' AND tablename = 'CompanyProductConfigs'
            )
            """,
            conn);
        var publicTableExists = Convert.ToBoolean(
            await existsCmd.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (!publicTableExists)
        {
            return;
        }

        var escapedSchema = schemaName.Replace("\"", "\"\"", StringComparison.Ordinal);
        await using var migrateCmd = new NpgsqlCommand(
            $"""
            INSERT INTO "{escapedSchema}"."CompanyProductConfigs"
                ("Id", "CompanyName", "ProductName", "ProductId", "ProductEventConfigJson", "StageThresholdsJson", "CreatedAtUtc")
            SELECT
                p."Id",
                p."CompanyName",
                p."ProductName",
                p."ProductId",
                p."ProductEventConfigJson",
                NULL::text AS "StageThresholdsJson",
                COALESCE(p."CreatedAtUtc", NOW() AT TIME ZONE 'utc')
            FROM public."CompanyProductConfigs" p
            WHERE 'tenant_' || LEFT(REGEXP_REPLACE(LOWER(TRIM(p."CompanyName")), '[^a-z0-9]+', '', 'g'), 40) = @schema
              AND NOT EXISTS (
                SELECT 1 FROM "{escapedSchema}"."CompanyProductConfigs" t
                WHERE t."CompanyName" = p."CompanyName"
                  AND t."ProductName" = p."ProductName"
                  AND t."ProductId" = p."ProductId"
              )
            """,
            conn);
        migrateCmd.Parameters.AddWithValue("schema", schemaName);

        try
        {
            var inserted = await migrateCmd.ExecuteNonQueryAsync(cancellationToken);
            if (inserted > 0)
            {
                logger.LogInformation(
                    "Migrated {Count} company product config(s) from public into schema {SchemaName}.",
                    inserted,
                    schemaName);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            logger.LogDebug(ex, "Skipping public config migration for {SchemaName}; tenant table not ready.", schemaName);
        }
    }

    private async Task TryDropSchemaAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE", conn);
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception dropEx)
        {
            logger.LogWarning(dropEx, "Could not drop failed tenant schema {SchemaName}.", schemaName);
        }
    }
}
