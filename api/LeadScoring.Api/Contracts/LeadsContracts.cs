namespace LeadScoring.Api.Contracts;

public record SendEmailRequest(string Subject, string HtmlBody, string RedirectUrl);
