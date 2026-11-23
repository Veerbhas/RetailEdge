namespace RetailEdge.Inventory;

public sealed class InventoryConcurrencyException : Exception
{
    public InventoryConcurrencyException(Exception? innerException = null)
        : base("Inventory changed during the operation. Reload and retry.", innerException)
    {
    }
}
