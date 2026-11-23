namespace RetailEdge.Inventory;

public interface IInventoryRepository
{
    Task AddAsync(Inventory inventory, CancellationToken cancellationToken = default);

    Task<Inventory?> GetByProductAsync(
        Guid storeId,
        string productId,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Inventory inventory,
        long expectedVersion,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteByProductAsync(
        Guid storeId,
        string productId,
        CancellationToken cancellationToken = default);
}
