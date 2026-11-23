using Microsoft.EntityFrameworkCore;

namespace RetailEdge.Cart;

public sealed class CartDbContext(DbContextOptions<CartDbContext> options)
    : DbContext(options)
{
    public DbSet<AbandonedCart> Carts => Set<AbandonedCart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<CartRecoveryTransition> RecoveryTransitions =>
        Set<CartRecoveryTransition>();

    public DbSet<CartScanCursor> ScanCursors => Set<CartScanCursor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AbandonedCart>(entity =>
        {
            entity.ToTable("cart");
            entity.HasKey(cart => cart.Id);
            entity.Property(cart => cart.CustomerId).IsRequired();
            entity.Property(cart => cart.SessionId).IsRequired();
            entity.Property(cart => cart.CartTotal)
                .IsRequired()
                .HasPrecision(18, 2)
                .HasColumnType("TEXT");
            entity.Property(cart => cart.Currency)
                .IsRequired()
                .HasMaxLength(3);
            entity.Property(cart => cart.VoucherCode)
                .HasMaxLength(100);
            entity.Property(cart => cart.LastActivityTimestamp).IsRequired();
            entity.Property(cart => cart.LastActivityTimestampTicks).IsRequired();
            entity.Property(cart => cart.Version)
                .IsConcurrencyToken()
                .IsRequired();

            entity.HasIndex(cart => new
            {
                cart.CheckoutStatus,
                cart.LastActivityTimestamp,
                cart.Id
            }).HasDatabaseName("IX_cart_abandonment_scan");

            entity.HasIndex(cart => new
            {
                cart.CheckoutStatus,
                cart.LastActivityTimestampTicks,
                cart.Id
            }).HasDatabaseName("IX_cart_abandonment_scan_ticks");

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

            entity.HasMany(cart => cart.Items)
                .WithOne()
                .HasForeignKey(item => item.CartId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("cart_item");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ProductId).IsRequired();
            entity.Property(item => item.Quantity).IsRequired();
            entity.Property(item => item.UnitPrice)
                .IsRequired()
                .HasPrecision(18, 2)
                .HasColumnType("TEXT");
            entity.ToTable("cart_item", table =>
                table.HasCheckConstraint(
                    "CK_cart_item_quantity_positive",
                    "Quantity > 0"));
            entity.ToTable("cart_item", table =>
                table.HasCheckConstraint(
                    "CK_cart_item_unit_price_nonnegative",
                    "UnitPrice >= 0"));
        });

        modelBuilder.Entity<CartRecoveryTransition>(entity =>
        {
            entity.ToTable("cart_recovery_transition");
            entity.HasKey(transition => transition.Id);
            entity.Property(transition => transition.Reason)
                .IsRequired()
                .HasMaxLength(100);
            entity.HasIndex(transition => transition.CartId)
                .HasDatabaseName("IX_cart_recovery_transition_cart");
            entity.HasIndex(transition => transition.Timestamp)
                .HasDatabaseName("IX_cart_recovery_transition_timestamp");
        });

        modelBuilder.Entity<CartScanCursor>(entity =>
        {
            entity.ToTable("cart_scan_cursor");
            entity.HasKey(cursor => cursor.ScanKey);
            entity.Property(cursor => cursor.ScanKey)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(cursor => cursor.UpdatedTimestamp).IsRequired();
        });
    }
}
