using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeadScoring.Api.Services;

/// <summary>
/// Always resolves leads from <c>public."Leads"</c> (never returns a tenant-schema row id).
/// </summary>
public class LeadResolutionService(
    ICompanyLeadDbAccessor companyLeadAccessor,
    IConfiguration configuration,
    ILogger<LeadResolutionService> logger)
{
    public static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    public Task<Guid?> GetCanonicalPublicLeadIdAsync(string emailRaw, CancellationToken cancellationToken = default) =>
        FindPublicLeadIdByEmailAsync(NormalizeEmail(emailRaw), cancellationToken);

    /// <summary>
    /// When multiple public rows share an email (legacy import + CRM), keep the richest row and repoint tracking.
    /// </summary>
    public async Task DedupePublicLeadsByEmailAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Hiperbrains");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            WITH canonical AS (
                SELECT DISTINCT ON (LOWER(TRIM("Email")))
                    "Id" AS canonical_id,
                    LOWER(TRIM("Email")) AS email_key
                FROM public."Leads"
                WHERE "Email" IS NOT NULL AND TRIM("Email") <> ''
                ORDER BY LOWER(TRIM("Email")), "Score" DESC, "CreatedAtUtc" ASC
            ),
            inferior AS (
                SELECT l."Id" AS inferior_id, c.canonical_id
                FROM public."Leads" AS l
                INNER JOIN canonical AS c ON LOWER(TRIM(l."Email")) = c.email_key
                WHERE l."Id" <> c.canonical_id
            ),
            repoint_events AS (
                UPDATE public."Events" AS e
                SET "LeadId" = i.canonical_id
                FROM inferior AS i
                WHERE e."LeadId" = i.inferior_id
                RETURNING 1
            ),
            repoint_maps AS (
                UPDATE public."LeadVisitorMaps" AS m
                SET "LeadId" = i.canonical_id
                FROM inferior AS i
                WHERE m."LeadId" = i.inferior_id
                  AND NOT EXISTS (
                    SELECT 1 FROM public."LeadVisitorMaps" AS x
                    WHERE x."LeadId" = i.canonical_id AND x."VisitorId" = m."VisitorId"
                  )
                RETURNING 1
            ),
            delete_maps AS (
                DELETE FROM public."LeadVisitorMaps" AS m
                USING inferior AS i
                WHERE m."LeadId" = i.inferior_id
                RETURNING 1
            ),
            delete_leads AS (
                DELETE FROM public."Leads" AS l
                USING inferior AS i
                WHERE l."Id" = i.inferior_id
                RETURNING l."Id"
            )
            SELECT COUNT(*) FROM delete_leads
            """,
            conn);

        var removed = Convert.ToInt32(
            await cmd.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (removed > 0)
        {
            logger.LogInformation(
                "Removed {Count} duplicate inferior public.Leads row(s); kept highest-score row per email.",
                removed);
        }
    }

    public async Task<Lead?> FindByEmailAsync(string emailRaw, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(emailRaw);
        if (string.IsNullOrWhiteSpace(normalizedEmail) || !normalizedEmail.Contains('@'))
        {
            return null;
        }

        await DedupePublicLeadsByEmailAsync(cancellationToken);

        var publicLeadId = await FindPublicLeadIdByEmailAsync(normalizedEmail, cancellationToken);
        if (publicLeadId is null)
        {
            await TryImportLegacyLeadIntoPublicAsync(normalizedEmail, cancellationToken);
            publicLeadId = await FindPublicLeadIdByEmailAsync(normalizedEmail, cancellationToken);
        }

        if (publicLeadId is null)
        {
            return null;
        }

        await RepointPublicReferencesFromLegacyTenantLeadsAsync(
            normalizedEmail,
            publicLeadId.Value,
            cancellationToken);

        var companyDb = companyLeadAccessor.GetDbContext();
        return await companyDb.Leads
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == publicLeadId.Value, cancellationToken);
    }

    /// <summary>
    /// Picks the canonical public row: highest score, then earliest created (richest CRM lead wins).
    /// </summary>
    private async Task<Guid?> FindPublicLeadIdByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Hiperbrains");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT "Id"
            FROM public."Leads"
            WHERE LOWER(TRIM("Email")) = @email
            ORDER BY "Score" DESC, "CreatedAtUtc" ASC
            LIMIT 1
            """,
            conn);
        cmd.Parameters.AddWithValue("email", normalizedEmail);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private async Task RepointPublicReferencesFromLegacyTenantLeadsAsync(
        string normalizedEmail,
        Guid publicLeadId,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Hiperbrains");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var schemasCmd = new NpgsqlCommand(
            """
            SELECT schema_name
            FROM information_schema.schemata
            WHERE schema_name LIKE 'tenant\_%' ESCAPE '\'
            """,
            conn);

        var legacyIds = new List<Guid>();
        await using (var reader = await schemasCmd.ExecuteReaderAsync(cancellationToken))
        {
            var schemas = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                schemas.Add(reader.GetString(0));
            }

            await reader.CloseAsync();

            foreach (var schema in schemas)
            {
                var escapedSchema = schema.Replace("\"", "\"\"", StringComparison.Ordinal);
                await using var idCmd = new NpgsqlCommand(
                    $"""
                    SELECT t."Id"
                    FROM "{escapedSchema}"."Leads" AS t
                    WHERE LOWER(TRIM(t."Email")) = @email
                      AND t."Id" <> @publicLeadId
                    """,
                    conn);
                idCmd.Parameters.AddWithValue("email", normalizedEmail);
                idCmd.Parameters.AddWithValue("publicLeadId", publicLeadId);

                try
                {
                    await using var idReader = await idCmd.ExecuteReaderAsync(cancellationToken);
                    while (await idReader.ReadAsync(cancellationToken))
                    {
                        legacyIds.Add(idReader.GetGuid(0));
                    }
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
                {
                    logger.LogDebug(ex, "Skipping legacy lead id scan in {Schema}.", schema);
                }
            }
        }

        if (legacyIds.Count == 0)
        {
            return;
        }

        foreach (var legacyId in legacyIds.Distinct())
        {
            await using var eventsCmd = new NpgsqlCommand(
                """
                UPDATE public."Events"
                SET "LeadId" = @publicLeadId
                WHERE "LeadId" = @legacyLeadId
                """,
                conn);
            eventsCmd.Parameters.AddWithValue("publicLeadId", publicLeadId);
            eventsCmd.Parameters.AddWithValue("legacyLeadId", legacyId);
            var eventsUpdated = await eventsCmd.ExecuteNonQueryAsync(cancellationToken);

            await using var mapsCmd = new NpgsqlCommand(
                """
                UPDATE public."LeadVisitorMaps" AS m
                SET "LeadId" = @publicLeadId
                WHERE m."LeadId" = @legacyLeadId
                  AND NOT EXISTS (
                    SELECT 1 FROM public."LeadVisitorMaps" AS existing
                    WHERE existing."LeadId" = @publicLeadId
                      AND existing."VisitorId" = m."VisitorId"
                  )
                """,
                conn);
            mapsCmd.Parameters.AddWithValue("publicLeadId", publicLeadId);
            mapsCmd.Parameters.AddWithValue("legacyLeadId", legacyId);
            var mapsUpdated = await mapsCmd.ExecuteNonQueryAsync(cancellationToken);

            await using var deleteDupMapsCmd = new NpgsqlCommand(
                """
                DELETE FROM public."LeadVisitorMaps"
                WHERE "LeadId" = @legacyLeadId
                """,
                conn);
            deleteDupMapsCmd.Parameters.AddWithValue("legacyLeadId", legacyId);
            await deleteDupMapsCmd.ExecuteNonQueryAsync(cancellationToken);

            if (eventsUpdated > 0 || mapsUpdated > 0)
            {
                logger.LogInformation(
                    "Repointed public tracking from legacy lead {LegacyLeadId} to public lead {PublicLeadId} for {Email} ({Events} events, {Maps} maps).",
                    legacyId,
                    publicLeadId,
                    normalizedEmail,
                    eventsUpdated,
                    mapsUpdated);
            }
        }
    }

    private async Task<bool> TryImportLegacyLeadIntoPublicAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Hiperbrains");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        var existingPublic = await FindPublicLeadIdByEmailAsync(normalizedEmail, cancellationToken);
        if (existingPublic is not null)
        {
            return false;
        }

        await using var schemasCmd = new NpgsqlCommand(
            """
            SELECT schema_name
            FROM information_schema.schemata
            WHERE schema_name LIKE 'tenant\_%' ESCAPE '\'
            ORDER BY schema_name
            """,
            conn);

        await using var reader = await schemasCmd.ExecuteReaderAsync(cancellationToken);
        var schemas = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        await reader.CloseAsync();

        foreach (var schema in schemas)
        {
            var escapedSchema = schema.Replace("\"", "\"\"", StringComparison.Ordinal);
            await using var importCmd = new NpgsqlCommand(
                $"""
                INSERT INTO public."Leads" (
                    "Id", "VisitorId", "Email", "FirstName", "LastName", "FirstSource", "LastSource",
                    "ProductId", "CompanyName", "WelcomeEmailSent", "Score", "Stage", "CreatedAtUtc",
                    "LastActivityUtc", "LastEmailSentDateUtc", "NextEmailSendDateUtc", "LastScoredAtUtc",
                    "UserExists", "SignupCompleted", "LoginDataExists", "ProfileCompletion",
                    "IsPlanSelected", "SelectedPlan", "PlanRenewalDate"
                )
                SELECT
                    t."Id", t."VisitorId", LOWER(TRIM(t."Email")), t."FirstName", t."LastName",
                    t."FirstSource", t."LastSource", t."ProductId", t."CompanyName",
                    t."WelcomeEmailSent", t."Score", t."Stage", t."CreatedAtUtc", t."LastActivityUtc",
                    t."LastEmailSentDateUtc", t."NextEmailSendDateUtc", t."LastScoredAtUtc",
                    t."UserExists", t."SignupCompleted", t."LoginDataExists", t."ProfileCompletion",
                    t."IsPlanSelected", t."SelectedPlan", t."PlanRenewalDate"
                FROM "{escapedSchema}"."Leads" AS t
                WHERE LOWER(TRIM(t."Email")) = @email
                  AND NOT EXISTS (
                    SELECT 1 FROM public."Leads" AS p
                    WHERE LOWER(TRIM(p."Email")) = @email
                  )
                ORDER BY t."Score" DESC, t."CreatedAtUtc" ASC
                LIMIT 1
                """,
                conn);
            importCmd.Parameters.AddWithValue("email", normalizedEmail);

            try
            {
                var inserted = await importCmd.ExecuteNonQueryAsync(cancellationToken);
                if (inserted > 0)
                {
                    logger.LogInformation(
                        "Imported lead {Email} from legacy schema {Schema} into public.Leads.",
                        normalizedEmail,
                        schema);
                    return true;
                }
            }
            catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
            {
                logger.LogDebug(ex, "Skipping legacy lead import from {Schema}.", schema);
            }
        }

        return false;
    }
}
