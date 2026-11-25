using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailEdge.Cart;
using Xunit;

namespace RetailEdge.Tests;

public sealed class CartApiTests
{
    [Fact]
    public async Task NonAdminScanReturnsMachineReadableForbidden()
    {
        await using ApiTestContext context = await ApiTestContext.CreateAsync();
        context.Controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim("role", "customer")],
                        "Test"))
            }
        };

        IActionResult response =
            await context.Controller.ScanAbandonedCarts();

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var body = Assert.IsType<CartApiErrorResponse>(objectResult.Value);
        Assert.Equal("ADMIN_ROLE_REQUIRED", body.Error.Code);
    }

    [Fact]
    public async Task CustomerACannotAccessCustomerBCartRecovery()
    {
        await using ApiTestContext context = await ApiTestContext.CreateAsync();
        Guid customerAId = Guid.NewGuid();
        Guid customerBId = Guid.NewGuid();
        AbandonedCart customerBCart = AbandonedCart.Create(
            customerBId,
            Guid.NewGuid(),
            [(Guid.NewGuid(), 1, 25m)],
            "USD",
            context.Now.AddHours(-1));
        customerBCart.MarkAbandoned(context.Now);
        context.DbContext.Carts.Add(customerBCart);
        await context.DbContext.SaveChangesAsync();

        context.Controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [
                            new Claim("customer_id", customerAId.ToString()),
                            new Claim("role", "customer")
                        ],
                        "Test"))
            }
        };

        IActionResult response =
            await context.Controller.GetRecovery(
                customerBCart.Id,
                CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var body = Assert.IsType<CartApiErrorResponse>(objectResult.Value);
        Assert.Equal("CART_ACCESS_DENIED", body.Error.Code);
    }

    [Fact]
    public async Task AnalyticsUsesCursorPagination()
    {
        await using ApiTestContext context = await ApiTestContext.CreateAsync();
        context.Controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim("role", "admin")],
                        "Test"))
            }
        };

        DateTimeOffset now = context.Now;
        for (int index = 0; index < 3; index++)
        {
            AbandonedCart cart = AbandonedCart.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                [(Guid.NewGuid(), 1, 10m)],
                "USD",
                now.AddHours(-index - 1));
            cart.MarkAbandoned(now.AddHours(-index - 1));
            context.DbContext.Carts.Add(cart);
        }
        await context.DbContext.SaveChangesAsync();

        IActionResult firstPage =
            await context.Controller.GetRecoveryAnalytics(
                now.AddDays(-1),
                now,
                null,
                1);
        var firstResponse = Assert.IsType<OkObjectResult>(firstPage);
        string? nextCursor = firstResponse.Value!.GetType()
            .GetProperty("nextCursor")!
            .GetValue(firstResponse.Value) as string;

        Assert.False(string.IsNullOrWhiteSpace(nextCursor));

        IActionResult secondPage =
            await context.Controller.GetRecoveryAnalytics(
                now.AddDays(-1),
                now,
                nextCursor,
                1);

        Assert.IsType<OkObjectResult>(secondPage);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class FakeAbandonmentDetection
        : IAbandonmentDetectionService
    {
        public Task<AbandonmentScanResult> ScanAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new AbandonmentScanResult(0, 0, 0, 0, null));
        }
    }

    private sealed class FakeRecoveryTracking
        : IRecoveryTrackingService
    {
        public Task<RecoveryTrackingResult> HandleOrderCompletedAsync(
            OrderCompletedEvent orderCompleted,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new RecoveryTrackingResult(
                    null,
                    null,
                    CartRecoveryStatus.Expired,
                    null,
                    false));
        }

        public Task<int> ExpireRecoveriesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class ApiTestContext : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private ApiTestContext(
            SqliteConnection connection,
            CartDbContext dbContext,
            CartController controller,
            DateTimeOffset now)
        {
            this.connection = connection;
            DbContext = dbContext;
            Controller = controller;
            Now = now;
        }

        public CartDbContext DbContext { get; }

        public CartController Controller { get; }

        public DateTimeOffset Now { get; }

        public static async Task<ApiTestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<CartDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new CartDbContext(options);
            await dbContext.Database.MigrateAsync();
            DateTimeOffset now = new(
                2026,
                11,
                23,
                12,
                0,
                0,
                TimeSpan.Zero);

            var controller = new CartController(
                dbContext,
                new FakeAbandonmentDetection(),
                new FakeRecoveryTracking(),
                new FixedTimeProvider(now));

            return new ApiTestContext(
                connection,
                dbContext,
                controller,
                now);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
