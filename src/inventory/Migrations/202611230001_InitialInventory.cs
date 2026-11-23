using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RetailEdge.Inventory.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("202611230001_InitialInventory")]
public sealed class InitialInventory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductId = table.Column<string>(
                    type: "TEXT",
                    maxLength: Inventory.MaximumProductIdLength,
                    nullable: false),
                StockQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inventory", inventory => inventory.Id);
                table.CheckConstraint(
                    "CK_inventory_stock_nonnegative",
                    "StockQuantity >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "UX_inventory_store_product",
            table: "inventory",
            columns: new[] { "StoreId", "ProductId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "inventory");
    }
}
