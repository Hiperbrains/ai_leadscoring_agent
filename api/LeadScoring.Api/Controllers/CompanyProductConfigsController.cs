using System.Text.Json;
using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/company-product-configs")]
[Authorize]
public class CompanyProductConfigsController(LeadScoringDbContext db, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertCompanyProductConfigRequest request)
    {
        tenantContext.RequireTenant();
        request.CompanyName = tenantContext.CompanyName!;

        if (!TryNormalizeRequest(request, out var normalizedItems, out var normalizedProductUrl, out var errorMessage))
        {
            return BadRequest(errorMessage);
        }

        if (!StageScoreThresholdsNormalizer.TryNormalizeFromRequest(request.StageThresholds, out var stageThresholds, out var stageError))
        {
            return BadRequest(stageError);
        }

        var configJson = JsonSerializer.Serialize(normalizedItems);
        var stageJson = StageScoreThresholdsNormalizer.SerializeNormalized(stageThresholds);
        var nextProductId = await GetNextProductIdAsync();
        var entity = new CompanyProductConfig
        {
            Id = Guid.NewGuid(),
            CompanyName = tenantContext.CompanyName!.Trim(),
            ProductName = request.ProductName.Trim(),
            ProductUrl = normalizedProductUrl,
            ProductId = nextProductId,
            ProductEventConfigJson = configJson,
            StageThresholdsJson = stageJson,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.CompanyProductConfigs.Add(entity);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("A config already exists for this company, product, and product ID.");
        }

        var dto = CompanyProductConfigMapper.ToDto(
            entity.Id,
            entity.CompanyName,
            entity.ProductName,
            entity.ProductUrl,
            entity.ProductId,
            entity.ProductEventConfigJson,
            entity.StageThresholdsJson,
            entity.CreatedAtUtc);

        return Ok(dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertCompanyProductConfigRequest request)
    {
        tenantContext.RequireTenant();
        request.CompanyName = tenantContext.CompanyName!;

        if (!TryNormalizeRequest(request, out var normalizedItems, out var normalizedProductUrl, out var errorMessage))
        {
            return BadRequest(errorMessage);
        }

        if (!StageScoreThresholdsNormalizer.TryNormalizeFromRequest(request.StageThresholds, out var stageThresholds, out var stageError))
        {
            return BadRequest(stageError);
        }

        var entity = await db.CompanyProductConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null || !BelongsToTenant(entity))
        {
            return NotFound("Company product config not found.");
        }

        entity.CompanyName = tenantContext.CompanyName!.Trim();
        entity.ProductName = request.ProductName.Trim();
        entity.ProductUrl = normalizedProductUrl;
        entity.ProductEventConfigJson = JsonSerializer.Serialize(normalizedItems);
        entity.StageThresholdsJson = StageScoreThresholdsNormalizer.SerializeNormalized(stageThresholds);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("A config already exists for this company, product, and product ID.");
        }

        var dto = CompanyProductConfigMapper.ToDto(
            entity.Id,
            entity.CompanyName,
            entity.ProductName,
            entity.ProductUrl,
            entity.ProductId,
            entity.ProductEventConfigJson,
            entity.StageThresholdsJson,
            entity.CreatedAtUtc);

        return Ok(dto);
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        tenantContext.RequireTenant();
        var tenant = tenantContext.CompanyName!.Trim();
        var tenantLower = tenant.ToLowerInvariant();
        var query = db.CompanyProductConfigs.AsNoTracking()
            .Where(x => x.CompanyName.ToLower() == tenantLower);

        var records = await query
            .OrderBy(x => x.ProductName)
            .ThenBy(x => x.ProductId)
            .Select(x => CompanyProductConfigMapper.ToDto(
                x.Id,
                x.CompanyName,
                x.ProductName,
                x.ProductUrl,
                x.ProductId,
                x.ProductEventConfigJson,
                x.StageThresholdsJson,
                x.CreatedAtUtc))
            .ToListAsync();

        return Ok(records);
    }

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id) => DeleteImplAsync(id);

    /// <summary>
    /// POST alias for deletes (SPA uses this so the path does not collide with PUT {id}; also works when proxies block DELETE).
    /// </summary>
    [HttpPost("{id:guid}/delete")]
    public Task<IActionResult> DeletePost(Guid id) => DeleteImplAsync(id);

    private bool BelongsToTenant(CompanyProductConfig entity) =>
        string.Equals(entity.CompanyName, tenantContext.CompanyName, StringComparison.OrdinalIgnoreCase);

    private async Task<IActionResult> DeleteImplAsync(Guid id)
    {
        tenantContext.RequireTenant();
        var entity = await db.CompanyProductConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null || !BelongsToTenant(entity))
        {
            return NotFound("Company product config not found.");
        }

        db.CompanyProductConfigs.Remove(entity);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static bool TryNormalizeRequest(
        UpsertCompanyProductConfigRequest request,
        out Dictionary<string, int> normalizedItems,
        out string normalizedProductUrl,
        out string errorMessage)
    {
        normalizedProductUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            normalizedItems = new();
            errorMessage = "Company context is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.ProductName))
        {
            normalizedItems = new();
            errorMessage = "Product name is required.";
            return false;
        }

        if (!ProductUrlNormalizer.TryNormalize(request.ProductUrl, out normalizedProductUrl, out var urlError))
        {
            normalizedItems = new();
            errorMessage = urlError;
            return false;
        }

        if (request.ProductEventConfig.Count == 0)
        {
            normalizedItems = new();
            errorMessage = "At least one event config item is required.";
            return false;
        }

        normalizedItems = request.ProductEventConfig
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(
                x => x.Key.Trim(),
                x => Math.Max(0, x.Value),
                StringComparer.OrdinalIgnoreCase);

        if (normalizedItems.Count == 0)
        {
            errorMessage = "At least one valid event config item is required.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private async Task<int> GetNextProductIdAsync()
    {
        var tenant = tenantContext.CompanyName!.Trim();
        var tenantLower = tenant.ToLowerInvariant();
        // Avoid DefaultIfEmpty + MaxAsync: not translatable on all EF Core / Npgsql combinations.
        var max = await db.CompanyProductConfigs
            .Where(x => x.CompanyName.ToLower() == tenantLower)
            .MaxAsync(x => (int?)x.ProductId);
        return (max ?? 0) + 1;
    }
}
