# Inventory Sync Review and Remediation

## Scope and status

Reviewed and remediated the Inventory Sync model, EF Core persistence, service
contracts, and JWT validation registration. The repository has no running API
host or inventory endpoints, so endpoint-level authorization and HTTP
model-binding behavior remain host integration work, not claims that an
endpoint vulnerability has been fully tested.

Copilot assisted with initial implementation and identifying code paths. Human
engineering judgment was used to determine retail-specific store identity,
stock concurrency semantics, safe DTO fields, and authentication key
requirements. Findings below distinguish applied fixes from remaining
integration work.

## Findings and remediation

### 1. Inventory identity omitted store scope

- **Location:** `src/inventory/model.cs`, `Inventory`; `src/inventory/InventoryDbContext.cs`, `OnModelCreating`
- **Severity:** High
- **Status:** Remediated in the data model.
- **Issue and retail impact:** A globally unique product inventory row could not
  represent different on-hand quantities at separate stores and could not
  support store-scoped stock lookups.
- **Detection:** Compared model fields and unique-index configuration against
  the omnichannel retail requirements in `README.md`. Copilot helped surface
  the candidate gap; human domain judgment established that stock is
  location-specific.
- **Fix applied:** Added required `StoreId` and a unique composite
  `(StoreId, ProductId)` index. Service and repository operations require both
  the store context and product identifier. The API host must still derive and
  authorize store context from trusted claims.

### 2. Stock updates could overwrite newer values

- **Location:** `src/inventory/model.cs`, `UpdateStock`; `src/inventory/service.cs`, `UpdateStockAsync`; `src/inventory/InventoryRepository.cs`, `UpdateAsync`
- **Severity:** High
- **Status:** Remediated using optimistic concurrency.
- **Issue and retail impact:** An unconditional read-modify-save could allow
  delayed POS or synchronization data to overwrite a newer stock count,
  leading to overselling or missed availability.
- **Detection:** Traced the update call path and checked the EF model for
  concurrency configuration. Human judgment was needed to identify the
  customer and operations consequences of stale inventory data.
- **Fix applied:** Updates require an expected version. EF Core includes the
  prior version in the update predicate, increments the version, and maps
  concurrency conflicts to `InventoryConcurrencyException`. Tests use two
  repository contexts to verify stale writes fail.
- **Remaining decision:** Each upstream feed must define whether values are
  absolute snapshots or quantity deltas and how retries are made idempotent.

### 3. Service exposed persistence/domain entities

- **Location:** `src/inventory/service.cs`, service operations
- **Severity:** Medium
- **Status:** Remediated at the application-service boundary.
- **Issue and retail impact:** Returning EF entities coupled callers to storage
  shape and created a risk that a future HTTP layer would expose or bind
  persistence fields.
- **Detection:** Inspected public service signatures against the repository's
  model/repository/service/controller architecture. Copilot generated the
  original signatures; a human must choose safe application and API
  boundaries.
- **Fix applied:** Added sealed `CreateInventoryRequest`,
  `UpdateInventoryStockRequest`, and `InventoryResponse` records plus
  `IInventoryService`. The service maps between these allowlisted contracts
  and the domain model. The write requests intentionally omit `StoreId`, `Id`,
  and other persistence fields.

### 4. Repository had provider-specific database-driver logic

- **Location:** `src/inventory/InventoryRepository.cs`
- **Severity:** Medium
- **Status:** Remediated.
- **Issue and retail impact:** Provider-specific SQLite exception inspection
  coupled the data-access layer to a raw database driver and made provider
  changes harder.
- **Detection:** Reviewed repository imports and exception handling. Copilot
  assisted with identifying the provider-specific branch; architectural
  judgment determined the repository should use the ORM abstraction.
- **Fix applied:** Repository reads/writes use EF Core only; no direct SQLite
  driver types or raw SQL are used in the repository. The unique database
  constraint remains the final duplicate protection. A service pre-check
  returns `InventoryAlreadyExistsException` for an existing row; a rare
  simultaneous insert race may still surface as an EF `DbUpdateException`
  rather than being misclassified as a duplicate.

### 5. Schema setup used `EnsureCreated`

- **Location:** `src/inventory/InventoryServiceCollectionExtensions.cs`, `MigrateInventoryDatabaseAsync`; `src/inventory/Migrations/`
- **Severity:** Medium
- **Status:** Partially remediated.
- **Issue and retail impact:** `EnsureCreated` does not provide a managed
  schema-upgrade path for deployed retail data.
- **Detection:** Inspected database initialization and compared it with
  schema-evolution needs.
- **Fix applied:** Added an EF Core initial migration and an explicit
  `MigrateInventoryDatabaseAsync` method. Documentation instructs operators to
  run it once as a deployment step.
- **Remaining work:** Existing `EnsureCreated` databases need a reviewed
  upgrade migration and approved store-ID backfill. The migration must not be
  applied blindly to existing rows. SQLite is still selected in this module's
  registration; provider selection should move to the host if multiple
  providers are required.

### 6. Duplicate creates lacked a clear normal-path result

- **Location:** `src/inventory/service.cs`, `CreateAsync`; unique index in `src/inventory/InventoryDbContext.cs`
- **Severity:** Medium
- **Status:** Partially remediated.
- **Issue and retail impact:** Repeated sync/create requests could create
  provider-specific failures without a stable normal-path result.
- **Detection:** Compared create behavior with the unique store/product
  constraint and considered retry behavior in a sync workflow.
- **Fix applied:** The service checks for an existing store/product entry and
  raises `InventoryAlreadyExistsException`; the unique index protects against
  races and preserves data integrity.
- **Remaining work:** Translate a concurrent unique-key race in a provider-
  appropriate infrastructure adapter if callers require one uniform conflict
  result across database providers.

### 7. Test coverage omitted important retail cases

- **Location:** `tests/InventoryServiceTests.cs`; `tests/InventoryJwtAuthenticationTests.cs`
- **Severity:** Medium
- **Status:** Partially remediated.
- **Issue and retail impact:** Earlier tests did not verify store isolation,
  stale stock writes, duplicate creation, or JWT security configuration.
- **Detection:** Compared test scenarios to service branches, database
  constraints, synchronization risks, and the repository's security
  requirements.
- **Fix applied:** Added SQLite-backed tests for store separation, stale
  concurrency writes, duplicate creates, invalid store/negative quantity, and
  CRUD. Added JWT tests for RS256 and HS256 signatures, algorithm pinning,
  unsigned-token rejection, mandatory lifetime fields, expiry, and future
  `iat`.
- **Remaining work:** Add HTTP integration tests for bearer challenge behavior,
  endpoint DTO binding, cross-store authorization, and error response codes
  once the API host/endpoints are implemented.

### 8. Store scope was not connected to authenticated authorization context

- **Location:** `src/inventory/service.cs`; future API integration
- **Severity:** High if exposed without authorization
- **Status:** Data access is store-scoped; endpoint-level BOLA controls remain
  unimplemented because no endpoints exist.
- **Issue and retail impact:** Requiring a `storeId` parameter alone does not
  prove a caller is authorized for that store. An endpoint that accepts an
  arbitrary client-provided store ID could expose or modify another store's
  inventory.
- **Detection:** Traced the store ID from service to query and searched for
  endpoint and authenticated-principal integration. There is no API host in
  this project.
- **Fix applied:** The write DTOs omit `StoreId`; all repository access is
  scoped by store and product.
- **Required host fix:** Derive the store from validated claims or an
  authorization service, verify the principal may operate on that store, then
  call the service. Never treat authentication or a client-supplied ID alone
  as authorization. Add cross-store BOLA tests at the endpoint layer.

## Required implementation checks

### Remediated inventory model

The `Inventory` model is persisted with EF Core ORM configuration and
store/product identity. It represents quantity only and has no price or other
monetary field, so there is no monetary numeric type to choose in this model.
If pricing is added to a product, cart, or order model, use `decimal`; do not
introduce floating-point currency.

The model validates nonempty store IDs, normalized bounded product IDs, and
nonnegative integer stock. The database independently enforces nonnegative
quantity and unique store/product identity.

### Repository and service architecture

- `InventoryRepository` uses EF Core for data access and does not issue raw SQL
  or use a direct database-driver API.
- `InventoryService` contains validation, version handling, and mapping only;
  it does not import EF Core or database namespaces.
- `IInventoryService` and request/response records make service contracts
  explicit.
- The write records are allowlists: create accepts product ID and stock
  quantity; update accepts stock quantity and expected version. Store scope is
  supplied separately by the trusted caller, not request-body binding.
- There are no HTTP endpoints yet; endpoint handlers must bind these DTOs, not
  `Inventory` entities.

### JWT validation

`src/security/RetailEdgeAuthenticationExtensions.cs` registers JWT bearer
authentication for API hosts and:

- accepts only exact `RS256` or `HS256` configuration and sets
  `TokenValidationParameters.ValidAlgorithms` to that single configured
  algorithm;
- requires signed tokens and validates the signing key, issuer, and audience;
- requires `exp` and `nbf`, validates token lifetime, requires numeric `iat`,
  rejects future-issued tokens beyond the configured 30-second clock skew, and
  rejects expired tokens;
- requires a 2048-bit RSA public key for RS256 or at least 32 bytes of
  Base64-configured key material for HS256;
- fails startup for missing configuration or unsupported algorithms instead
  of silently disabling authentication.

Hosts must call `UseAuthentication()` before `UseAuthorization()`. Signing
material must come from a managed secret/configuration provider, never source
control. Since no host exists here, HTTP `401` responses, error codes, bearer
middleware challenge behavior, and authorization policies remain integration
tests to add at host creation. Real RS256 and HS256 token signatures are covered
by the current unit tests.

## Issues Copilot Introduced That Required Human Judgment

1. **Missing store scope (retail-specific).** The initial generated model used
   a global product key, preventing distinct store stock. Human retail-domain
   judgment identified the need for store-level inventory. This has been
   remediated with `StoreId`, a composite unique index, and store-scoped
   repository operations.
2. **No concurrency semantics.** The initial read-modify-save implementation
   could overwrite fresh stock with stale POS/sync data. Human judgment was
   needed to identify the operational impact and require a version strategy.
   Optimistic concurrency is now implemented and tested; source-specific
   snapshot/delta semantics remain a business integration decision.
3. **No security boundary around store identifiers.** The original service
   accepted product IDs without store or caller scope. Store scope is now
   required, and request DTOs do not allow clients to set it. A human must
   ensure the future API derives authorized store scope from claims; the
   service parameter is not itself an authorization check.
4. **No JWT verification controls existed.** Human review identified the need
   to reject algorithm confusion and validate token lifetimes. JWT bearer
   registration now pins RS256/HS256 and validates `exp`, `iat`, and `nbf`;
   API-host integration and authorization still require implementation.
