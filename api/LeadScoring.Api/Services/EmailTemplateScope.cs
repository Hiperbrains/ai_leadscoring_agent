using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

/// <summary>
/// Scopes email template queries by company first, then product, so shared <c>public</c> schema rows
/// do not cross-match when multiple companies reuse the same numeric <see cref="EmailTemplate.ProductId"/>.
/// </summary>
public static class EmailTemplateScope
{
    public static IQueryable<EmailTemplate> ApplyLeadScope(IQueryable<EmailTemplate> query, Lead lead)
    {
        var companyName = lead.CompanyName?.Trim();
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            query = query.Where(t =>
                t.CompanyName == null
                || (t.CompanyName != null && EF.Functions.ILike(t.CompanyName, companyName)));
        }

        return query.Where(t => t.ProductId == lead.ProductId || t.ProductId == null);
    }

    public static IQueryable<EmailTemplate> OrderForLead(IQueryable<EmailTemplate> query, Lead lead)
    {
        var companyName = lead.CompanyName?.Trim();
        return query
            .OrderByDescending(t =>
                companyName != null
                && t.CompanyName != null
                && EF.Functions.ILike(t.CompanyName, companyName))
            .ThenByDescending(t => t.ProductId == lead.ProductId)
            .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt);
    }

    public static IQueryable<EmailTemplate> ApplyProductScope(
        IQueryable<EmailTemplate> query,
        string? companyName,
        int? productId)
    {
        var normalizedCompany = companyName?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedCompany))
        {
            query = query.Where(t =>
                t.CompanyName == null
                || (t.CompanyName != null && EF.Functions.ILike(t.CompanyName, normalizedCompany)));
        }

        return query.Where(t => t.ProductId == productId || t.ProductId == null);
    }

    public static IQueryable<EmailTemplate> OrderForProduct(
        IQueryable<EmailTemplate> query,
        string? companyName,
        int? productId)
    {
        var normalizedCompany = companyName?.Trim();
        return query
            .OrderByDescending(t =>
                normalizedCompany != null
                && t.CompanyName != null
                && EF.Functions.ILike(t.CompanyName, normalizedCompany))
            .ThenByDescending(t => t.ProductId == productId)
            .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt);
    }
}
