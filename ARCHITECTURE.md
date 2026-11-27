# ARCHITECTURE.md

RetailEdge uses a layered, service-oriented architecture: cart and inventory domain models feed application services, repositories use EF Core, and thin API controllers expose owner- or admin-authorized operations.

Inventory Sync remains an independent store-and-product stock service; Cart Abandonment reads cart and activity data and may reference product IDs for recommendations, but it does not own stock mutation or payment processing.

Cart activity enters the abandoned-cart model, which is persisted in `CartDbContext` with item, transition, and scan-cursor records through repository/service boundaries.

The abandonment detector queries `Pending` carts by the configured inactivity cutoff, using keyset pagination ordered by UTC ticks and cart ID for deterministic restartable batches.

Each batch commits cart state, voucher assignment, transition logs, notification/outbox work, and cursor progress independently so later failures do not roll back earlier committed batches.

The notification contract separates vendor delivery from recovery logic; email or push providers consume cart summaries and optional vouchers, while durable state and correlation IDs preserve idempotency.

When an order-completion event arrives, `RecoveryTrackingService` verifies customer ownership, rejects mismatches with a Broken Object Level Authorization error, converts within 24 hours, or expires open recoveries.

The inventory, cart, and notification services communicate through stable interfaces or events rather than sharing database transactions, accepting eventual consistency across channels and retries.

Web, mobile, and POS activity can update the shared cart session independently; the system therefore uses timestamps, cursors, optimistic concurrency, and idempotent transitions instead of assuming synchronous state.

Cursor-based analytics and scan queries avoid offset scans and preserve predictable database access; configurable batch limits provide back-pressure while keeping request memory bounded.

Decimal money fields are stored exactly, while UTC-tick fields support SQLite-compatible filtering; these trade exactness and scan portability for duplicate timestamp storage and migration maintenance.

Key trade-offs are SQLite compatibility, a separate outbox/provider boundary rather than a vendor lock-in dependency, and pseudonymous recommendation data rather than direct PII sharing.
