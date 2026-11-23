using Microsoft.EntityFrameworkCore;

namespace RetailEdge.Inventory;

public sealed class InventoryRepository(InventoryDbContext dbContext) : IInventoryRepository
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
        string productId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Inventories
            .AsNoTracking()
            .SingleOrDefaultAsync(
            inventory => inventory.ProductId == productId,
            cancellationToken);
    }

    public async Task UpdateAsync(
        Inventory inventory,
        CancellationToken cancellationToken = default)
    {
        dbContext.Inventories.Update(inventory);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.Entry(inventory).State = EntityState.Detached;
    }

    public async Task<bool> DeleteByProductAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        Inventory? inventory = await GetByProductAsync(productId, cancellationToken);
        if (inventory is null)
        {
            return false;
        }

        dbContext.Inventories.Remove(inventory);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.Entry(inventory).State = EntityState.Detached;
        return true;
    }
}
