using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;

namespace RetailEdge.Inventory;

public sealed class InventoryRepository(
    InventoryDbContext dbContext,
    ILogger<InventoryRepository> logger) : IInventoryRepository
{
    public async Task AddAsync(
        Inventory inventory,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Inventories.AddAsync(inventory, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.Entry(inventory).State = EntityState.Detached;
    }

    public Task<Inventory?> GetByProductAsync(
        Guid storeId,
        string productId,
        CancellationToken cancellationToken = default)
    {
        Inventory.ValidateStoreId(storeId);
        string normalizedProductId = Inventory.NormalizeProductId(productId);

        return dbContext.Inventories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                inventory =>
                    inventory.StoreId == storeId
                    && inventory.ProductId == normalizedProductId,
                cancellationToken);
    }

    public async Task UpdateAsync(
        Inventory inventory,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (expectedVersion <= 0 || inventory.Version != expectedVersion + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        EntityEntry<Inventory> entry = dbContext.Attach(inventory);
        entry.Property(value => value.StockQuantity).IsModified = true;
        entry.Property(value => value.Version).OriginalValue = expectedVersion;
        entry.Property(value => value.Version).IsModified = true;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            entry.State = EntityState.Detached;
            logger.LogWarning(
                exception,
                "Inventory stock update conflicted for store {StoreId} and product {ProductId}.",
                inventory.StoreId,
                inventory.ProductId);
            throw new InventoryConcurrencyException(exception);
        }

        entry.State = EntityState.Detached;
    }

    public async Task<bool> DeleteByProductAsync(
        Guid storeId,
        string productId,
        CancellationToken cancellationToken = default)
    {
        Inventory.ValidateStoreId(storeId);
        string normalizedProductId = Inventory.NormalizeProductId(productId);
        Inventory? inventory = await dbContext.Inventories.SingleOrDefaultAsync(
            value => value.StoreId == storeId && value.ProductId == normalizedProductId,
            cancellationToken);
        if (inventory is null)
        {
            return false;
        }

        dbContext.Inventories.Remove(inventory);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            dbContext.Entry(inventory).State = EntityState.Detached;
            logger.LogWarning(
                exception,
                "Inventory delete conflicted for store {StoreId} and product {ProductId}.",
                inventory.StoreId,
                inventory.ProductId);
            throw new InventoryConcurrencyException(exception);
        }

        dbContext.Entry(inventory).State = EntityState.Detached;
        return true;
    }
}
