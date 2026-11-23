using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace RetailEdge.Cart;

[ApiController]
[Route("")]
public sealed class CartController(
    CartDbContext dbContext,
    IAbandonmentDetectionService abandonmentDetection,
    IRecoveryTrackingService recoveryTracking,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("scan/abandoned-carts")]
    [Authorize]
    public async Task<IActionResult> ScanAbandonedCarts(
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated())
        {
            return Unauthorized(ApiError(
                "UNAUTHENTICATED",
                "Authentication is required."));
        }
        if (!IsAdmin(User))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiError(
                    "ADMIN_ROLE_REQUIRED",
                    "Administrator role is required."));
        }

        AbandonmentScanResult result =
            await abandonmentDetection.ScanAsync(cancellationToken);

        string? cursor = result.Cursor is null
            ? null
            : EncodeScanCursor(result.Cursor);

        return Accepted(new
        {
            result.ScannedCount,
            result.AbandonedCount,
            result.NotificationCount,
            result.VoucherCount,
            cursor
        });
    }

    [HttpGet("carts/{id:guid}/recovery")]
    [Authorize]
    public async Task<IActionResult> GetRecovery(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!IsAuthenticated())
        {
            return Unauthorized(ApiError(
                "UNAUTHENTICATED",
                "Authentication is required."));
        }

        string? customerClaim = User.FindFirst("customer_id")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(customerClaim, out Guid requestingCustomerId)
            || requestingCustomerId == Guid.Empty)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiError(
                    "CART_ACCESS_DENIED",
                    "Cart access was denied."));
        }

        AbandonedCart? cart = await dbContext.Carts
            .SingleOrDefaultAsync(
                value => value.Id == id,
                cancellationToken);
        if (cart is null
            || cart.CustomerId != requestingCustomerId)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiError(
                    "CART_ACCESS_DENIED",
                    "Cart access was denied."));
        }

        await recoveryTracking.ExpireRecoveriesAsync(cancellationToken);
        if (cart.RecoveryStatus is CartRecoveryStatus.Pending
            or CartRecoveryStatus.Notified)
        {
            await dbContext.Entry(cart).ReloadAsync(cancellationToken);
        }

        return Ok(new CartRecoveryApiResponse(
            cart.Id,
            cart.RecoveryStatus,
            cart.AbandonmentTimestamp,
            cart.RecoveryTimestamp,
            cart.VoucherCode,
            cart.Version));
    }

    [HttpGet("analytics/recovery")]
    [Authorize]
    public async Task<IActionResult> GetRecoveryAnalytics(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated())
        {
            return Unauthorized(ApiError(
                "UNAUTHENTICATED",
                "Authentication is required."));
        }
        if (!IsAdmin(User))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiError(
                    "ADMIN_ROLE_REQUIRED",
                    "Administrator role is required."));
        }

        limit = Math.Clamp(limit, 1, 100);
        DateTimeOffset rangeFrom = from ?? timeProvider.GetUtcNow().AddDays(-30);
        DateTimeOffset rangeTo = to ?? timeProvider.GetUtcNow();
        if (rangeTo < rangeFrom)
        {
            return BadRequest(ApiError(
                "INVALID_DATE_RANGE",
                "The end of the date range must not precede the start."));
        }

        long rangeFromTicks = rangeFrom.UtcTicks;
        long rangeToTicks = rangeTo.UtcTicks;
        long? cursorTimestampTicks = null;
        Guid? cursorCartId = null;
        if (!string.IsNullOrWhiteSpace(cursor)
            && !TryDecodeAnalyticsCursor(
                cursor,
                out cursorTimestampTicks,
                out cursorCartId))
        {
            return BadRequest(ApiError(
                "INVALID_CURSOR",
                "The supplied pagination cursor is invalid."));
        }

        IQueryable<AbandonedCart> query = dbContext.Carts
            .AsNoTracking()
            .Where(cart =>
                cart.AbandonmentTimestampTicks != null
                && cart.AbandonmentTimestampTicks >= rangeFromTicks
                && cart.AbandonmentTimestampTicks <= rangeToTicks);

        if (cursorTimestampTicks is long timestampTicks
            && cursorCartId is Guid cartId)
        {
            query = query.Where(cart =>
                cart.AbandonmentTimestampTicks < timestampTicks
                || (cart.AbandonmentTimestampTicks == timestampTicks
                    && cart.Id.CompareTo(cartId) < 0));
        }

        List<AbandonedCart> carts = await query
            .OrderByDescending(cart => cart.AbandonmentTimestampTicks)
            .ThenByDescending(cart => cart.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        bool hasMore = carts.Count > limit;
        if (hasMore)
        {
            carts.RemoveAt(carts.Count - 1);
        }

        string? nextCursor = hasMore
            ? EncodeAnalyticsCursor(
                carts[^1].AbandonmentTimestampTicks!.Value,
                carts[^1].Id)
            : null;

        var items = carts
            .Select(cart => new CartAnalyticsItem(
                cart.Id,
                cart.CustomerId,
                cart.RecoveryStatus,
                cart.AbandonmentTimestamp,
                cart.RecoveryTimestamp))
            .ToArray();

        return Ok(new
        {
            from = rangeFrom,
            to = rangeTo,
            items,
            nextCursor,
            hasMore
        });
    }

    private bool IsAuthenticated()
    {
        return User.Identity?.IsAuthenticated == true;
    }

    private static bool IsAdmin(ClaimsPrincipal principal)
    {
        return principal.IsInRole("admin")
            || principal.FindAll("role")
                .Any(claim =>
                    string.Equals(
                        claim.Value,
                        "admin",
                        StringComparison.OrdinalIgnoreCase));
    }

    private static CartApiErrorResponse ApiError(
        string code,
        string message)
    {
        return new CartApiErrorResponse(
            new CartApiError(code, message));
    }

    private static string EncodeScanCursor(CartScanCursor cursor)
    {
        if (cursor.LastActivityTimestamp is null
            || cursor.LastCartId is null)
        {
            return string.Empty;
        }

        return Convert.ToHexString(
            Encoding.UTF8.GetBytes(
                $"{cursor.LastActivityTimestamp.Value.UtcTicks:x}|{cursor.LastCartId.Value:N}"));
    }

    private static string EncodeAnalyticsCursor(
        long timestampTicks,
        Guid cartId)
    {
        return Convert.ToHexString(
            Encoding.UTF8.GetBytes(
                $"{timestampTicks:x}|{cartId:N}"));
    }

    private static bool TryDecodeAnalyticsCursor(
        string cursor,
        out long? timestampTicks,
        out Guid? cartId)
    {
        timestampTicks = null;
        cartId = null;
        try
        {
            byte[] bytes = Convert.FromHexString(cursor);
            string value = Encoding.UTF8.GetString(bytes);
            string[] parts = value.Split('|', StringSplitOptions.None);
            if (parts.Length != 2
                || !long.TryParse(
                    parts[0],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out long ticks)
                || ticks < 0
                || !Guid.TryParseExact(parts[1], "N", out Guid parsedCartId)
                || parsedCartId == Guid.Empty)
            {
                return false;
            }

            timestampTicks = ticks;
            cartId = parsedCartId;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
