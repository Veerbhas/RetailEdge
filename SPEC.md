# Cart Abandonment Recovery Feature Specification

## 1. Purpose

Build the Cart Abandonment Recovery system on top of the remediated Inventory
Sync module. Implementation must follow this document and the repository
architecture conventions. The inventory module is a dependency and must not
be modified unless a defect found during integration directly blocks the
feature.

The implementation uses C# 12, .NET 8, ASP.NET Core, Entity Framework Core
SQLite, xUnit, and the existing JWT authentication setup.

## 2. Scope

Implement:

- An abandoned-cart domain model and EF Core persistence.
- Abandonment detection with batch and restart support.
- Voucher generation and notification integration boundaries.
- Order-completion recovery tracking.
- State-transition audit records.
- The three REST endpoints listed in this specification.
- Automated tests for the required scenarios and repository integration.

Out of scope:

- Building a separate payment or inventory reservation service.
- Building a full email or push provider; use injectable provider interfaces.
- Modifying inventory synchronization semantics unless integration is blocked.
- Guessing or exposing customer PII in logs or API responses.

## 3. Architecture

Use the existing pattern:

`domain model -> repository -> application service -> controller`

Production code belongs under `src/cart/`. Cross-cutting authentication remains
under `src/security/`. SQL operations use EF Core and SQLite.

Expected files may include:

- `model.cs`
- `CartContracts.cs`
- `ICartRepository.cs`
- `CartRepository.cs`
- `IAbandonmentDetectionService.cs`
- `AbandonmentDetectionService.cs`
- `IRecoveryTrackingService.cs`
- `RecoveryTrackingService.cs`
- `CartServiceCollectionExtensions.cs`
- `CartDbContext.cs`
- `CartController.cs`
- `Migrations/`

Names may vary only where they remain consistent with existing repository
patterns.

### 3.1 Dependency boundaries

Define interfaces for:

- Cart persistence.
- Voucher generation.
- Notification delivery.
- Time/source of current time.
- State-transition logging.

Domain and application code must not depend on EF, HTTP, or a notification
vendor.

Use dependency injection for all implementations. Notification and voucher
integrations must be observable and retry-safe.

## 4. Time and configuration

Use an injectable clock (`TimeProvider` or equivalent). Production code must not
call `DateTime.UtcNow` directly in feature business logic.

Configuration names:

- `CartAbandonment:InactiveMinutes` (default `30`)
- `CartAbandonment:VoucherThreshold` (decimal; required)
- `CartAbandonment:BatchSize` (default `100`, minimum `1`, maximum `1000`)
- `CartRecovery:RecoveryWindowHours` (default `24`)

Threshold and timing values must be validated at startup. No configured value
may be negative or unusable. The inactive threshold and recovery window must
be configurable but tests must verify the specified 30-minute and 24-hour
defaults.

## 5. Domain model

### 5.1 AbandonedCart

Fields:

- `Id: Guid`
- `CustomerId: Guid`
- `SessionId: Guid`
- `Items: IReadOnlyCollection<CartItem>`
- `CartTotal: decimal`
- `Currency: string`
- `CheckoutStatus: CartCheckoutStatus` (`Pending`, `InProgress`, `Completed`,
  `Cancelled`)
- `LastActivityTimestamp: DateTimeOffset`
- `AbandonmentTimestamp: DateTimeOffset?`
- `RecoveryStatus: CartRecoveryStatus`
- `VoucherCode: string?`
- `RecoveryTimestamp: DateTimeOffset?`
- `Version: int`

The product explicitly requires a version integer for optimistic concurrency.
Use an EF concurrency token and increment the version on every state
transition. Do not replace `int` with `long` in the public domain contract.

`CartItem` fields:

- `ProductId: Guid`
- `Quantity: int`
- `UnitPrice: decimal`

Prices and totals must use `decimal` throughout. Never use `float` or
`double` for currency. EF Core must store decimal values as SQLite `TEXT` or
another exact-decimal representation rather than a binary floating-point
type.

Currency must be a normalized ISO 4217-style uppercase code, maximum 3
characters, and validation must reject unknown/blank values only to the extent
needed by tests. It must not be interpreted as a float.

Cart quantity must be greater than zero. Each unit price must be greater than
or equal to zero. Cart total must equal the sum of `Quantity * UnitPrice`
and must be stored or derived consistently. `CustomerId`, `SessionId`, and
cart `Id` must not be `Guid.Empty`.

### 5.2 Recovery status

Use:

- `Pending`: abandoned and awaiting recovery workflow.
- `Notified`: recovery notification/workflow triggered.
- `Converted`: customer completed purchase within the recovery window.
- `Expired`: 24-hour recovery window elapsed without conversion.

Detection changes a qualifying cart from pending checkout to abandoned and sets
its abandonment timestamp. Recovery status starts as `Pending` and becomes
`Notified` after the notification/outbox workflow succeeds or is durably
accepted. A final status is not overwritten.

### 5.3 Voucher

Generate a voucher only when `CartTotal > VoucherThreshold`. A voucher code
must be:

- Unique.
- One-time/redeemable once.
- Nullable on carts.
- Stored only once in the voucher or outbox implementation.
- Rejecting repeated notification, conversion, or scan retries.

Implement voucher generation through `IVoucherService` and persist voucher
identity in the cart. No payment or voucher payload may be logged.

### 5.4 State-transition log

Persist each transition with:

- `Id: Guid`
- `CartId: Guid`
- `FromStatus: CartRecoveryStatus`
- `ToStatus: CartRecoveryStatus`
- `Timestamp: DateTimeOffset`
- `CorrelationId: string?`
- `Reason: string`

The reason is a short internal code such as `AbandonmentDetected`,
`NotificationAccepted`, `OrderConverted`, or `RecoveryWindowExpired`.
Avoid customer PII and token/payment data in logs.

## 6. Persistence

Add a dedicated `CartDbContext` with `DbSet<AbandonedCart>`,
`DbSet<CartItem>`, `DbSet<CartRecoveryTransition>`, and
`DbSet<CartScanCursor>`.

### 6.1 Required indexes

Declare at least these indexes:

1. `IX_cart_abandonment_scan`
   - Columns: `CheckoutStatus`, `LastActivityTimestamp`, `Id`
   - Used by the abandonment scan query.
2. `IX_cart_owner_recovery`
   - Columns: `CustomerId`, `RecoveryStatus`, `AbandonmentTimestamp`
3. `IX_cart_recovery_expiry`
   - Columns: `RecoveryStatus`, `AbandonmentTimestamp`

The indexes must be present in EF model configuration and in the migration.

### 6.2 Abandonment scan query

The scan must select:

```text
CheckoutStatus = Pending
AND LastActivityTimestamp <= now - 30 minutes
```

The EF Core LINQ query must be composed from
`CheckoutStatus` and `LastActivityTimestamp` and ordered by
`LastActivityTimestamp`, then `Id`, so EF/SQLite can use
`IX_cart_abandonment_scan`. The query must not first load all carts and filter
in memory.

Use keyset cursor pagination with fields `(LastActivityTimestamp, Id)`.
Process at most the configured batch size. Cursor values must not be a page
number.

### 6.3 Batch cursor

Persist one restartable cursor record with a stable key (for example,
`AbandonedCartScan`). It stores:

- `ScanKey: string`
- `LastActivityTimestamp: DateTimeOffset?`
- `LastCartId: Guid?`
- `UpdatedTimestamp: DateTimeOffset`

After a successfully committed batch, persist the last processed key and commit
cursor progress in the same independent batch transaction. A batch failure
must not advance the cursor. On the next run, continue after the persisted
cursor.

At the end of the eligible result set, reset or complete the cursor so a later
notification of a new old cart can be found. Do not reset it merely because
one batch operation failed.

### 6.4 Transactions and concurrency

Each configured batch commits independently. Commit all cart state changes,
voucher assignment, transition logs, and durable notification/outbox work for
that batch in one database transaction. Cursor progress may be committed with
the batch.

Use optimistic concurrency with `Version` and `DbUpdateConcurrencyException`.
Map stale writes to a domain/service concurrency error rather than silently
overwriting a current state. Retry only when the operation is safe and
idempotent.

## 7. Abandonment detection service

`POST /scan/abandoned-carts` triggers `ScanAsync(cancellationToken)`.

For each qualifying cart:

1. Verify the cart is still `CheckoutStatus.Pending`.
2. Mark `CheckoutStatus` abandoned and set `AbandonmentTimestamp`.
3. Set recovery status to `Pending`, unless already final or already notified.
4. Evaluate `CartTotal > VoucherThreshold`.
5. Generate and attach a voucher only for high-value carts.
6. Generate one notification summary and optional voucher code.
7. Log the state transition.
8. Trigger or durably enqueue the notification event.
9. Change `Pending -> Notified` after the notification/outbox operation is
   durably accepted.

Notification failures must be retriable and visible through the outbox or a
service/provider result. They must not silently disappear.

### 7.1 Multiple abandoned carts

When one customer has multiple abandoned carts, only the most recent
abandonment workflow is allowed to trigger. Choose the record with the greatest
`AbandonmentTimestamp`; if timestamps are equal, use the greatest `Id` only as
a deterministic tie-breaker.

The service must mark qualifying carts abandoned as data records, but only the
most recent cart is eligible to enter the notification/recovery workflow.
Older carts remain identifiable, are not notified twice, and may remain
`Pending` or move to `Expired` by policy. They must not become `Converted`
through this feature.

## 8. Voucher and notification contracts

Use allowlisted service contracts rather than domain entities.

Example notification contract:

```text
CartRecoveryNotification(
    CartId,
    CustomerId,
    CartTotal,
    Currency,
    Items,
    VoucherCode?)
```

The notification provider may receive only the minimum required fields.
Customer PII must not be present in domain logs. Email or push selection is an
integration concern.

Notification events must be idempotent. Repeated delivery with the same
`CartId` and recovery attempt must not create duplicate notifications or
multiple vouchers.

## 9. Recovery tracking service

Listen for an order-completion event through an interface/handler boundary.

Minimum event fields:

- `OrderId: Guid`
- `CustomerId: Guid`
- `CompletedAt: DateTimeOffset`
- `CartId: Guid?`
- `CorrelationId: string?`

For a matching event:

1. If `CartId` is supplied, load that cart and verify
   `Cart.CustomerId == event.CustomerId`.
2. If `CartId` is absent, select only the customer's most recent open abandoned
   cart that is `Pending` or `Notified`.
3. Reject a mismatch and do not mark the cart converted.
4. Convert only if `CompletedAt <= AbandonmentTimestamp + 24 hours`.
5. Set recovery status to `Converted`, set `RecoveryTimestamp`, and increment
   the version.
6. Persist an `OrderConverted` transition log.
7. Ignore duplicate order events after conversion.

This is the service-layer Broken Object Level Authorisation check. A caller may
not mark a cart owned by a different customer because of a request or event
containing a mismatched owner.

### 9.1 Expiration

A background/scan/service operation or same-time recovery lookup must expire
open carts when:

`now >= AbandonmentTimestamp + RecoveryWindowHours`.

Set `RecoveryStatus = Expired`, set `RecoveryTimestamp = now`, increment version,
and persist an `RecoveryWindowExpired` transition. A converted cart must never
be expired, and an expired cart must never be converted.

## 10. API and DTO requirements

All write endpoints must use explicit request DTO/record types; never bind
the AbandonedCart entity directly.

### 10.1 POST /scan/abandoned-carts

- Require an authenticated principal with the admin role.
- Trigger the abandonment scan.
- Return `202 Accepted` with a machine-readable batch summary.

Success response shape:

```text
{
  "scannedCount": integer,
  "abandonedCount": integer,
  "notificationCount": integer,
  "voucherCount": integer,
  "cursor": string?
}
```

An unauthenticated caller must receive `401`.
A non-admin authenticated caller must receive `403` with
`error.code = "ADMIN_ROLE_REQUIRED"`.
Do not expose stack traces or internal provider details.

### 10.2 GET /carts/{id}/recovery

- Require authentication.
- Read `CustomerId` from a trusted claim such as `sub` or `customer_id`.
- Load the requested cart.
- If the principal customer ID does not equal `Cart.CustomerId`, return `403`,
  not `200` and not a distinguishable `404`.
- Owners receive only a recovery response DTO containing:
  - `CartId`
  - `RecoveryStatus`
  - `AbandonmentTimestamp`
  - `RecoveryTimestamp`
  - `VoucherCode` (nullable)
  - `Version`

Never return all cart items or unrelated PII from this endpoint.
Cross-owner access must use `error.code = "CART_ACCESS_DENIED"`.

### 10.3 GET /analytics/recovery

- Require admin role.
- Accept date range and cursor parameters.
- Use cursor-based pagination, no offset paging.
- Apply query filters at the database before response mapping.
- Return a summary for the date range plus `NextCursor`.

Example response:

```text
{
  "from": "DateTimeOffset",
  "to": "DateTimeOffset",
  "items": [
    {
      "cartId": "Guid",
      "customerId": "Guid",
      "recoveryStatus": "CartRecoveryStatus",
      "abandonmentTimestamp": "DateTimeOffset?",
      "recoveryTimestamp": "DateTimeOffset?"
    }
  ],
  "nextCursor": "string?"
}
```

The analytics query must not expose sensitive customer PII. Non-admin callers
receive `403` with `error.code = "ADMIN_ROLE_REQUIRED"`.

### 10.4 Common error response

Use:

```text
{
  "error": {
    "code": "MACHINE_READABLE_CODE",
    "message": "Safe user-facing message"
  }
}
```

Safe domain errors may use `CART_NOT_FOUND`, `INVALID_CURSOR`,
`CONCURRENT_MODIFICATION`, or `RECOVERY_CONFLICT`. Do not leak database details.

## 11. Security

- Pin JWT algorithms to exactly RS256 or HS256 using the existing extension.
- Reject unsigned and `alg: none` tokens.
- Validate signature, issuer, audience, `exp`, `nbf`, and numeric `iat`.
- Call `UseAuthentication()` before `UseAuthorization()`.
- Derive customer and role scope only from trusted claims.
- Do not trust `CustomerId` or `CartId` in write DTO fields for ownership.
- Admin role for scan and analytics; owner claim for recovery status.
- Return 403 rather than 200 for cross-owner access.
- Use allowlisted DTO fields on all write endpoints.
- Never log authorization tokens, payment data, voucher redemption secrets,
  or full customer records.

## 12. Logging and observability

Log structured fields only:

- `CartId`
- `CustomerId` (only if policy permits hashed/opaque)
- `CorrelationId`
- `StateTransition`
- `ScanCursor`
- `BatchSize`
- `Outcome`

Do not log item details, unit prices, customer names, addresses, email bodies,
authentication claims, or authorization headers.

State transitions must be persisted and also emit structured transition logs
for analytics.

## 13. Testing requirements

Use SQLite in-memory databases and xUnit. Use a fake/in-memory clock and fake
voucher/notification providers. Tests must isolate and clean database state.

Minimum required test cases:

1. **AbandonedAfterThirtyMinutes**
   - A pending checkout inactive for exactly 30 minutes is flagged abandoned.
   - A cart inactive for less than 30 minutes is not flagged.

2. **VoucherAboveThreshold**
   - A cart total above the configured threshold generates one voucher.

3. **NoVoucherBelowThreshold**
   - A cart total below or equal to the threshold generates no voucher.

4. **RecoveryConvertedWithinTwentyFourHours**
   - A matching order for the cart owner within 24 hours sets
     `Converted`, with a recovery timestamp.

5. **RecoveryExpiredAfterTwentyFourHours**
   - An open recovery at or after the 24-hour boundary is set to `Expired`.

6. **MostRecentCartOnly**
   - A customer with multiple abandoned carts causes exactly one recovery
     workflow for the latest cart.

7. **CrossCustomerRecoveryAccessForbidden**
   - Customer A's token attempting to read Customer B's cart recovery returns
     `403`, not `200`.

8. **AdminEndpointRejectsNonAdmin**
   - A non-admin token calling `/scan/abandoned-carts` receives `403` with
     `error.code = "ADMIN_ROLE_REQUIRED"`.

Additional required coverage:

- Batch commits independently and cursor advances only after success.
- Restart resumes after the persisted cursor.
- Duplicate order-completion events do not duplicate conversion or voucher.
- Stale cart version rejects concurrent state mutation.
- Owner mismatch in an order event does not mark recovery converted.
- Composite scan index exists and a 1000-cart test remains database-filtered.
- Analytics and cart endpoints use their role/owner checks.
- Expiry and conversion are mutually exclusive.
- No `float` or `double` appears in currency/model fields.

Tests must verify observable behavior and status, not implementation details
alone.

## 14. Implementation sequence

1. Add and register `CartDbContext` and migration.
2. Implement domain models and decimal validation.
3. Implement repositories and optimistic concurrency.
4. Implement scan detection, cursor, voucher, and notification boundaries.
5. Implement recovery tracking, owner matching, and expiry.
6. Implement service-layer BOLA checks and API endpoints.
7. Add all required tests.
8. Run `dotnet build` and the smallest relevant test suite first, then
   `dotnet test`.

Do not generate mock-only endpoints or leave feature paths that compile but
are unreachable from the application host.

## 15. Acceptance criteria

The feature is complete when:

- Pending carts inactive for at least 30 minutes are abandoned exactly once.
- The scan query uses the declared composite index and processes in batches.
- Batch cursors persist and support restart.
- High-value carts receive exactly one one-time voucher.
- Low-value carts receive no voucher.
- Multiple abandoned carts trigger only the latest customer's workflow.
- Matching orders within 24 hours convert the recovery.
- Expired carts cannot convert after 24 hours.
- Owner and admin authorization failures return machine-readable 403s.
- Analytics pagination is cursor-based.
- State transitions are persisted.
- Decimal monetary fields never use floating-point types.
- At least the eight required test scenarios pass.
