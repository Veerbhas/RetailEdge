# RetailEdge

## Case study

RetailEdge is a microservices-based omnichannel retailer serving a web store,
mobile app, and physical-store POS terminals. This repository implements the
Cart Abandonment Recovery System.

The system must:

- Identify a cart as abandoned after 30 minutes of customer inactivity.
- Start a recovery workflow after abandonment is detected.
- Send a personalised email or push notification to the customer.
- Optionally attach a one-time discount voucher when the cart value exceeds
  the configured voucher threshold.
- Record the abandonment and recovery events for analytics.
- Mark a recovery as successful when the customer completes checkout within
  24 hours.
- Record the conversion associated with the successful recovery.

The inventory module must also be reviewed before any feature code relies on
it. Existing inventory code must not be treated as production-ready merely
because AI generated it.

## Tech stack

- C# 12
- .NET 8
- ASP.NET Core for any HTTP endpoints
- xUnit for unit and integration tests
- GitHub Copilot for AI-assisted engineering

This project uses .NET 8 conventions and the repository's current C# toolchain.
Dependencies and test frameworks should be added only when implementation
requires them.

## Repository structure

```text
RetailEdge/
├── .github/
│   └── copilot-instructions.md
├── src/
│   ├── inventory/
│   │   ├── model.cs
│   │   ├── service.cs
│   │   ├── InventoryDbContext.cs
│   │   ├── IInventoryRepository.cs
│   │   ├── InventoryRepository.cs
│   │   ├── InventoryServiceCollectionExtensions.cs
│   │   ├── InventoryConcurrencyException.cs
│   │   ├── InventoryContracts.cs
│   │   ├── IInventoryService.cs
│   │   └── Migrations/
│   ├── security/
│   │   └── RetailEdgeAuthenticationExtensions.cs
│   └── cart/
├── tests/
│   ├── InventoryServiceTests.cs
│   ├── InventoryJwtAuthenticationTests.cs
│   └── RetailEdge.Tests.csproj
├── README.md
└── RetailEdge.csproj
```

Production source belongs under `src/`. Automated tests belong under `tests/`.

## Inventory service

The inventory service uses SQLite through Entity Framework Core. Each inventory
row is scoped by store and product, so the same product can have different stock
levels at different locations. The database enforces a unique `(StoreId,
ProductId)` pair and rejects negative stock quantities. Product IDs are
trimmed, required, and limited to 100 characters.

`UpdateStockAsync` sets the current stock quantity (it does not apply a delta)
and requires the version read by the caller. A stale version raises
`InventoryConcurrencyException` instead of overwriting newer stock. Missing
records return `null` from `GetByProductAsync`, cause `UpdateStockAsync` to
throw `KeyNotFoundException`, and make `DeleteAsync` return `false`. Call
`MigrateInventoryDatabaseAsync()` from a single deployment step to apply the
included EF Core migration; do not run migrations concurrently from every
application replica. The included initial migration targets SQLite and is for
new installations. Do not apply it directly to a database created with the
earlier `EnsureCreated` schema. Existing databases need a reviewed upgrade
migration and an approved mapping of each existing row to a store; no store
identity is guessed automatically.

Creating a second inventory record for the same store and product raises
`InventoryAlreadyExistsException`. The optimistic version must be refreshed
after a concurrency conflict before retrying an update.

The inventory application service accepts allowlisted
`CreateInventoryRequest` and `UpdateInventoryStockRequest` records and returns
`InventoryResponse`; it does not expose EF entities as service results. The
requests intentionally omit `StoreId`: an API host must derive the authorized
store from its authenticated principal and pass that scope to the service.
Inventory tracks quantities only and has no monetary field; monetary amounts
belong in the relevant pricing/order model and must use `decimal`.

## JWT authentication setup

The project provides `AddRetailEdgeJwtAuthentication(configuration)` from
`RetailEdge.Security` for API hosts. Configure `Authentication:Jwt:Algorithm`
to exactly `RS256` or `HS256`, and provide `Issuer` and `Audience`. RS256
requires `RsaPublicKeyPem` with a
minimum 2048-bit public key. HS256 requires `HmacSigningKeyBase64` containing at
least 32 random bytes. Supply key material through a managed secret/configuration
provider or environment variables; never commit signing keys. The validator
pins the configured algorithm, requires a signature, issuer, audience, `exp`,
`nbf`, and numeric `iat`, and rejects future-issued tokens beyond a 30-second
clock skew. API hosts must include `UseAuthentication()` before
`UseAuthorization()`. This library setup does not by itself implement endpoints
or resource-level authorization; each host must authorize the requested store
against trusted claims before calling inventory operations.

## Engineering goals

- Keep domain rules separated from infrastructure and delivery concerns.
- Use explicit models and application services instead of putting business logic
  in controllers or notification providers.
- Make time-based behavior testable with an injectable clock rather than direct
  calls to the system clock.
- Prefer idempotent recovery and conversion operations so retries and duplicate
  events do not create duplicate notifications or conversions.
- Use dependency injection and interface boundaries for notification, voucher,
  analytics, inventory, and persistence integrations.
- Emit structured logs and correlation identifiers for traceability.
- Never log payment, credentials, tokens, or other sensitive customer data.
- Validate and authorise all externally supplied identifiers and thresholds.
- Configure voucher thresholds and recovery timing through configuration, not
  hard-coded feature values.
- Avoid vendor lock-in in domain code.

## Inventory review gate

Before implementing cart recovery integration with inventory, inspect
`src/inventory/model.cs` and `src/inventory/service.cs` and verify:

- Data types, nullability, and validation match the inventory domain.
- Synchronisation behavior is deterministic and handles retries safely.
- Partial failures, duplicates, and stale data are handled correctly.
- Concurrency and scaling concerns are addressed.
- External boundaries have appropriate error handling and logging.
- Sensitive data is not exposed or logged.
- Automated tests cover normal and failure cases.

Document review findings before production code depends on this module.
Fix defects or replace unreviewed AI-generated code before integration.

## Cart abandonment workflow

The intended domain workflow is:

1. Record cart activity and update the customer's cart.
2. Detect carts inactive for at least 30 minutes.
3. Mark the cart abandoned exactly once.
4. Create a recovery event for analytics.
5. Send a personalised notification through email or push.
6. Create and attach a one-time voucher only when configured cart-value rules
   are satisfied.
7. When checkout completes within 24 hours of abandonment, mark the recovery
   successful and record a conversion.
8. Prevent duplicate notifications and duplicate conversions from retries or
   repeated events.

The notification and voucher outcomes must be durable and observable. Failures
should not silently discard a recovery request.

## Delivery requirements

AI-generated changes must be:

- Grounded in the requirements in this README and repository conventions.
- Reviewed for correctness, security, maintainability, and test coverage.
- Covered by the smallest meaningful automated tests.
- Validated with build and test commands before review.
- Explained where assumptions or trade-offs affect behavior.

No production or test implementation should be added without requirements,
scope, or assignment details defining the expected behavior.

## Running the application

Development mode starts both EF Core databases and the ASP.NET Core API:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --project RetailEdge.csproj --urls "http://127.0.0.1:5087"
```

For production or shared environments, provide
`Authentication:Jwt:HmacSigningKeyBase64` as a Base64-encoded random key of at
least 32 bytes, and configure `Authentication:Jwt:Issuer` and
`Authentication:Jwt:Audience`. The SQLite connection can be overridden with
`ConnectionStrings:RetailEdge`.

Run the automated suite with:

```powershell
dotnet test tests/RetailEdge.Tests.csproj
```