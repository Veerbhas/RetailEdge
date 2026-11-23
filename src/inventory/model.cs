namespace RetailEdge.Inventory;

public sealed class Inventory
{
    public const int MaximumProductIdLength = 100;

    private Inventory()
    {
        ProductId = string.Empty;
    }

    private Inventory(string productId, int stockQuantity)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        StockQuantity = stockQuantity;
    }

    public Guid Id { get; private set; }

    public string ProductId { get; private set; }

    public int StockQuantity { get; private set; }

    public static Inventory Create(string productId, int stockQuantity)
    {
        string normalizedProductId = NormalizeProductId(productId);
        ValidateStockQuantity(stockQuantity);

        return new Inventory(normalizedProductId, stockQuantity);
    }

    public void UpdateStock(int stockQuantity)
    {
        ValidateStockQuantity(stockQuantity);
        StockQuantity = stockQuantity;
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