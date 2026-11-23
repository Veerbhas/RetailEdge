namespace RetailEdge.Inventory;

public sealed class InventoryAlreadyExistsException : Exception
{
    public InventoryAlreadyExistsException(Exception? innerException = null)
        : base("Inventory already exists for this store and product.", innerException)
    {
    }
}
