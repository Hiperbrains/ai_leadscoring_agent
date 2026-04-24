using Microsoft.AspNetCore.Http;

namespace LeadScoring.Api.Contracts;

public class ImportLeadsRequest
{
    public IFormFile? File { get; set; }
    public string? Source { get; set; }
}
