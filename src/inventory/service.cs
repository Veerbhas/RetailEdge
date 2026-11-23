namespace RetailEdge.Inventory;

public sealed class InventoryService(IInventoryRepository repository) : IInventoryService
{
    public async Task<InventoryResponse> CreateAsync(
        Guid storeId,
        CreateInventoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Inventory.ValidateStoreId(storeId);
        string normalizedProductId = Inventory.NormalizeProductId(request.ProductId);
        Inventory? existing = await repository.GetByProductAsync(
            storeId,
            normalizedProductId,
            cancellationToken);
        if (existing is not null)
        {
            throw new InventoryAlreadyExistsException();
        }

        Inventory inventory = Inventory.Create(
            storeId,
            normalizedProductId,
            request.StockQuantity);
        await repository.AddAsync(inventory, cancellationToken);
        return ToResponse(inventory);
    }

    public async Task<InventoryResponse?> GetByProductAsync(
        Guid storeId,
        string productId,
        CancellationToken cancellationToken = default)
    {
        Inventory.ValidateStoreId(storeId);
        string normalizedProductId = Inventory.NormalizeProductId(productId);
        Inventory? inventory = await repository.GetByProductAsync(
            storeId,
            normalizedProductId,
            cancellationToken);
        return inventory is null ? null : ToResponse(inventory);
    }

    public async Task<InventoryResponse> UpdateStockAsync(
        Guid storeId,
        string productId,
        UpdateInventoryStockRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Inventory.ValidateStoreId(storeId);
        string normalizedProductId = Inventory.NormalizeProductId(productId);
        Inventory inventory = await repository.GetByProductAsync(
            storeId,
            normalizedProductId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                "Inventory was not found.");

        inventory.UpdateStock(request.StockQuantity, request.ExpectedVersion);
        await repository.UpdateAsync(
            inventory,
            request.ExpectedVersion,
            cancellationToken);
        return ToResponse(inventory);
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

    private static InventoryResponse ToResponse(Inventory inventory)
    {
        return new InventoryResponse(
            inventory.Id,
            inventory.StoreId,
            inventory.ProductId,
            inventory.StockQuantity,
            inventory.Version);
    }
}