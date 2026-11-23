using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RetailEdge.Cart;

public interface IRecoveryTrackingService
{
    Task<RecoveryTrackingResult> HandleOrderCompletedAsync(
        OrderCompletedEvent orderCompleted,
        CancellationToken cancellationToken = default);

    Task<int> ExpireRecoveriesAsync(
        CancellationToken cancellationToken = default);
}

public sealed class RecoveryTrackingService(
    CartDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<RecoveryTrackingOptions> options,
    ILogger<RecoveryTrackingService> logger)
    : IRecoveryTrackingService
{
    public async Task<RecoveryTrackingResult> HandleOrderCompletedAsync(
        OrderCompletedEvent orderCompleted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderCompleted);
        if (orderCompleted.OrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Order ID is required.",
                nameof(orderCompleted));
        }
        if (orderCompleted.CustomerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Customer ID is required.",
                nameof(orderCompleted));
        }

        RecoveryTrackingOptions settings = options.Value;
        settings.Validate();
        TimeSpan recoveryWindow =
            TimeSpan.FromHours(settings.RecoveryWindowHours);

        AbandonedCart? cart;
        if (orderCompleted.CartId is Guid cartId)
        {
            cart = await dbContext.Carts
                .SingleOrDefaultAsync(
                    value => value.Id == cartId,
                    cancellationToken);

            if (cart is not null
                && cart.CustomerId != orderCompleted.CustomerId)
            {
                logger.LogWarning(
                    "Order completion ownership mismatch for cart {CartId}.",
                    cartId);
                throw new CartRecoveryAuthorizationException();
            }
        }
        else
        {
            int pendingStatus = (int)CartRecoveryStatus.Pending;
            int notifiedStatus = (int)CartRecoveryStatus.Notified;
            cart = await dbContext.Carts
                .Where(value =>
                    value.CustomerId == orderCompleted.CustomerId
                    && (EF.Property<int>(
                        value,
                        nameof(AbandonedCart.RecoveryStatus)) == pendingStatus
                        || EF.Property<int>(
                            value,
                            nameof(AbandonedCart.RecoveryStatus)) == notifiedStatus))
                .OrderByDescending(value => value.AbandonmentTimestampTicks)
                .ThenByDescending(value => value.LastActivityTimestampTicks)
                .ThenByDescending(value => value.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (cart is null)
        {
            return new RecoveryTrackingResult(
                null,
                null,
                CartRecoveryStatus.Expired,
                null,
                false);
        }

        if (cart.CustomerId != orderCompleted.CustomerId)
        {
            throw new CartRecoveryAuthorizationException();
        }

        if (cart.RecoveryStatus == CartRecoveryStatus.Converted)
        {
            return new RecoveryTrackingResult(
                cart.Id,
                cart.CustomerId,
                CartRecoveryStatus.Converted,
                cart.RecoveryTimestamp,
                false);
        }

        if (cart.RecoveryStatus == CartRecoveryStatus.Expired)
        {
            return new RecoveryTrackingResult(
                cart.Id,
                cart.CustomerId,
                CartRecoveryStatus.Expired,
                cart.RecoveryTimestamp,
                false);
        }

        if (cart.AbandonmentTimestamp is null)
        {
            throw new InvalidOperationException(
                "Only abandoned carts can be matched to completed orders.");
        }

        if (orderCompleted.CompletedAt < cart.AbandonmentTimestamp.Value)
        {
            return new RecoveryTrackingResult(
                cart.Id,
                cart.CustomerId,
                cart.RecoveryStatus,
                cart.RecoveryTimestamp,
                false);
        }

        await ExpireCartIfNeededAsync(cart, timeProvider.GetUtcNow(), recoveryWindow);

        if (cart.RecoveryStatus == CartRecoveryStatus.Expired)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new RecoveryTrackingResult(
                cart.Id,
                cart.CustomerId,
                CartRecoveryStatus.Expired,
                cart.RecoveryTimestamp,
                true);
        }

        if (orderCompleted.CompletedAt
            > cart.AbandonmentTimestamp.Value.Add(recoveryWindow))
        {
            return new RecoveryTrackingResult(
                cart.Id,
                cart.CustomerId,
                cart.RecoveryStatus,
                cart.RecoveryTimestamp,
                false);
        }

        CartRecoveryStatus fromStatus = cart.RecoveryStatus;
        cart.MarkConverted(
            orderCompleted.CustomerId,
            orderCompleted.CompletedAt,
            recoveryWindow);

        dbContext.RecoveryTransitions.Add(
            CartRecoveryTransition.Create(
                cart.Id,
                fromStatus,
                CartRecoveryStatus.Converted,
                orderCompleted.CompletedAt,
                "OrderConverted",
                orderCompleted.CorrelationId));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Recovery conversion conflicted for cart {CartId}.",
                cart.Id);
            throw new CartRecoveryConcurrencyException(exception);
        }

        return new RecoveryTrackingResult(
            cart.Id,
            cart.CustomerId,
            CartRecoveryStatus.Converted,
            cart.RecoveryTimestamp,
            true);
    }

    public async Task<int> ExpireRecoveriesAsync(
        CancellationToken cancellationToken = default)
    {
        RecoveryTrackingOptions settings = options.Value;
        settings.Validate();
        TimeSpan recoveryWindow =
            TimeSpan.FromHours(settings.RecoveryWindowHours);
        DateTimeOffset now = timeProvider.GetUtcNow();
        long nowTicks = now.UtcTicks;

        List<AbandonedCart> openCarts = await dbContext.Carts
            .Where(cart =>
                (EF.Property<int>(
                    cart,
                    nameof(AbandonedCart.RecoveryStatus))
                    == (int)CartRecoveryStatus.Pending
                    || EF.Property<int>(
                        cart,
                        nameof(AbandonedCart.RecoveryStatus))
                        == (int)CartRecoveryStatus.Notified)
                && cart.AbandonmentTimestampTicks != null)
            .ToListAsync(cancellationToken);

        int expiredCount = 0;
        foreach (AbandonedCart cart in openCarts)
        {
            if (cart.AbandonmentTimestampTicks is not long abandonmentTicks)
            {
                continue;
            }

            long expiryTicks =
                abandonmentTicks + recoveryWindow.Ticks;
            if (nowTicks < expiryTicks)
            {
                continue;
            }

            CartRecoveryStatus fromStatus = cart.RecoveryStatus;
            cart.MarkExpired(now);
            dbContext.RecoveryTransitions.Add(
                CartRecoveryTransition.Create(
                    cart.Id,
                    fromStatus,
                    CartRecoveryStatus.Expired,
                    now,
                    "RecoveryWindowExpired"));
            expiredCount++;
        }

        if (expiredCount > 0)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                logger.LogWarning(
                    exception,
                    "Recovery expiry batch conflicted.");
                throw new CartRecoveryConcurrencyException(exception);
            }
        }

        return expiredCount;
    }

    private async Task ExpireCartIfNeededAsync(
        AbandonedCart cart,
        DateTimeOffset now,
        TimeSpan recoveryWindow)
    {
        if (cart.AbandonmentTimestamp is not DateTimeOffset abandonment)
        {
            return;
        }

        if (now >= abandonment.Add(recoveryWindow)
            && cart.RecoveryStatus is CartRecoveryStatus.Pending
                or CartRecoveryStatus.Notified)
        {
            CartRecoveryStatus fromStatus = cart.RecoveryStatus;
            cart.MarkExpired(now);
            dbContext.RecoveryTransitions.Add(
                CartRecoveryTransition.Create(
                    cart.Id,
                    fromStatus,
                    CartRecoveryStatus.Expired,
                    now,
                    "RecoveryWindowExpired"));
        }

        await Task.CompletedTask;
    }
}

public sealed class CartRecoveryAuthorizationException()
    : InvalidOperationException(
        "The completed order customer does not own the abandoned cart.");

public sealed class CartRecoveryConcurrencyException(
    Exception? innerException = null)
    : InvalidOperationException(
        "The cart recovery state changed concurrently.",
        innerException);
