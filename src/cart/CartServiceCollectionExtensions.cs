using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
