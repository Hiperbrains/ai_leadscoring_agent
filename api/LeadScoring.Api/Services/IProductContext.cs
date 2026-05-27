namespace LeadScoring.Api.Services;

public interface IProductContext
{
    /// <summary>
    /// Resolves the ProductId selected by the current request.
    /// Reads from header <c>X-Product-Id</c>, validates ownership against the tenant's
    /// <c>CompanyProductConfigs</c>, and falls back to the tenant's lowest configured product id
    /// when the header is missing or invalid. Returns <c>null</c> when the tenant has no products yet.
    /// </summary>
    Task<int?> GetCurrentProductIdAsync(CancellationToken cancellationToken = default);
}
