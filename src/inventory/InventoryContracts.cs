namespace RetailEdge.Inventory;

/// <summary>Allowlisted fields for creating a store-scoped inventory record.</summary>
public sealed record CreateInventoryRequest(string ProductId, int StockQuantity);

/// <summary>Allowlisted fields for replacing a stock count using an expected version.</summary>
public sealed record UpdateInventoryStockRequest(int StockQuantity, long ExpectedVersion);

/// <summary>Inventory data returned by the application service.</summary>
public sealed record InventoryResponse(
    Guid Id,
    Guid StoreId,
    string ProductId,
    int StockQuantity,
    long Version);
