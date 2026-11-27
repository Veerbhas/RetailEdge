# ARCHITECTURE.md

RetailEdge is organized as a layered, microservices-oriented platform: HTTP/worker requests enter thin controllers or handlers, application services apply domain rules, repositories use EF Core, and SQLite-backed contexts persist changes before responses or events return.

Inventory Sync remains an independent store-and-product stock service; Cart Abandonment reads cart/activity data and may reference product IDs, but it does not own inventory mutation or payment processing.

Cart activity is represented by the abandoned-cart model and persisted in `CartDbContext` with item, transition, and scan-cursor records through explicit repository/service boundaries.

The abandonment scan receives an authenticated trigger or scheduled request, queries `Pending` carts by the inactivity cutoff, and continues keyset pagination ordered by UTC ticks and cart ID.

Configurable batches limit memory and database pressure; each batch commits cart status, voucher assignment, transition logs, notification/outbox work, and cursor progress independently.

The notification contract separates vendor delivery from recovery logic, so email or push providers receive cart summaries and optional vouchers while durable state and correlation IDs preserve idempotency.

An order-completion event returns through `RecoveryTrackingService`, which verifies ownership, converts a matching cart within 24 hours, or expires open recoveries and persists transition records.

Inventory, cart, and notification services communicate through stable interfaces or events rather than one distributed transaction, accepting eventual consistency across web, mobile, and POS channels.

This architecture suits RetailEdge because channels can update shared cart state independently while timestamps, cursors, optimistic concurrency, and idempotent transitions reconcile retries and delayed events.

Keyset pagination avoids unstable offset scans, while configurable batch limits provide bounded memory and back-pressure during large abandonment scans.

The design trades database/event-transport simplicity for operational resilience: SQLite companion ticks and provider interfaces improve compatibility and vendor independence but add timestamp synchronization and integration-maintenance costs.
