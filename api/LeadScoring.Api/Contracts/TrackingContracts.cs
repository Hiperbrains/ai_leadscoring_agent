namespace LeadScoring.Api.Contracts;

public record CaptureEmailRequest(
    string? VisitorId,
    string Email,
    string? Source,
    string Redirect,
    string? Campaign,
    long? DwellMs);

public record CaptureEmailResponse(string RedirectUrl, string? VisitorId = null);

public record VisitorEmailHintResponse(string? Email, bool AlreadyCaptured, string? VisitorId);

public record SkipEmailGateRequest(
    string? VisitorId,
    string? Source,
    string Redirect,
    string? Campaign,
    long? DwellMs);

public record RedirectMergeResponse(string RedirectUrl);

public record TrackEventRequest(
    string VisitorId,
    string? Source,
    string? EventType,
    string? Campaign,
    string? MetadataJson,
    Guid? LeadId);
