namespace LeadScoring.Api.Models;

public enum EventType
{
    EmailClick,
    WebsiteActivity,
    BookDemo,
    BlogPost,
    PricingPage,
    Signup
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

public enum BatchType
{
    Daily,
    Weekly,
    Monthly
}

public enum BatchStatus
{
    Running,
    Completed,
    Failed
}

public enum BatchLeadStatus
{
    Pending,
    Success,
    Failed
}
