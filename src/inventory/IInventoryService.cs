namespace RetailEdge.Inventory;

public interface IInventoryService
{
    Task<InventoryResponse> CreateAsync(
        Guid storeId,
        CreateInventoryRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryResponse?> GetByProductAsync(
        Guid storeId,
        string productId,
        CancellationToken cancellationToken = default);

    Task<InventoryResponse> UpdateStockAsync(
        Guid storeId,
        string productId,
        UpdateInventoryStockRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid storeId,
        string productId,
        CancellationToken cancellationToken = default);
}
