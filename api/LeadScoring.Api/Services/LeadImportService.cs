using System.Globalization;
using CsvHelper;
using ClosedXML.Excel;
using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

public class LeadImportService(LeadScoringDbContext db)
{
    public async Task<LeadImportResult> ImportFromFileAsync(IFormFile file, string? source)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        List<LeadImportRowDto> rows;

        await using var stream = file.OpenReadStream();
        rows = extension switch
        {
            ".csv" => await ParseCsvAsync(stream, source),
            ".xlsx" => ParseExcel(stream, source),
            _ => throw new InvalidOperationException("Unsupported file type. Use .csv or .xlsx")
        };

        return await ImportRowsAsync(rows);
    }

    public Task<LeadImportResult> ImportFromPayloadAsync(LeadImportPayload payload)
    {
        var normalizedRows = payload.Leads.Select(x => new LeadImportRowDto(
            x.Email,
            x.FirstName,
            x.LastName,
            string.IsNullOrWhiteSpace(x.Source) ? payload.Source : x.Source)).ToList();

        return ImportRowsAsync(normalizedRows);
    }

    private async Task<LeadImportResult> ImportRowsAsync(List<LeadImportRowDto> rows)
    {
        var processed = 0;
        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var row in rows)
        {
            processed++;
            var email = row.Email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
            {
                skipped++;
                errors.Add($"Row {processed}: missing email.");
                continue;
            }

            try
            {
                var existing = await db.Leads.FirstOrDefaultAsync(x => x.Email == email);
                if (existing is null)
                {
                    db.Leads.Add(new Lead
                    {
                        Id = Guid.NewGuid(),
                        Email = email,
                        FirstName = Sanitize(row.FirstName),
                        LastName = Sanitize(row.LastName),
                        CreatedAtUtc = DateTime.UtcNow,
                        LastActivityUtc = DateTime.UtcNow
                    });
                    imported++;
                }
                else
                {
                    existing.FirstName = Sanitize(row.FirstName) ?? existing.FirstName;
                    existing.LastName = Sanitize(row.LastName) ?? existing.LastName;
                    updated++;
                }
            }
            catch (Exception ex)
            {
                skipped++;
                errors.Add($"Row {processed}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync();
        return new LeadImportResult(processed, imported, updated, skipped, errors);
    }

    private static async Task<List<LeadImportRowDto>> ParseCsvAsync(Stream stream, string? source)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord?.ToList() ?? [];
        var emailHeader = FindColumn(headers, "email", "email address", "e-mail");
        var firstNameHeader = FindColumn(headers, "firstname", "first name", "first_name");
        var lastNameHeader = FindColumn(headers, "lastname", "last name", "last_name");

        if (emailHeader is null)
        {
            throw new InvalidOperationException("CSV must include an email column.");
        }

        var rows = new List<LeadImportRowDto>();
        while (await csv.ReadAsync())
        {
            var email = csv.GetField(emailHeader);
            if (string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            var first = firstNameHeader is null ? null : csv.GetField(firstNameHeader);
            var last = lastNameHeader is null ? null : csv.GetField(lastNameHeader);
            rows.Add(new LeadImportRowDto(email, first, last, source));
        }

        return rows;
    }

    private static List<LeadImportRowDto> ParseExcel(Stream stream, string? source)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        var headerCells = sheet.Row(1).CellsUsed().ToList();
        var headers = headerCells.Select(c => c.GetString()).ToList();

        var emailHeader = FindColumn(headers, "email", "email address", "e-mail");
        var firstNameHeader = FindColumn(headers, "firstname", "first name", "first_name");
        var lastNameHeader = FindColumn(headers, "lastname", "last name", "last_name");

        if (emailHeader is null)
        {
            throw new InvalidOperationException("Excel must include an email column.");
        }

        var emailIdx = headers.FindIndex(h => Normalize(h) == Normalize(emailHeader)) + 1;
        var firstIdx = firstNameHeader is null ? -1 : headers.FindIndex(h => Normalize(h) == Normalize(firstNameHeader)) + 1;
        var lastIdx = lastNameHeader is null ? -1 : headers.FindIndex(h => Normalize(h) == Normalize(lastNameHeader)) + 1;

        var rows = new List<LeadImportRowDto>();
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var email = row.Cell(emailIdx).GetString();
            if (string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            var first = firstIdx < 1 ? null : row.Cell(firstIdx).GetString();
            var last = lastIdx < 1 ? null : row.Cell(lastIdx).GetString();
            rows.Add(new LeadImportRowDto(email, first, last, source));
        }

        return rows;
    }

    private static string? FindColumn(List<string> headers, params string[] aliases)
    {
        return headers.FirstOrDefault(h => aliases.Contains(Normalize(h)));
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string? Sanitize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
