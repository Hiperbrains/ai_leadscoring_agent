using LeadScoring.Api.Models;

namespace LeadScoring.Api.Services;

public interface ITenantLeadScope
{
    string? GetCurrentUserEmail();

    Task<string> ResolveCompanyNameAsync(CancellationToken cancellationToken = default);

    Task EnsureTenantContextMatchesUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Scopes a leads query to the given company and the product currently active for the request
    /// (resolved via <see cref="IProductContext"/>). When the tenant has no products configured,
    /// the scope falls back to the legacy <see cref="TenantLeadScope.ScopedProductId"/> so existing
    /// fixtures keep working.
    /// </summary>
    IQueryable<Lead> ApplyScope(IQueryable<Lead> leads, string companyName);

    /// <summary>
    /// Scopes a leads query to the given company and an explicit product id. Use when the product
    /// id has already been resolved (e.g. inside background jobs or when chaining controller logic).
    /// </summary>
    IQueryable<Lead> ApplyScope(IQueryable<Lead> leads, string companyName, int productId);

    /// <summary>
    /// Returns the product id currently active for the request, or <c>null</c> when the tenant
    /// has no products configured yet.
    /// </summary>
    Task<int?> ResolveCurrentProductIdAsync(CancellationToken cancellationToken = default);
}
