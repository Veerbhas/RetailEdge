using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace RetailEdge.Cart;

public static class CartServiceCollectionExtensions
{
    public static IServiceCollection AddCart(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CartDbContext>(
            options => options.UseSqlite(connectionString));
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<AbandonmentDetectionOptions>()
            .BindConfiguration(AbandonmentDetectionOptions.SectionName)
            .ValidateOnStart();
        services.AddOptions<RecoveryTrackingOptions>()
            .BindConfiguration(RecoveryTrackingOptions.SectionName)
            .ValidateOnStart();
        services.AddScoped<IAbandonmentDetectionService, AbandonmentDetectionService>();
        services.AddScoped<IRecoveryTrackingService, RecoveryTrackingService>();
        services.TryAddScoped<IVoucherGenerator, DefaultVoucherGenerator>();
        services.TryAddScoped<ICartNotificationPublisher, LoggingCartNotificationPublisher>();

        return services;
    }

    public static async Task MigrateCartDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        await using AsyncServiceScope scope =
            serviceProvider.CreateAsyncScope();
        CartDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<CartDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}

public sealed class DefaultVoucherGenerator : IVoucherGenerator
{
    public string GenerateOneTimeVoucher(
        Guid cartId,
        decimal cartTotal,
        string currency)
    {
        return $"RE-{cartId.ToString("N")[..12].ToUpperInvariant()}";
    }
}

public sealed class LoggingCartNotificationPublisher(
    ILogger<LoggingCartNotificationPublisher> logger)
    : ICartNotificationPublisher
{
    public Task PublishAsync(
        CartRecoveryNotification notification,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Cart recovery notification accepted for cart {CartId}.",
            notification.CartId);
        return Task.CompletedTask;
    }
}
