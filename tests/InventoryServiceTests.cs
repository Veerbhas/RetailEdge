using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RetailEdge.Inventory;
using InventoryRecord = RetailEdge.Inventory.Inventory;
using InventoryResponse = RetailEdge.Inventory.InventoryResponse;
using Xunit;

namespace RetailEdge.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task OperationsAreScopedByStoreAndUseOptimisticConcurrency()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new InventoryDbContext(options);
        await dbContext.Database.MigrateAsync();
        var repository = new InventoryRepository(
            dbContext,
            NullLogger<InventoryRepository>.Instance);
        var service = new InventoryService(repository);
        Guid firstStoreId = Guid.NewGuid();
        Guid secondStoreId = Guid.NewGuid();

        InventoryResponse createdAtFirstStore =
            await service.CreateAsync(
                firstStoreId,
                new CreateInventoryRequest(" product-123 ", 10));
        InventoryResponse createdAtSecondStore =
            await service.CreateAsync(
                secondStoreId,
                new CreateInventoryRequest("product-123", 20));
        InventoryResponse? fetchedAtFirstStore =
            await service.GetByProductAsync(firstStoreId, "product-123");
        InventoryResponse updated = await service.UpdateStockAsync(
            firstStoreId,
            "product-123",
            new UpdateInventoryStockRequest(4, createdAtFirstStore.Version));
        InventoryResponse? fetchedAfterUpdate =
            await service.GetByProductAsync(firstStoreId, "product-123");

        await Assert.ThrowsAsync<InventoryConcurrencyException>(
            () => service.UpdateStockAsync(
                firstStoreId,
                "product-123",
                new UpdateInventoryStockRequest(7, createdAtFirstStore.Version)));

        InventoryResponse? secondStoreInventory =
            await service.GetByProductAsync(secondStoreId, "product-123");
        bool deleted = await service.DeleteAsync(firstStoreId, "product-123");
        InventoryResponse? fetchedAfterDelete =
            await service.GetByProductAsync(firstStoreId, "product-123");

        Assert.Equal("product-123", createdAtFirstStore.ProductId);
        Assert.NotEqual(Guid.Empty, createdAtFirstStore.Id);
        Assert.Equal(firstStoreId, fetchedAtFirstStore?.StoreId);
        Assert.Equal(10, fetchedAtFirstStore?.StockQuantity);
        Assert.Equal(4, updated.StockQuantity);
        Assert.Equal(2, updated.Version);
        Assert.Equal(4, fetchedAfterUpdate?.StockQuantity);
        Assert.Equal(20, secondStoreInventory?.StockQuantity);
        Assert.Equal(1, createdAtSecondStore.Version);
        Assert.True(deleted);
        Assert.Null(fetchedAfterDelete);
        Assert.False(await service.DeleteAsync(firstStoreId, "product-123"));
        Assert.NotNull(await service.GetByProductAsync(secondStoreId, "product-123"));
    }

    [Fact]
    public async Task CreateAsync_RejectsInvalidStoreAndNegativeStock()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new InventoryDbContext(options);
        await dbContext.Database.MigrateAsync();
        var service = new InventoryService(new InventoryRepository(
            dbContext,
            NullLogger<InventoryRepository>.Instance));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.CreateAsync(
                Guid.NewGuid(),
                new CreateInventoryRequest("product-123", -1)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(
                Guid.Empty,
                new CreateInventoryRequest("product-123", 1)));
    }

    [Fact]
    public async Task CreateAsync_ReportsDuplicateStoreAndProduct()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new InventoryDbContext(options);
        await dbContext.Database.MigrateAsync();
        var service = new InventoryService(new InventoryRepository(
            dbContext,
            NullLogger<InventoryRepository>.Instance));
        Guid storeId = Guid.NewGuid();

        await service.CreateAsync(
            storeId,
            new CreateInventoryRequest("product-123", 5));

        await Assert.ThrowsAsync<InventoryAlreadyExistsException>(
            () => service.CreateAsync(
                storeId,
                new CreateInventoryRequest("product-123", 8)));
    }

    [Fact]
    public async Task ConcurrentRepositories_RejectStaleWriteAtDatabaseBoundary()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var firstDbContext = new InventoryDbContext(options);
        await firstDbContext.Database.MigrateAsync();
        Guid storeId = Guid.NewGuid();
        var firstRepository = new InventoryRepository(
            firstDbContext,
            NullLogger<InventoryRepository>.Instance);
        var service = new InventoryService(firstRepository);
        await service.CreateAsync(
            storeId,
            new CreateInventoryRequest("product-456", 12));

        await using var secondDbContext = new InventoryDbContext(options);
        var secondRepository = new InventoryRepository(
            secondDbContext,
            NullLogger<InventoryRepository>.Instance);
        InventoryRecord firstSnapshot =
            (await firstRepository.GetByProductAsync(storeId, "product-456"))!;
        InventoryRecord secondSnapshot =
            (await secondRepository.GetByProductAsync(storeId, "product-456"))!;

        firstSnapshot.UpdateStock(10, firstSnapshot.Version);
        secondSnapshot.UpdateStock(8, secondSnapshot.Version);
        await firstRepository.UpdateAsync(firstSnapshot, expectedVersion: 1);

        await Assert.ThrowsAsync<InventoryConcurrencyException>(
            () => secondRepository.UpdateAsync(secondSnapshot, expectedVersion: 1));

        InventoryRecord? persisted =
            await firstRepository.GetByProductAsync(storeId, "product-456");
        Assert.Equal(10, persisted?.StockQuantity);
        Assert.Equal(2, persisted?.Version);
    }
}
