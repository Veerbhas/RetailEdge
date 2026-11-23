namespace RetailEdge.Inventory;

public sealed class Inventory
{
    public const int MaximumProductIdLength = 100;

    private Inventory()
    {
        ProductId = string.Empty;
    }

    private Inventory(Guid storeId, string productId, int stockQuantity)
    {
        StoreId = storeId;
        Id = Guid.NewGuid();
        ProductId = productId;
        StockQuantity = stockQuantity;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string ProductId { get; private set; }

    public int StockQuantity { get; private set; }

    public long Version { get; private set; }

    public static Inventory Create(Guid storeId, string productId, int stockQuantity)
    {
        ValidateStoreId(storeId);
        string normalizedProductId = NormalizeProductId(productId);
        ValidateStockQuantity(stockQuantity);

        return new Inventory(storeId, normalizedProductId, stockQuantity);
    }

    public void UpdateStock(int stockQuantity, long expectedVersion)
    {
        ValidateStockQuantity(stockQuantity);
        if (expectedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        if (expectedVersion != Version)
        {
            throw new InventoryConcurrencyException();
        }

        StockQuantity = stockQuantity;
        Version = checked(Version + 1);
    }

    internal static void ValidateStoreId(Guid storeId)
    {
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("Store ID is required.", nameof(storeId));
        }
    }

    internal static string NormalizeProductId(string productId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        string normalizedProductId = productId.Trim();
        if (normalizedProductId.Length > MaximumProductIdLength)
        {
            throw new ArgumentException(
                $"Product ID cannot exceed {MaximumProductIdLength} characters.",
                nameof(productId));
        }

        return normalizedProductId;
    }

    private static void ValidateStockQuantity(int stockQuantity)
    {
        if (stockQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stockQuantity),
                stockQuantity,
                "Stock quantity cannot be negative.");
        }
    }
}