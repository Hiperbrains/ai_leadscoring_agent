namespace LeadScoring.Api.Contracts;

public record CaptureEmailRequest(
    string VisitorId,
    string Email,
    string? Source,
    string Redirect,
    string? Campaign,
    long? DwellMs);

public record CaptureEmailResponse(string RedirectUrl);

public record VisitorEmailHintResponse(string? Email, bool AlreadyCaptured);

public record RedirectMergeResponse(string RedirectUrl);

public record TrackEventRequest(
    string VisitorId,
    string? Source,
    string? EventType,
    string? Campaign,
    string? MetadataJson,
    Guid? LeadId);
