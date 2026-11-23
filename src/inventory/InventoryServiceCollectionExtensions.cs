using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace RetailEdge.Inventory;

public static class InventoryServiceCollectionExtensions
{
    public static IServiceCollection AddInventory(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<InventoryDbContext>(
            options => options.UseSqlite(connectionString));
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<InventoryService>();

        return services;
    }

    public static async Task MigrateInventoryDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
        InventoryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
