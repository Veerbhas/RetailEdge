namespace RetailEdge.Cart;

public enum CartCheckoutStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3,
    Abandoned = 4
}

public enum CartRecoveryStatus
{
    Pending = 0,
    Notified = 1,
    Converted = 2,
    Expired = 3
}

public sealed class CartItem
{
    private CartItem()
    {
        ProductId = Guid.Empty;
    }

    private CartItem(Guid cartId, Guid productId, int quantity, decimal unitPrice)
    {
        Id = Guid.NewGuid();
        CartId = cartId;
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; private set; }

    public Guid CartId { get; private set; }

    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    internal static CartItem Create(
        Guid cartId,
        Guid productId,
        int quantity,
        decimal unitPrice)
    {
        if (cartId == Guid.Empty)
        {
            throw new ArgumentException("Cart ID is required.", nameof(cartId));
        }
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product ID is required.", nameof(productId));
        }
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Cart item quantity must be greater than zero.");
        }
        if (unitPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                unitPrice,
                "Unit price cannot be negative.");
        }

        return new CartItem(cartId, productId, quantity, unitPrice);
    }

    internal void SetCartId(Guid cartId)
    {
        if (cartId == Guid.Empty)
        {
            throw new ArgumentException("Cart ID is required.", nameof(cartId));
        }

        CartId = cartId;
    }
}

public sealed class AbandonedCart
{
    private readonly List<CartItem> items = [];

    private AbandonedCart()
    {
        SessionId = Guid.Empty;
        CustomerId = Guid.Empty;
        Currency = string.Empty;
    }

    private AbandonedCart(
        Guid customerId,
        Guid sessionId,
        IEnumerable<CartItem> items,
        decimal cartTotal,
        string currency,
        DateTimeOffset lastActivityTimestamp)
    {
        Id = Guid.NewGuid();
        CustomerId = customerId;
        SessionId = sessionId;
        CartTotal = cartTotal;
        Currency = currency;
        CheckoutStatus = CartCheckoutStatus.Pending;
        LastActivityTimestamp = lastActivityTimestamp;
        LastActivityTimestampTicks = lastActivityTimestamp.UtcTicks;
        RecoveryStatus = CartRecoveryStatus.Pending;
        Version = 1;
        this.items = items.ToList();
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid SessionId { get; private set; }

    public IReadOnlyCollection<CartItem> Items => items;

    public decimal CartTotal { get; private set; }

    public string Currency { get; private set; }

    public CartCheckoutStatus CheckoutStatus { get; private set; }

    public DateTimeOffset? AbandonmentTimestamp { get; private set; }

    public long? AbandonmentTimestampTicks { get; private set; }

    public CartRecoveryStatus RecoveryStatus { get; private set; }

    public string? VoucherCode { get; private set; }

    public DateTimeOffset? RecoveryTimestamp { get; private set; }

    public DateTimeOffset LastActivityTimestamp { get; private set; }

    public long LastActivityTimestampTicks { get; private set; }

    public int Version { get; private set; }

    public static AbandonedCart Create(
        Guid customerId,
        Guid sessionId,
        IEnumerable<(Guid ProductId, int Quantity, decimal UnitPrice)> itemValues,
        string currency,
        DateTimeOffset lastActivityTimestamp)
    {
        ArgumentNullException.ThrowIfNull(itemValues);
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer ID is required.", nameof(customerId));
        }
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }

        string normalizedCurrency = NormalizeCurrency(currency);
        var values = itemValues.ToList();
        if (values.Count == 0)
        {
            throw new ArgumentException("A cart must contain at least one item.", nameof(itemValues));
        }

        decimal cartTotal = 0m;
        foreach ((Guid productId, int quantity, decimal unitPrice) in values)
        {
            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(itemValues),
                    quantity,
                    "Cart item quantity must be greater than zero.");
            }
            if (unitPrice < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(itemValues),
                    unitPrice,
                    "Unit price cannot be negative.");
            }
            if (productId == Guid.Empty)
            {
                throw new ArgumentException("Product ID is required.", nameof(itemValues));
            }

            cartTotal = checked(cartTotal + unitPrice * quantity);
        }

        Guid provisionalCartId = Guid.NewGuid();
        List<CartItem> items = values
            .Select(value => CartItem.Create(
                provisionalCartId,
                value.ProductId,
                value.Quantity,
                value.UnitPrice))
            .ToList();

        var cart = new AbandonedCart(
            customerId,
            sessionId,
            items,
            cartTotal,
            normalizedCurrency,
            lastActivityTimestamp);

        foreach (CartItem item in items)
        {
            item.SetCartId(cart.Id);
        }

        return cart;
    }

    public void MarkAbandoned(DateTimeOffset abandonmentTimestamp)
    {
        if (CheckoutStatus != CartCheckoutStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only a pending-checkout cart can be marked abandoned.");
        }
        if (AbandonmentTimestamp is not null)
        {
            return;
        }
        if (abandonmentTimestamp < LastActivityTimestamp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(abandonmentTimestamp),
                "The abandonment timestamp cannot precede cart activity.");
        }

        CheckoutStatus = CartCheckoutStatus.Abandoned;
        AbandonmentTimestamp = abandonmentTimestamp;
        AbandonmentTimestampTicks = abandonmentTimestamp.UtcTicks;
        RecoveryStatus = CartRecoveryStatus.Pending;
        Version = checked(Version + 1);
    }

    public void AttachVoucher(string voucherCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(voucherCode);
        string normalizedVoucherCode = voucherCode.Trim();
        if (normalizedVoucherCode.Length > 100)
        {
            throw new ArgumentException(
                "Voucher code cannot exceed 100 characters.",
                nameof(voucherCode));
        }
        if (VoucherCode is not null)
        {
            return;
        }

        VoucherCode = normalizedVoucherCode;
        Version = checked(Version + 1);
    }

    public void MarkNotified()
    {
        if (RecoveryStatus != CartRecoveryStatus.Pending)
        {
            return;
        }

        RecoveryStatus = CartRecoveryStatus.Notified;
        Version = checked(Version + 1);
    }

    public void MarkConverted(
        Guid completingCustomerId,
        DateTimeOffset completedAt,
        TimeSpan recoveryWindow)
    {
        if (completingCustomerId != CustomerId)
        {
            throw new InvalidOperationException(
                "The completing customer must own the abandoned cart.");
        }
        if (RecoveryStatus is CartRecoveryStatus.Converted
            or CartRecoveryStatus.Expired)
        {
            return;
        }
        if (AbandonmentTimestamp is null)
        {
            throw new InvalidOperationException(
                "A cart must be abandoned before it can be converted.");
        }
        if (completedAt > AbandonmentTimestamp.Value + recoveryWindow)
        {
            throw new InvalidOperationException(
                "The recovery window has elapsed.");
        }

        RecoveryStatus = CartRecoveryStatus.Converted;
        RecoveryTimestamp = completedAt;
        Version = checked(Version + 1);
    }

    public void MarkExpired(DateTimeOffset expiredAt)
    {
        if (RecoveryStatus is CartRecoveryStatus.Converted
            or CartRecoveryStatus.Expired)
        {
            return;
        }
        if (AbandonmentTimestamp is null)
        {
            throw new InvalidOperationException(
                "A cart must be abandoned before it can expire.");
        }

        RecoveryStatus = CartRecoveryStatus.Expired;
        RecoveryTimestamp = expiredAt;
        Version = checked(Version + 1);
    }

    private static string NormalizeCurrency(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        string normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3)
        {
            throw new ArgumentException(
                "Currency must be a three-character ISO-style code.",
                nameof(currency));
        }
        if (!normalized.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                "Currency must contain letters only.",
                nameof(currency));
        }

        return normalized;
    }
}

public sealed class CartRecoveryTransition
{
    private CartRecoveryTransition()
    {
        Reason = string.Empty;
    }

    private CartRecoveryTransition(
        Guid cartId,
        CartRecoveryStatus fromStatus,
        CartRecoveryStatus toStatus,
        DateTimeOffset timestamp,
        string reason,
        string? correlationId)
    {
        Id = Guid.NewGuid();
        CartId = cartId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Timestamp = timestamp;
        Reason = reason;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid CartId { get; private set; }

    public CartRecoveryStatus FromStatus { get; private set; }

    public CartRecoveryStatus ToStatus { get; private set; }

    public DateTimeOffset Timestamp { get; private set; }

    public string Reason { get; private set; }

    public string? CorrelationId { get; private set; }

    public static CartRecoveryTransition Create(
        Guid cartId,
        CartRecoveryStatus fromStatus,
        CartRecoveryStatus toStatus,
        DateTimeOffset timestamp,
        string reason,
        string? correlationId = null)
    {
        if (cartId == Guid.Empty)
        {
            throw new ArgumentException("Cart ID is required.", nameof(cartId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        string normalizedReason = reason.Trim();
        if (normalizedReason.Length > 100)
        {
            throw new ArgumentException(
                "Transition reason cannot exceed 100 characters.",
                nameof(reason));
        }

        return new CartRecoveryTransition(
            cartId,
            fromStatus,
            toStatus,
            timestamp,
            normalizedReason,
            correlationId);
    }
}

public sealed class CartScanCursor
{
    private CartScanCursor()
    {
        ScanKey = string.Empty;
    }

    private CartScanCursor(
        string scanKey,
        DateTimeOffset? lastActivityTimestamp,
        Guid? lastCartId,
        DateTimeOffset updatedTimestamp)
    {
        ScanKey = scanKey;
        LastActivityTimestamp = lastActivityTimestamp;
        LastCartId = lastCartId;
        UpdatedTimestamp = updatedTimestamp;
    }

    public string ScanKey { get; private set; }

    public DateTimeOffset? LastActivityTimestamp { get; private set; }

    public Guid? LastCartId { get; private set; }

    public DateTimeOffset UpdatedTimestamp { get; private set; }

    public static CartScanCursor Create(
        string scanKey,
        DateTimeOffset? lastActivityTimestamp,
        Guid? lastCartId,
        DateTimeOffset updatedTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scanKey);
        string normalizedKey = scanKey.Trim();
        if (normalizedKey.Length > 100)
        {
            throw new ArgumentException(
                "Scan key cannot exceed 100 characters.",
                nameof(scanKey));
        }

        return new CartScanCursor(
            normalizedKey,
            lastActivityTimestamp,
            lastCartId,
            updatedTimestamp);
    }

    public void Advance(
        DateTimeOffset lastActivityTimestamp,
        Guid lastCartId,
        DateTimeOffset updatedTimestamp)
    {
        if (lastCartId == Guid.Empty)
        {
            throw new ArgumentException("Cart ID is required.", nameof(lastCartId));
        }

        LastActivityTimestamp = lastActivityTimestamp;
        LastCartId = lastCartId;
        UpdatedTimestamp = updatedTimestamp;
    }

    public void Complete(DateTimeOffset updatedTimestamp)
    {
        LastActivityTimestamp = null;
        LastCartId = null;
        UpdatedTimestamp = updatedTimestamp;
    }
}
