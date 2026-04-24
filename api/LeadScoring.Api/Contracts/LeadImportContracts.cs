namespace LeadScoring.Api.Contracts;

public record LeadImportRowDto(
    string Email,
    string? FirstName,
    string? LastName,
    string? Source);

public class LeadImportPayload
{
    public string? Source { get; set; }
    public List<LeadImportRowDto> Leads { get; set; } = [];
}

public record LeadImportResult(
    int Processed,
    int Imported,
    int Updated,
    int Skipped,
    List<string> Errors);
