using System.Text.RegularExpressions;

namespace LeadScoring.Api.Services;

public static class TenantConnectionStringBuilder
{
    public const string SharedSchemaName = "public";

    private static readonly Regex SafeCompanyPart = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Unique value stored on <see cref="Models.Tenant.DatabaseName"/> (not a PostgreSQL schema).
    /// All application data is stored in the shared <see cref="SharedSchemaName"/> schema.
    /// </summary>
    public static string ToTenantDatabaseKey(Guid tenantId) => tenantId.ToString("D");

    /// <summary>
    /// Legacy schema name from company name (schema-per-tenant installs only).
    /// </summary>
    public static string LegacySchemaNameFromCompany(string companyName)
    {
        var slug = SafeCompanyPart.Replace(companyName.Trim().ToLowerInvariant(), "");
        if (string.IsNullOrEmpty(slug))
        {
            slug = "company";
        }

        slug = slug.Length > 40 ? slug[..40] : slug;
        return $"tenant_{slug}";
    }
}
