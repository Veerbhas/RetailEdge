using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RetailEdge.Cart.Migrations;

[DbContext(typeof(CartDbContext))]
[Migration("202611230002_InitialCart")]
public sealed class InitialCart : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "cart",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                CartTotal = table.Column<decimal>(
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                Currency = table.Column<string>(
                    type: "TEXT",
                    maxLength: 3,
                    nullable: false),
                CheckoutStatus = table.Column<int>(type: "INTEGER", nullable: false),
                AbandonmentTimestamp = table.Column<DateTimeOffset>(
                    type: "TEXT",
                    nullable: true),
                AbandonmentTimestampTicks = table.Column<long>(
                    type: "INTEGER",
                    nullable: true),
                RecoveryStatus = table.Column<int>(type: "INTEGER", nullable: false),
                VoucherCode = table.Column<string>(
                    type: "TEXT",
                    maxLength: 100,
                    nullable: true),
                RecoveryTimestamp = table.Column<DateTimeOffset>(
                    type: "TEXT",
                    nullable: true),
                LastActivityTimestamp = table.Column<DateTimeOffset>(
                    type: "TEXT",
                    nullable: false),
                LastActivityTimestampTicks = table.Column<long>(
                    type: "INTEGER",
                    nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cart", cart => cart.Id);
            });

        migrationBuilder.CreateTable(
            name: "cart_item",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CartId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                UnitPrice = table.Column<decimal>(
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cart_item", table => table.Id);
                table.ForeignKey(
                    name: "FK_cart_item_cart_CartId",
                    column: x => x.CartId,
                    principalTable: "cart",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint(
                    "CK_cart_item_quantity_positive",
                    "Quantity > 0");
                table.CheckConstraint(
                    "CK_cart_item_unit_price_nonnegative",
                    "UnitPrice >= 0");
            });

        migrationBuilder.CreateTable(
            name: "cart_recovery_transition",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CartId = table.Column<Guid>(type: "TEXT", nullable: false),
                FromStatus = table.Column<int>(type: "INTEGER", nullable: false),
                ToStatus = table.Column<int>(type: "INTEGER", nullable: false),
                Timestamp = table.Column<DateTimeOffset>(
                    type: "TEXT",
                    nullable: false),
                Reason = table.Column<string>(
                    type: "TEXT",
                    maxLength: 100,
                    nullable: false),
                CorrelationId = table.Column<string>(
                    type: "TEXT",
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cart_recovery_transition", table => table.Id);
            });

        migrationBuilder.CreateTable(
            name: "cart_scan_cursor",
            columns: table => new
            {
                ScanKey = table.Column<string>(
                    type: "TEXT",
                    maxLength: 100,
                    nullable: false),
                LastActivityTimestamp = table.Column<DateTimeOffset>(
                    type: "TEXT",
                    nullable: true),
                LastCartId = table.Column<Guid>(type: "TEXT", nullable: true),
                UpdatedTimestamp = table.Column<DateTimeOffset>(
                    type: "TEXT",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cart_scan_cursor", table => table.ScanKey);
            });

        migrationBuilder.CreateIndex(
            name: "IX_cart_abandonment_scan",
            table: "cart",
            columns: new[] { "CheckoutStatus", "LastActivityTimestamp", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_cart_abandonment_scan_ticks",
            table: "cart",
            columns: new[] { "CheckoutStatus", "LastActivityTimestampTicks", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_cart_owner_recovery",
            table: "cart",
            columns: new[] { "CustomerId", "RecoveryStatus", "AbandonmentTimestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_cart_recovery_expiry",
            table: "cart",
            columns: new[] { "RecoveryStatus", "AbandonmentTimestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_cart_item_CartId",
            table: "cart_item",
            column: "CartId");

        migrationBuilder.CreateIndex(
            name: "IX_cart_recovery_transition_cart",
            table: "cart_recovery_transition",
            column: "CartId");

        migrationBuilder.CreateIndex(
            name: "IX_cart_recovery_transition_timestamp",
            table: "cart_recovery_transition",
            column: "Timestamp");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "cart_recovery_transition");
        migrationBuilder.DropTable(name: "cart_item");
        migrationBuilder.DropTable(name: "cart_scan_cursor");
        migrationBuilder.DropTable(name: "cart");
    }
}
