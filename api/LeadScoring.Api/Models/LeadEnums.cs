namespace LeadScoring.Api.Models;

public enum EventType
{
    Open,
    EmailClick,
    WebsiteActivity
}

public enum EventSource
{
    Email,
    Website
}

public enum LeadStage
{
    Cold,
    Warm,
    Mql,
    Hot
}
