using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailEdge.Cart;
using Xunit;

namespace RetailEdge.Tests;

public sealed class RecoveryTrackingTests
{
    [Fact]
    public async Task ConvertsWithinTwentyFourHours()
    {
        await using TestContext context = await TestContext.CreateAsync();
        (AbandonedCart cart, Guid customerId) =
            await context.AddAbandonedCartAsync(
                context.Now.AddHours(-2));

        RecoveryTrackingResult result =
            await context.Service.HandleOrderCompletedAsync(
                new OrderCompletedEvent(
                    Guid.NewGuid(),
                    customerId,
                    cart.AbandonmentTimestamp!.Value.AddHours(24),
                    cart.Id,
                    "conversion-test"));

        Assert.True(result.Changed);
        Assert.Equal(CartRecoveryStatus.Converted, result.Status);
        Assert.Equal(
            cart.AbandonmentTimestamp!.Value.AddHours(24),
            cart.RecoveryTimestamp);
    }

    [Fact]
    public async Task OwnerMismatchThrowsAuthorizationException()
    {
        await using TestContext context = await TestContext.CreateAsync();
        (AbandonedCart cart, _) =
            await context.AddAbandonedCartAsync(
                context.Now.AddHours(-1));

        await Assert.ThrowsAsync<CartRecoveryAuthorizationException>(
            () => context.Service.HandleOrderCompletedAsync(
                new OrderCompletedEvent(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    context.Now,
                    cart.Id,
                    "bola-test")));

        Assert.Equal(
            CartRecoveryStatus.Pending,
            cart.RecoveryStatus);
    }

    [Fact]
    public async Task OrderAfterWindowExpiresRecovery()
    {
        await using TestContext context = await TestContext.CreateAsync();
        (AbandonedCart cart, Guid customerId) =
            await context.AddAbandonedCartAsync(
                context.Now.AddHours(-25));

        RecoveryTrackingResult result =
            await context.Service.HandleOrderCompletedAsync(
                new OrderCompletedEvent(
                    Guid.NewGuid(),
                    customerId,
                    context.Now,
                    cart.Id,
                    "expired-test"));

        Assert.True(result.Changed);
        Assert.Equal(CartRecoveryStatus.Expired, result.Status);
        Assert.Equal(context.Now, cart.RecoveryTimestamp);
    }

    [Fact]
    public async Task ExpireRecoveriesMarksOpenCartsAtBoundary()
    {
        await using TestContext context = await TestContext.CreateAsync();
        (AbandonedCart cart, _) =
            await context.AddAbandonedCartAsync(
                context.Now.AddHours(-24));

        int expiredCount =
            await context.Service.ExpireRecoveriesAsync();

        Assert.Equal(1, expiredCount);
        Assert.Equal(CartRecoveryStatus.Expired, cart.RecoveryStatus);
    }

    [Fact]
    public async Task DuplicateConversionDoesNotRepeatTransition()
    {
        await using TestContext context = await TestContext.CreateAsync();
        (AbandonedCart cart, Guid customerId) =
            await context.AddAbandonedCartAsync(
                context.Now.AddHours(-1));
        var order = new OrderCompletedEvent(
            Guid.NewGuid(),
            customerId,
            context.Now,
            cart.Id,
            "duplicate-test");

        RecoveryTrackingResult first =
            await context.Service.HandleOrderCompletedAsync(order);
        RecoveryTrackingResult duplicate =
            await context.Service.HandleOrderCompletedAsync(order);

        Assert.True(first.Changed);
        Assert.False(duplicate.Changed);
        Assert.Equal(1, await context.DbContext.RecoveryTransitions
            .CountAsync(transition =>
                transition.CartId == cart.Id
                && transition.ToStatus == CartRecoveryStatus.Converted));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestContext(
            SqliteConnection connection,
            CartDbContext dbContext,
            RecoveryTrackingService service,
            DateTimeOffset now)
        {
            this.connection = connection;
            DbContext = dbContext;
            Service = service;
            Now = now;
        }

        public CartDbContext DbContext { get; }

        public RecoveryTrackingService Service { get; }

        public DateTimeOffset Now { get; }

        public static async Task<TestContext> CreateAsync()
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
            var service = new RecoveryTrackingService(
                dbContext,
                new FixedTimeProvider(now),
                Options.Create(new RecoveryTrackingOptions
                {
                    RecoveryWindowHours = 24
                }),
                NullLogger<RecoveryTrackingService>.Instance);

            return new TestContext(
                connection,
                dbContext,
                service,
                now);
        }

        public async Task<(AbandonedCart Cart, Guid CustomerId)>
            AddAbandonedCartAsync(DateTimeOffset abandonmentTimestamp)
        {
            Guid customerId = Guid.NewGuid();
            AbandonedCart cart = AbandonedCart.Create(
                customerId,
                Guid.NewGuid(),
                [(Guid.NewGuid(), 1, 50m)],
                "USD",
                abandonmentTimestamp.AddHours(-1));
            cart.MarkAbandoned(abandonmentTimestamp);
            DbContext.Carts.Add(cart);
            await DbContext.SaveChangesAsync();
            return (cart, customerId);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
