using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RetailEdge.Cart.Migrations;

[DbContext(typeof(CartDbContext))]
public sealed class CartDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "8.0.34")
            .HasAnnotation("Relational:MaxIdentifierLength", 64);

        modelBuilder.Entity<AbandonedCart>(entity =>
        {
            entity.Property(cart => cart.Id).HasColumnType("TEXT");
            entity.Property(cart => cart.CustomerId).HasColumnType("TEXT");
            entity.Property(cart => cart.SessionId).HasColumnType("TEXT");
            entity.Property(cart => cart.CartTotal)
                .HasPrecision(18, 2)
                .HasColumnType("TEXT");
            entity.Property(cart => cart.Currency)
                .HasMaxLength(3)
                .HasColumnType("TEXT");
            entity.Property(cart => cart.VoucherCode)
                .HasMaxLength(100)
                .HasColumnType("TEXT");
            entity.Property(cart => cart.Version)
                .IsConcurrencyToken()
                .HasColumnType("INTEGER");
            entity.HasKey(cart => cart.Id);
            entity.HasIndex(cart => new
            {
                cart.CheckoutStatus,
                cart.LastActivityTimestamp,
                cart.Id
            }).HasDatabaseName("IX_cart_abandonment_scan");
            entity.HasIndex(cart => new
            {
                cart.CustomerId,
                cart.RecoveryStatus,
                cart.AbandonmentTimestamp
            }).HasDatabaseName("IX_cart_owner_recovery");
            entity.HasIndex(cart => new
            {
                cart.RecoveryStatus,
                cart.AbandonmentTimestamp
            }).HasDatabaseName("IX_cart_recovery_expiry");
            entity.ToTable("cart");
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.Property(item => item.Id).HasColumnType("TEXT");
            entity.Property(item => item.CartId).HasColumnType("TEXT");
            entity.Property(item => item.ProductId).HasColumnType("TEXT");
            entity.Property(item => item.Quantity).HasColumnType("INTEGER");
            entity.Property(item => item.UnitPrice)
                .HasPrecision(18, 2)
                .HasColumnType("TEXT");
            entity.HasKey(item => item.Id);
            entity.ToTable("cart_item");
        });

        modelBuilder.Entity<CartRecoveryTransition>(entity =>
        {
            entity.Property(transition => transition.Id).HasColumnType("TEXT");
            entity.Property(transition => transition.CartId).HasColumnType("TEXT");
            entity.Property(transition => transition.Timestamp).HasColumnType("TEXT");
            entity.Property(transition => transition.Reason)
                .HasMaxLength(100)
                .HasColumnType("TEXT");
            entity.HasKey(transition => transition.Id);
            entity.ToTable("cart_recovery_transition");
        });

        modelBuilder.Entity<CartScanCursor>(entity =>
        {
            entity.Property(cursor => cursor.ScanKey)
                .HasMaxLength(100)
                .HasColumnType("TEXT");
            entity.HasKey(cursor => cursor.ScanKey);
            entity.ToTable("cart_scan_cursor");
        });
    }
}
