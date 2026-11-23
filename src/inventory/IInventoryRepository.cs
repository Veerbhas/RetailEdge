namespace RetailEdge.Inventory;

public interface IInventoryRepository
{
    Task AddAsync(Inventory inventory, CancellationToken cancellationToken = default);

    Task<Inventory?> GetByProductAsync(
        string productId,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(Inventory inventory, CancellationToken cancellationToken = default);

    Task<bool> DeleteByProductAsync(
        string productId,
        CancellationToken cancellationToken = default);
}
