namespace LeadScoring.Api.Services;

public class ConsoleEmailService(ILogger<ConsoleEmailService> logger) : IEmailService
{
    public Task SendAsync(string to, string subject, string htmlBody)
    {
        logger.LogInformation("Email -> {To} | Subject: {Subject} | Body: {HtmlBody}", to, subject, htmlBody);
        return Task.CompletedTask;
    }
}
