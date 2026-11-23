using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
