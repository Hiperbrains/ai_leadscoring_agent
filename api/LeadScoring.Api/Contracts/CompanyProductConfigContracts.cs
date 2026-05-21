using System.Text.Json;

namespace LeadScoring.Api.Contracts;

/// <summary>Minimum total score for each stage boundary: Cold &lt; WarmMin ≤ scores &lt; MqlMin are Warm; MQL &lt; HotMin.</summary>
public sealed class StageScoreThresholdsDto
{
    /// <summary>Minimum total score to qualify as Warm (default matches legacy cutoff: Cold was ≤50).</summary>
    public int WarmMin { get; set; } = StageScoreThresholdsNormalizer.DefaultWarmMin;

    /// <summary>Minimum total score to qualify as MQL.</summary>
    public int MqlMin { get; set; } = StageScoreThresholdsNormalizer.DefaultMqlMin;

    /// <summary>Minimum total score to qualify as Hot.</summary>
    public int HotMin { get; set; } = StageScoreThresholdsNormalizer.DefaultHotMin;
}

public static class StageScoreThresholdsNormalizer
{
    public const int DefaultWarmMin = 51;
    public const int DefaultMqlMin = 101;
    public const int DefaultHotMin = 151;

    private static readonly JsonSerializerOptions StageThresholdJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static StageScoreThresholdsDto CreateDefaultClone() =>
        new()
        {
            WarmMin = DefaultWarmMin,
            MqlMin = DefaultMqlMin,
            HotMin = DefaultHotMin
        };

    /// <returns>Canonical JSON object for persisted storage.</returns>
    public static string SerializeNormalized(StageScoreThresholdsDto normalized) =>
        JsonSerializer.Serialize(normalized, StageThresholdJson);

    public static bool TryNormalizeFromRequest(
        StageScoreThresholdsDto? request,
        out StageScoreThresholdsDto normalized,
        out string errorMessage)
    {
        if (request is null)
        {
            normalized = CreateDefaultClone();
            errorMessage = string.Empty;
            return true;
        }

        var warmMin = request.WarmMin;
        var mqlMin = request.MqlMin;
        var hotMin = request.HotMin;

        if (warmMin < 1)
        {
            normalized = CreateDefaultClone();
            errorMessage = "Minimum score for Warm must be at least 1.";
            return false;
        }

        if (mqlMin <= warmMin)
        {
            normalized = CreateDefaultClone();
            errorMessage = "Minimum score for MQL must be greater than the minimum score for Warm.";
            return false;
        }

        if (hotMin <= mqlMin)
        {
            normalized = CreateDefaultClone();
            errorMessage = "Minimum score for Hot must be greater than the minimum score for MQL.";
            return false;
        }

        normalized = new StageScoreThresholdsDto { WarmMin = warmMin, MqlMin = mqlMin, HotMin = hotMin };
        errorMessage = string.Empty;
        return true;
    }

    /// <summary>Parse thresholds from DB JSON; invalid or empty yields defaults (legacy behaviour).</summary>
    public static StageScoreThresholdsDto ParseOrDefaults(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateDefaultClone();
        }

        try
        {
            var dto = JsonSerializer.Deserialize<StageScoreThresholdsDto>(json, StageThresholdJson);
            if (dto is not null &&
                TryNormalizeFromRequest(dto, out var normalized, out _))
            {
                return normalized;
            }
        }
        catch
        {
        }

        return CreateDefaultClone();
    }
}

public sealed class UpsertCompanyProductConfigRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    /// <summary>Optional; ignored. ProductId is assigned by the server on create and preserved on update.</summary>
    public int ProductId { get; set; }
    public Dictionary<string, int> ProductEventConfig { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional warm/mql/hot minimum score boundaries; omitted means built-in defaults (51 / 101 / 151).</summary>
    public StageScoreThresholdsDto? StageThresholds { get; set; }
}

public sealed class CompanyProductConfigDto
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public Dictionary<string, int> ProductEventConfig { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public StageScoreThresholdsDto StageThresholds { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
}

public static class CompanyProductConfigMapper
{
    public static CompanyProductConfigDto ToDto(
        Guid id,
        string companyName,
        string productName,
        int productId,
        string productEventConfigJson,
        string? stageThresholdsJson,
        DateTime createdAtUtc)
    {
        Dictionary<string, int>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(productEventConfigJson);
        }
        catch
        {
            parsed = null;
        }

        var stages = StageScoreThresholdsNormalizer.ParseOrDefaults(stageThresholdsJson);

        return new CompanyProductConfigDto
        {
            Id = id,
            CompanyName = companyName,
            ProductName = productName,
            ProductId = productId,
            ProductEventConfig = parsed ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            StageThresholds = stages,
            CreatedAtUtc = createdAtUtc
        };
    }
}
