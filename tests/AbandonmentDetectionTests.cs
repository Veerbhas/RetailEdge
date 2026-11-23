using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailEdge.Cart;
using Xunit;

namespace RetailEdge.Tests;

public sealed class AbandonmentDetectionTests
{
    [Fact]
    public async Task CartInactiveForThirtyMinutesIsAbandoned()
    {
        await using TestContext context = await TestContext.CreateAsync();
        DateTimeOffset now = context.Now;
        AbandonedCart cart = AddCart(
            context,
            now.AddMinutes(-30),
            50m);

        AbandonmentScanResult result =
            await context.ScanAsync();

        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(1, result.AbandonedCount);
        Assert.Equal(CartCheckoutStatus.Abandoned, cart.CheckoutStatus);
        Assert.Equal(now, cart.AbandonmentTimestamp);
    }

    [Fact]
    public async Task CartInactiveForLessThanThirtyMinutesIsNotAbandoned()
    {
        await using TestContext context = await TestContext.CreateAsync();
        DateTimeOffset now = context.Now;
        AbandonedCart cart = AddCart(
            context,
            now.AddMinutes(-29),
            50m);

        AbandonmentScanResult result =
            await context.ScanAsync();

        Assert.Equal(0, result.ScannedCount);
        Assert.Null(cart.AbandonmentTimestamp);
        Assert.Equal(CartCheckoutStatus.Pending, cart.CheckoutStatus);
    }

    [Fact]
    public async Task VoucherIsGeneratedAboveDecimalThreshold()
    {
        await using TestContext context =
            await TestContext.CreateAsync(voucherThreshold: 25.50m);
        AddCart(context, context.Now.AddHours(-1), 25.51m);

        AbandonmentScanResult result =
            await context.ScanAsync();

        Assert.Equal(1, result.VoucherCount);
        Assert.Equal(1, result.NotificationCount);
        AbandonedCart cart = await context.DbContext.Carts.SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(cart.VoucherCode));
    }

    [Fact]
    public async Task VoucherIsNotGeneratedAtOrBelowDecimalThreshold()
    {
        await using TestContext context =
            await TestContext.CreateAsync(voucherThreshold: 25.50m);
        AddCart(context, context.Now.AddHours(-1), 25.50m);

        AbandonmentScanResult result =
            await context.ScanAsync();

        Assert.Equal(0, result.VoucherCount);
        Assert.Null((await context.DbContext.Carts.SingleAsync()).VoucherCode);
    }

    [Fact]
    public async Task BatchPersistsRestartableKeysetCursor()
    {
        await using TestContext context =
            await TestContext.CreateAsync(batchSize: 2);
        DateTimeOffset activity = context.Now.AddHours(-2);
        for (int index = 0; index < 5; index++)
        {
            AddCart(context, activity.AddMinutes(index), 10m);
        }

        AbandonmentScanResult first =
            await context.ScanAsync();
        AbandonmentScanResult second =
            await context.ScanAsync();

        Assert.Equal(5, first.ScannedCount);
        Assert.Equal(5, first.AbandonedCount);
        Assert.Equal(0, second.ScannedCount);
        CartScanCursor cursor = await context.DbContext.ScanCursors
            .SingleAsync(value =>
                value.ScanKey == AbandonmentDetectionService.ScanCursorKey);
        Assert.Null(cursor.LastActivityTimestamp);
        Assert.Null(cursor.LastCartId);
    }

    [Fact]
    public async Task CustomerMultipleCartsOnlyLatestGetsNotification()
    {
        await using TestContext context =
            await TestContext.CreateAsync();
        Guid customerId = Guid.NewGuid();

        AbandonedCart older = AddCart(
            context,
            context.Now.AddHours(-2),
            20m,
            customerId);
        AbandonedCart newer = AddCart(
            context,
            context.Now.AddHours(-1),
            20m,
            customerId);

        AbandonmentScanResult result =
            await context.ScanAsync();

        Assert.Equal(2, result.AbandonedCount);
        Assert.Equal(1, result.NotificationCount);
        Assert.Single(context.NotificationPublisher.Notifications);
        Assert.Contains(
            context.NotificationPublisher.Notifications,
            notification => notification.CartId == newer.Id);
        Assert.DoesNotContain(
            context.NotificationPublisher.Notifications,
            notification => notification.CartId == older.Id);
    }

    private static AbandonedCart AddCart(
        TestContext context,
        DateTimeOffset activity,
        decimal total,
        Guid? customerId = null)
    {
        Guid unitCustomerId = customerId ?? Guid.NewGuid();
        AbandonedCart cart = AbandonedCart.Create(
            unitCustomerId,
            Guid.NewGuid(),
            [(Guid.NewGuid(), 1, total)],
            "USD",
            activity);
        context.DbContext.Carts.Add(cart);
        context.DbContext.SaveChanges();
        return cart;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class TestVoucherGenerator : IVoucherGenerator
    {
        public string GenerateOneTimeVoucher(
            Guid cartId,
            decimal cartTotal,
            string currency)
        {
            return $"V-{cartId:N}";
        }
    }

    private sealed class TestNotificationPublisher
        : ICartNotificationPublisher
    {
        public List<CartRecoveryNotification> Notifications { get; } = [];

        public Task PublishAsync(
            CartRecoveryNotification notification,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestContext(
            SqliteConnection connection,
            CartDbContext dbContext,
            AbandonmentDetectionService service,
            TestNotificationPublisher notificationPublisher,
            DateTimeOffset now)
        {
            this.connection = connection;
            DbContext = dbContext;
            Service = service;
            NotificationPublisher = notificationPublisher;
            Now = now;
        }

        public CartDbContext DbContext { get; }

        public AbandonmentDetectionService Service { get; }

        public TestNotificationPublisher NotificationPublisher { get; }

        public DateTimeOffset Now { get; }

        public static async Task<TestContext> CreateAsync(
            decimal voucherThreshold = 100m,
            int batchSize = 100)
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
            var publisher = new TestNotificationPublisher();
            var service = new AbandonmentDetectionService(
                dbContext,
                new TestVoucherGenerator(),
                publisher,
                new FixedTimeProvider(now),
                Options.Create(new AbandonmentDetectionOptions
                {
                    InactiveMinutes = 30,
                    VoucherThreshold = voucherThreshold,
                    BatchSize = batchSize
                }),
                NullLogger<AbandonmentDetectionService>.Instance);

            return new TestContext(
                connection,
                dbContext,
                service,
                publisher,
                now);
        }

        public Task<AbandonmentScanResult> ScanAsync()
        {
            return Service.ScanAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
