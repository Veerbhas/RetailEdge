namespace RetailEdge.Inventory;

public sealed class InventoryService(IInventoryRepository repository)
{
    public async Task<Inventory> CreateAsync(
        Guid storeId,
        string productId,
        int stockQuantity,
        CancellationToken cancellationToken = default)
    {
        Inventory inventory = Inventory.Create(storeId, productId, stockQuantity);
        await repository.AddAsync(inventory, cancellationToken);
        return inventory;
    }

    public Task<Inventory?> GetByProductAsync(
        Guid storeId,
        string productId,
        CancellationToken cancellationToken = default)
    {
        Inventory.ValidateStoreId(storeId);
        string normalizedProductId = Inventory.NormalizeProductId(productId);
        return repository.GetByProductAsync(storeId, normalizedProductId, cancellationToken);
    }

    public async Task<Inventory> UpdateStockAsync(
        Guid storeId,
        string productId,
        int stockQuantity,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        Inventory.ValidateStoreId(storeId);
        string normalizedProductId = Inventory.NormalizeProductId(productId);
        Inventory inventory = await repository.GetByProductAsync(
            storeId,
            normalizedProductId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                "Inventory was not found.");

        inventory.UpdateStock(stockQuantity, expectedVersion);
        await repository.UpdateAsync(inventory, expectedVersion, cancellationToken);
        return inventory;
    }

    public Task<bool> DeleteAsync(
        Guid storeId,
        string productId,
        CancellationToken cancellationToken = default)
    {
        Inventory.ValidateStoreId(storeId);
        string normalizedProductId = Inventory.NormalizeProductId(productId);
        return repository.DeleteByProductAsync(
            storeId,
            normalizedProductId,
            cancellationToken);
    }
}