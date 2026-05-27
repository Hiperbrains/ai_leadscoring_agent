using System.Globalization;
using LeadScoring.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

/// <summary>
/// Resolves the currently active <c>ProductId</c> for an authenticated request.
/// The Angular UI selects a product in the global navbar and sends it back as the
/// <c>X-Product-Id</c> header on every API call. We validate ownership against the
/// tenant's <see cref="Models.CompanyProductConfig"/> rows so a stale client cannot leak data
/// across products, and we cache the resolved value per request.
/// </summary>
public class ProductContext(
    IHttpContextAccessor httpContextAccessor,
    LeadScoringDbContext db,
    ITenantContext tenantContext,
    IAmbientTenantState ambient) : IProductContext
{
    private const string HeaderName = "X-Product-Id";
    private const string HttpItemsKey = "__ResolvedProductId";

    public async Task<int?> GetCurrentProductIdAsync(CancellationToken cancellationToken = default)
    {
        if (ambient.ProductId.HasValue)
        {
            return ambient.ProductId;
        }

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null && httpContext.Items.TryGetValue(HttpItemsKey, out var cached) && cached is int cachedId)
        {
            return cachedId;
        }

        var companyName = tenantContext.CompanyName?.Trim();
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return null;
        }

        var companyLower = companyName.ToLowerInvariant();
        var configuredProductIds = await db.CompanyProductConfigs
            .AsNoTracking()
            .Where(x => x.CompanyName.ToLower() == companyLower)
            .Select(x => x.ProductId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (configuredProductIds.Count == 0)
        {
            return null;
        }

        var requested = ReadRequestedProductId(httpContext);
        int resolved;
        if (requested.HasValue && configuredProductIds.Contains(requested.Value))
        {
            resolved = requested.Value;
        }
        else
        {
            resolved = configuredProductIds.Min();
        }

        if (httpContext is not null)
        {
            httpContext.Items[HttpItemsKey] = resolved;
        }

        return resolved;
    }

    private static int? ReadRequestedProductId(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var raw))
        {
            return null;
        }

        var value = raw.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
