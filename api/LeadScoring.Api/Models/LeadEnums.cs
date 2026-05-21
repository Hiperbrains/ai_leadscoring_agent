namespace LeadScoring.Api.Models;

public enum EventType
{
    EmailClick,
    WebsiteActivity,
    BookDemo,
    BlogPost,
    PricingPage,
    Signup,
    EmailCaptured
}

public enum EventSource
{
    Unknown = 0,
    Email = 1,
    Website = 2,
    LinkedIn = 3,
    Direct = 4,
    Organic = 5
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

public enum CampaignBatchType
{
    Day1 = 1,
    Day2 = 2,
    Day3 = 3,
    Day4 = 4,
    /// <summary>Manual: stage Warm, initial template (<see cref="EmailTemplate.IsFollowUp"/> false), same eligibility slice as Day 3 filtered by stage.</summary>
    Warm = 5,
    /// <summary>Manual: stage Warm follow-up templates; Day 4–style inactive slice filtered by stage.</summary>
    WarmFollowUp = 6,
    /// <summary>Manual: MQL initial templates; Day 3 slice filtered to <see cref="LeadStage.Mql"/>.</summary>
    Mql = 7,
    /// <summary>Manual: MQL follow-up templates; Day 4 slice filtered to <see cref="LeadStage.Mql"/>.</summary>
    MqlFollowUp = 8,
    /// <summary>Manual: Hot initial templates; Day 3 slice filtered to <see cref="LeadStage.Hot"/>.</summary>
    Hot = 9,
    /// <summary>Manual: Hot follow-up templates; Day 4 slice filtered to <see cref="LeadStage.Hot"/>.</summary>
    HotFollowUp = 10
}
