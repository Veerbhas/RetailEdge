using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RetailEdge.Cart;

public interface IAbandonmentDetectionService
{
    Task<AbandonmentScanResult> ScanAsync(
        CancellationToken cancellationToken = default);
}

public sealed class AbandonmentDetectionService(
    CartDbContext dbContext,
    IVoucherGenerator voucherGenerator,
    ICartNotificationPublisher notificationPublisher,
    TimeProvider timeProvider,
    IOptions<AbandonmentDetectionOptions> options,
    ILogger<AbandonmentDetectionService> logger)
    : IAbandonmentDetectionService
{
    public const string ScanCursorKey = "AbandonedCartScan";

    public async Task<AbandonmentScanResult> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        AbandonmentDetectionOptions settings = options.Value;
        settings.Validate();
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset inactivityCutoff =
            now.AddMinutes(-settings.InactiveMinutes);
        long inactivityCutoffTicks = inactivityCutoff.UtcTicks;

        CartScanCursor cursor = await dbContext.ScanCursors
            .SingleOrDefaultAsync(
                value => value.ScanKey == ScanCursorKey,
                cancellationToken)
            ?? CartScanCursor.Create(
                ScanCursorKey,
                null,
                null,
                now);

        bool cursorIsNew = dbContext.Entry(cursor).State == EntityState.Detached
            && cursor.ScanKey == ScanCursorKey
            && await dbContext.ScanCursors
                .AsNoTracking()
                .CountAsync(
                    value => value.ScanKey == ScanCursorKey,
                    cancellationToken) == 0;
        if (cursorIsNew)
        {
            dbContext.ScanCursors.Add(cursor);
        }

        int scannedCount = 0;
        int abandonedCount = 0;
        int notificationCount = 0;
        int voucherCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            int pendingStatus = (int)CartCheckoutStatus.Pending;
            IQueryable<AbandonedCart> query = dbContext.Carts
                .AsTracking()
                .Include(cart => cart.Items)
                .Where(cart =>
                    EF.Property<int>(cart, nameof(AbandonedCart.CheckoutStatus))
                        == pendingStatus
                    && cart.LastActivityTimestampTicks <= inactivityCutoffTicks);

            if (cursor.LastActivityTimestamp is DateTimeOffset cursorActivity
                && cursor.LastCartId is Guid cursorCartId)
            {
                query = query.Where(cart =>
                    cart.LastActivityTimestampTicks > cursorActivity.UtcTicks
                    || (cart.LastActivityTimestampTicks == cursorActivity.UtcTicks
                        && cart.Id.CompareTo(cursorCartId) > 0));
            }

            List<AbandonedCart> batch = await query
                .OrderBy(cart => cart.LastActivityTimestampTicks)
                .ThenBy(cart => cart.Id)
                .Take(settings.BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                cursor.Complete(timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
                break;
            }

            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                foreach (AbandonedCart cart in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (cart.CheckoutStatus != CartCheckoutStatus.Pending
                        || cart.LastActivityTimestampTicks > inactivityCutoffTicks)
                    {
                        continue;
                    }

                    CartRecoveryStatus fromStatus = cart.RecoveryStatus;
                    cart.MarkAbandoned(now);
                    abandonedCount++;

                    if (cart.CartTotal > settings.VoucherThreshold)
                    {
                        if (cart.VoucherCode is null)
                        {
                            string voucherCode = voucherGenerator.GenerateOneTimeVoucher(
                                cart.Id,
                                cart.CartTotal,
                                cart.Currency);
                            cart.AttachVoucher(voucherCode);
                            voucherCount++;
                        }
                    }

                    dbContext.RecoveryTransitions.Add(
                        CartRecoveryTransition.Create(
                            cart.Id,
                            fromStatus,
                            cart.RecoveryStatus,
                            now,
                            "AbandonmentDetected"));
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                foreach (AbandonedCart cart in batch.Where(value =>
                             value.AbandonmentTimestamp is not null))
                {
                    bool isLatest = await IsLatestAbandonedCartAsync(
                        cart,
                        now,
                        cancellationToken);
                    if (!isLatest)
                    {
                        continue;
                    }

                    string correlationId = $"{cart.Id:N}";
                    var notification = new CartRecoveryNotification(
                        cart.Id,
                        cart.CustomerId,
                        cart.CartTotal,
                        cart.Currency,
                        cart.Items
                            .Select(item => new CartItemNotification(
                                item.ProductId,
                                item.Quantity,
                                item.UnitPrice))
                            .ToArray(),
                        cart.VoucherCode,
                        correlationId);

                    await notificationPublisher.PublishAsync(
                        notification,
                        cancellationToken);

                    cart.MarkNotified();
                    dbContext.RecoveryTransitions.Add(
                        CartRecoveryTransition.Create(
                            cart.Id,
                            CartRecoveryStatus.Pending,
                            CartRecoveryStatus.Notified,
                            now,
                            "NotificationAccepted",
                            correlationId));
                    notificationCount++;
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                AbandonedCart lastCart = batch[^1];
                cursor.Advance(
                    lastCart.LastActivityTimestamp,
                    lastCart.Id,
                    timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                scannedCount += batch.Count;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                logger.LogWarning(
                    exception,
                    "Cart abandonment scan batch conflicted; cursor will not advance.");
                throw new CartScanConcurrencyException(exception);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return new AbandonmentScanResult(
            scannedCount,
            abandonedCount,
            notificationCount,
            voucherCount,
            cursor);
    }

    private async Task<bool> IsLatestAbandonedCartAsync(
        AbandonedCart cart,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AbandonedCart? latest = await dbContext.Carts
            .AsNoTracking()
            .Where(value =>
                value.CustomerId == cart.CustomerId
                && value.AbandonmentTimestamp != null)
            .OrderByDescending(value => value.AbandonmentTimestampTicks)
            .ThenByDescending(value => value.LastActivityTimestampTicks)
            .ThenByDescending(value => value.Id)
            .ThenByDescending(value => value.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return latest is not null
            && latest.Id == cart.Id
            && latest.AbandonmentTimestamp <= now;
    }
}

public sealed class CartScanConcurrencyException(
    Exception? innerException = null)
    : InvalidOperationException(
        "The cart abandonment scan could not update a current cart state.",
        innerException);
