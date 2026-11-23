namespace RetailEdge.Cart;

public sealed record RecoveryTrackingOptions
{
    public const string SectionName = "CartRecovery";

    public int RecoveryWindowHours { get; init; } = 24;

    public void Validate()
    {
        if (RecoveryWindowHours <= 0)
        {
            throw new InvalidOperationException(
                "CartRecovery:RecoveryWindowHours must be greater than zero.");
        }
    }
}

public sealed record OrderCompletedEvent(
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset CompletedAt,
    Guid? CartId,
    string? CorrelationId);

public sealed record RecoveryTrackingResult(
    Guid? CartId,
    Guid? CustomerId,
    CartRecoveryStatus Status,
    DateTimeOffset? RecoveryTimestamp,
    bool Changed);
