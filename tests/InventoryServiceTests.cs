using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailEdge.Inventory;
using InventoryRecord = RetailEdge.Inventory.Inventory;
using Xunit;

namespace RetailEdge.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task CreateUpdateGetAndDelete_UsesDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new InventoryDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var service = new InventoryService(new InventoryRepository(dbContext));

        InventoryRecord created = await service.CreateAsync(" product-123 ", 10);
        InventoryRecord? fetched = await service.GetByProductAsync("product-123");
        InventoryRecord updated = await service.UpdateStockAsync("product-123", 4);
        InventoryRecord? fetchedAfterUpdate = await service.GetByProductAsync("product-123");
        bool deleted = await service.DeleteAsync("product-123");
        InventoryRecord? fetchedAfterDelete = await service.GetByProductAsync("product-123");

        Assert.Equal("product-123", created.ProductId);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(10, fetched?.StockQuantity);
        Assert.Equal(4, updated.StockQuantity);
        Assert.Equal(4, fetchedAfterUpdate?.StockQuantity);
        Assert.True(deleted);
        Assert.Null(fetchedAfterDelete);
        Assert.False(await service.DeleteAsync("product-123"));
    }

    [Fact]
    public async Task CreateAsync_RejectsNegativeStock()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new InventoryDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var service = new InventoryService(new InventoryRepository(dbContext));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.CreateAsync("product-123", -1));
    }
}
