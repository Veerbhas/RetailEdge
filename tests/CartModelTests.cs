using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using RetailEdge.Cart;
using Xunit;

namespace RetailEdge.Tests;

public sealed class CartModelTests
{
    [Fact]
    public async Task CartModelUsesDecimalTotalAndRequiredIndexes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CartDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new CartDbContext(options);
        await dbContext.Database.MigrateAsync();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        AbandonedCart cart = AbandonedCart.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [(Guid.NewGuid(), 2, 12.50m)],
            "USD",
            now);

        await dbContext.Carts.AddAsync(cart);
        await dbContext.SaveChangesAsync();
        cart.MarkAbandoned(now.AddMinutes(30));
        await dbContext.SaveChangesAsync();

        AbandonedCart persisted = await dbContext.Carts
            .Include(value => value.Items)
            .SingleAsync();
        var monetaryProperties = typeof(AbandonedCart)
            .GetProperties()
            .Concat(typeof(CartItem).GetProperties())
            .Where(property =>
                property.Name.Contains("Price", StringComparison.Ordinal)
                || property.Name.Contains("Total", StringComparison.Ordinal))
            .Select(property => property.PropertyType);

        Assert.All(monetaryProperties, property =>
            Assert.Equal(typeof(decimal), property));
        Assert.Equal(25m, persisted.CartTotal);
        Assert.Equal(CartCheckoutStatus.Abandoned, persisted.CheckoutStatus);
        Assert.Equal(2, persisted.Version);
        Assert.Single(persisted.Items);

        var indexes = dbContext.Model.FindEntityType(typeof(AbandonedCart))!
            .GetIndexes()
            .Select(index => index.GetDatabaseName())
            .ToHashSet();
        Assert.Contains("IX_cart_abandonment_scan", indexes);
        Assert.Contains("IX_cart_owner_recovery", indexes);
        Assert.Contains("IX_cart_recovery_expiry", indexes);
    }
}
