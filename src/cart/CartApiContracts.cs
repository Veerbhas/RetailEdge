namespace RetailEdge.Cart;

public sealed record CartApiError(
    string Code,
    string Message);

public sealed record CartApiErrorResponse(
    CartApiError Error);

public sealed record CartRecoveryApiResponse(
    Guid CartId,
    CartRecoveryStatus RecoveryStatus,
    DateTimeOffset? AbandonmentTimestamp,
    DateTimeOffset? RecoveryTimestamp,
    string? VoucherCode,
    int Version);

public sealed record CartAnalyticsItem(
    Guid CartId,
    Guid CustomerId,
    CartRecoveryStatus RecoveryStatus,
    DateTimeOffset? AbandonmentTimestamp,
    DateTimeOffset? RecoveryTimestamp);

public sealed record CartAnalyticsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyCollection<CartAnalyticsItem> Items,
    string? NextCursor);
