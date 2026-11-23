namespace RetailEdge.Inventory;

public sealed class InventoryService(IInventoryRepository repository)
{
    public async Task<Inventory> CreateAsync(
        string productId,
        int stockQuantity,
        CancellationToken cancellationToken = default)
    {
        Inventory inventory = Inventory.Create(productId, stockQuantity);
        await repository.AddAsync(inventory, cancellationToken);
        return inventory;
    }

    public Task<Inventory?> GetByProductAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        string normalizedProductId = Inventory.NormalizeProductId(productId);
        return repository.GetByProductAsync(normalizedProductId, cancellationToken);
    }

    public async Task<Inventory> UpdateStockAsync(
        string productId,
        int stockQuantity,
        CancellationToken cancellationToken = default)
    {
        string normalizedProductId = Inventory.NormalizeProductId(productId);
        Inventory inventory = await repository.GetByProductAsync(
            normalizedProductId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Inventory for product '{normalizedProductId}' was not found.");

        inventory.UpdateStock(stockQuantity);
        await repository.UpdateAsync(inventory, cancellationToken);
        return inventory;
    }

    public Task<bool> DeleteAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        string normalizedProductId = Inventory.NormalizeProductId(productId);
        return repository.DeleteByProductAsync(normalizedProductId, cancellationToken);
    }
}