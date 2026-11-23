using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace RetailEdge.Inventory.Migrations;

[DbContext(typeof(InventoryDbContext))]
public sealed class InventoryDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "8.0.34")
            .HasAnnotation("Relational:MaxIdentifierLength", 64);

        modelBuilder.Entity<Inventory>(entity =>
        {
            entity.Property(inventory => inventory.Id)
                .HasColumnType("TEXT");
            entity.Property(inventory => inventory.StoreId)
                .HasColumnType("TEXT");
            entity.Property(inventory => inventory.ProductId)
                .IsRequired()
                .HasMaxLength(Inventory.MaximumProductIdLength)
                .HasColumnType("TEXT");
            entity.Property(inventory => inventory.StockQuantity)
                .HasColumnType("INTEGER");
            entity.Property(inventory => inventory.Version)
                .IsConcurrencyToken()
                .HasColumnType("INTEGER");

            entity.HasKey(inventory => inventory.Id);
            entity.HasIndex(inventory => new { inventory.StoreId, inventory.ProductId })
                .IsUnique()
                .HasDatabaseName("UX_inventory_store_product");
            entity.ToTable("inventory", table =>
                table.HasCheckConstraint(
                    "CK_inventory_stock_nonnegative",
                    "StockQuantity >= 0"));
        });
    }
}
