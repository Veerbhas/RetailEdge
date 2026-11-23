using Microsoft.EntityFrameworkCore;

namespace RetailEdge.Inventory;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Inventory> Inventories => Set<Inventory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Inventory>(entity =>
        {
            entity.ToTable("inventory", table =>
                table.HasCheckConstraint(
                    "CK_inventory_stock_nonnegative",
                    "StockQuantity >= 0"));
            entity.HasKey(inventory => inventory.Id);
            entity.Property(inventory => inventory.StoreId)
                .IsRequired();
            entity.Property(inventory => inventory.ProductId)
                .IsRequired()
                .HasMaxLength(Inventory.MaximumProductIdLength);
            entity.Property(inventory => inventory.StockQuantity)
                .IsRequired();
            entity.Property(inventory => inventory.Version)
                .IsConcurrencyToken()
                .IsRequired();
            entity.HasIndex(inventory => new { inventory.StoreId, inventory.ProductId })
                .IsUnique()
                .HasDatabaseName("UX_inventory_store_product");
        });
    }
}
