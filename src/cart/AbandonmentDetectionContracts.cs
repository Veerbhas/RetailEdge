namespace RetailEdge.Cart;

public sealed record AbandonmentDetectionOptions
{
    public const string SectionName = "CartAbandonment";

    public int InactiveMinutes { get; init; } = 30;

    public decimal VoucherThreshold { get; init; }

    public int BatchSize { get; init; } = 100;

    public void Validate()
    {
        if (InactiveMinutes <= 0)
        {
            throw new InvalidOperationException(
                "CartAbandonment:InactiveMinutes must be greater than zero.");
        }
        if (VoucherThreshold < 0m)
        {
            throw new InvalidOperationException(
                "CartAbandonment:VoucherThreshold cannot be negative.");
        }
        if (BatchSize is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "CartAbandonment:BatchSize must be between 1 and 1000.");
        }
    }
}

public sealed record CartRecoveryNotification(
    Guid CartId,
    Guid CustomerId,
    decimal CartTotal,
    string Currency,
    IReadOnlyCollection<CartItemNotification> Items,
    string? VoucherCode,
    string CorrelationId);

public sealed record CartItemNotification(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice);

public sealed record AbandonmentScanResult(
    int ScannedCount,
    int AbandonedCount,
    int NotificationCount,
    int VoucherCount,
    CartScanCursor? Cursor);

public interface IVoucherGenerator
{
    string GenerateOneTimeVoucher(Guid cartId, decimal cartTotal, string currency);
}

public interface ICartNotificationPublisher
{
    Task PublishAsync(
        CartRecoveryNotification notification,
        CancellationToken cancellationToken = default);
}
