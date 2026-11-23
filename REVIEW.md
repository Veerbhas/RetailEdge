# Inventory Code Review

## Scope and summary

Reviewed the generated inventory implementation in `src/inventory/`, its
dependency-injection setup, project configuration, and the inventory tests.
This is an architectural and security-oriented review, not a penetration test.
No code fixes were applied as part of this review; recommended changes below
remain open.

The implementation has useful foundations: domain-level validation rejects
negative stock, the database adds a nonnegative-stock check constraint, and a
unique product index prevents duplicate inventory rows for the current model.
The main design gaps are that inventory is not store-scoped, concurrent updates
can overwrite one another, and persistence setup/schema evolution are coupled
to SQLite and `EnsureCreated`.

## Findings

### 1. Inventory identity is not store-scoped

- **Location:** `src/inventory/model.cs`, `Inventory` properties; `src/inventory/InventoryDbContext.cs`, `OnModelCreating`
- **Severity:** High
- **Issue and retail impact:** Inventory consists of `ProductId` and
  `StockQuantity`, and `ProductId` is globally unique. A product stocked at two
  physical stores cannot have separate quantities. Store-specific fulfilment
  and stock availability therefore cannot be modeled correctly. Repository
  queries also cannot enforce store scope.
- **Detection:** Inspected the model fields and unique-index declaration and
  compared them with the omnichannel retail context in `README.md`. Copilot
  helped identify the missing scope as a candidate concern; engineering
  judgment was required to determine that a global product key is inadequate
  for store-level stock.
- **Recommended fix:** Add a validated `StoreId` to the inventory identity.
  Use a unique composite index on `(StoreId, ProductId)` and require authorized
  store context in repository and service operations before exposing the
  feature to callers.

### 2. Concurrent stock updates can overwrite newer values

- **Location:** `src/inventory/service.cs`, `InventoryService.UpdateStockAsync`; `src/inventory/InventoryRepository.cs`, `UpdateAsync`
- **Severity:** Medium
- **Issue and retail impact:** The service reads a stock row, changes it, and
  then saves an unconditional update. If two POS, e-commerce, or synchronization
  updates race, the later save can overwrite a newer quantity with stale data.
  Incorrect availability can lead to overselling or missed sales.
- **Detection:** Traced the read-modify-save path through the service and
  repository and checked the entity mapping for a concurrency token or version
  check. Copilot helped surface concurrency as a review area; engineering
  judgment was needed to relate the race to multiple retail stock sources.
- **Recommended fix:** Define whether incoming values are authoritative
  snapshots or deltas. For snapshots, add optimistic concurrency/version
  checks and a conflict/reconciliation policy; for deltas, use an atomic,
  idempotent update strategy.

### 3. Provider selection and schema lifecycle are coupled to SQLite

- **Location:** `src/inventory/InventoryServiceCollectionExtensions.cs`, `AddInventory` and `InitializeInventoryDatabaseAsync`
- **Severity:** Medium
- **Issue and retail impact:** Service registration hard-codes `UseSqlite`, and
  startup uses `EnsureCreatedAsync`. This couples application setup to one
  provider and does not provide a migration path for evolving an already
  deployed schema. A schema change can become difficult to roll out safely
  across retail services and environments.
- **Detection:** Inspected the registration and initialization extension
  methods and compared them with the README engineering goal to avoid vendor
  lock-in. Copilot-assisted review surfaced the SQLite/`EnsureCreated` design;
  choosing migration-based evolution is an engineering architecture decision.
- **Recommended fix:** Configure the provider in the application composition
  root while keeping inventory registration provider-agnostic. Use EF Core
  migrations and an explicit deployment/startup migration strategy for
  persistent environments.

### 4. Application service returns the persistence/domain entity directly

- **Location:** `src/inventory/service.cs`, `CreateAsync`, `GetByProductAsync`, and `UpdateStockAsync`
- **Severity:** Medium
- **Issue and retail impact:** The service exposes the `Inventory` entity to
  its callers. This makes consumers depend on the entity shape and makes it
  easier for a future HTTP layer to expose or bind the persistence model
  directly. That can constrain model evolution and increase accidental
  over-posting or data-exposure risk.
- **Detection:** Traced public service return types and compared the boundary
  with the repository's stated model/repository/service/controller convention.
  Copilot generated the service shape; an engineer must decide which types are
  appropriate across an application or API boundary.
- **Recommended fix:** Define explicit application result contracts and
  request/response DTOs at delivery boundaries. Map permitted fields
  deliberately; do not bind HTTP writes directly to the entity.

### 5. Repository methods commit independently

- **Location:** `src/inventory/InventoryRepository.cs`, `AddAsync`, `UpdateAsync`, and `DeleteByProductAsync`
- **Severity:** Medium
- **Issue and retail impact:** Each repository method calls `SaveChangesAsync`.
  This is adequate for the current single-row operations, but future workflows
  that must persist inventory together with an audit record, outbox event, or
  other state have no explicit shared commit boundary. A partial write could
  leave stock changes inconsistent with downstream retail events.
- **Detection:** Inspected where `SaveChangesAsync` is called and compared it
  with the README's durable/observable workflow and integration goals. This is
  a forward-looking architectural limitation, not evidence that the present
  single-row calls currently partially commit.
- **Recommended fix:** When introducing multi-record workflows, define a
  transaction/unit-of-work boundary above individual repository operations
  (or use an outbox pattern for reliable event publication). Keep the current
  simple boundary if no multi-record atomicity requirement exists.

### 6. Expected persistence conflicts have no application-level outcome

- **Location:** `src/inventory/service.cs`, `CreateAsync`; `src/inventory/InventoryRepository.cs`, `AddAsync`
- **Severity:** Medium
- **Issue and retail impact:** A duplicate product is prevented by a unique
  database index, but the service does not translate that expected conflict
  into a stable application result. A caller may receive a provider-specific
  exception, making retries, API error mapping, and operational handling
  provider-dependent.
- **Detection:** Compared create behavior with the unique index and followed
  repository exceptions through the service. Copilot can generate the
  persistence constraint, but deciding the public conflict contract requires
  application-level judgment.
- **Recommended fix:** Translate recognized unique-key conflicts into a
  domain/application conflict outcome while allowing cancellation and
  unexpected database failures to propagate visibly. Add a duplicate-create
  test.

### 7. Automated coverage does not exercise key failure and race paths

- **Location:** `tests/InventoryServiceTests.cs`
- **Severity:** Medium
- **Issue and retail impact:** The current tests cover the SQLite CRUD path and
  rejection of negative stock, but do not cover duplicate creates,
  update-not-found, invalid product IDs, database failure behavior, or
  concurrent updates. Those omissions leave important stock-integrity and
  operational behavior unverified.
- **Detection:** Read the test cases and compared their scenarios with the
  service/repository branches and the README testing expectations. Copilot
  assisted by adding basic happy-path coverage; engineering judgment is
  required to choose high-impact retail failure and concurrency cases.
- **Recommended fix:** Add focused tests for the listed validation and
  persistence outcomes. Add deterministic concurrency/conflict tests after
  selecting the stock update semantics and concurrency strategy.

### 8. Service/repository operations do not accept store or caller scope

- **Location:** `src/inventory/service.cs`, all public operations; `src/inventory/IInventoryRepository.cs`, all methods
- **Severity:** Medium
- **Issue and retail impact:** No store or authenticated-caller context is
  represented in these operation contracts. There are no HTTP endpoints in
  this code, so this is not a confirmed exploitable BOLA vulnerability today.
  If exposed as-is, however, callers could not be restricted by store at this
  layer, and a product ID alone would not prove authorization.
- **Detection:** Inspected service/repository parameters and searched the
  reviewed code for authentication, user context, and store identifiers. The
  absence of an API limits exploitability assessment; human security judgment
  is needed to prevent the service from becoming an unsafe endpoint dependency.
- **Recommended fix:** Add store scope to the inventory model and repository
  predicates. At the API boundary, derive authorized user/store context from
  validated claims and enforce authorization for every read and mutation;
  never trust a client-supplied object ID as authorization.

## Security-specific review

The reviewed inventory code contains no JWT implementation, authentication
middleware, HTTP controllers/endpoints, or request DTOs. Therefore the
following are **not implemented surfaces**, rather than confirmed vulnerabilities:

- **JWT algorithm pinning:** No JWT verification code exists to assess whether
  algorithms are pinned or `alg: none` is rejected. When authentication is
  added, explicitly allow only the approved algorithm and validate signature,
  issuer, audience, and key.
- **Token expiry claims:** No token validation middleware exists to assess
  `exp`, `iat`, or `nbf`. Configure and test all three when JWT authentication
  is implemented.
- **Mass assignment / DTO whitelisting:** There are no write endpoints or
  model-binding DTOs. Keep API write DTOs allowlisted and map them explicitly
  rather than binding requests to `Inventory`.
- **Broken Object Level Authorisation:** There are no resource HTTP endpoints
  to test. The service/repository also lack store/caller scope, so endpoint
  authorization and scoped queries must be implemented before exposure.

Additional retail-specific checks:

- **Negative stock:** The model rejects negative values, and
  `InventoryDbContext` declares a database check constraint. A missing stock
  constraint was not found.
- **Monetary precision:** The inventory model has no money, price, or cart
  total fields; no `float` or `double` monetary value was found. Use `decimal`
  if monetary values are introduced.
- **Voucher exposure and customer PII logging:** No voucher, notification, or
  customer data handling code exists in the reviewed inventory module, so
  these risks are not applicable to this code.

## Review process and use of Copilot

Copilot generated the initial inventory implementation and assisted in
identifying review areas such as store scoping, concurrency, and persistence
setup. The review then inspected the README requirements, domain model,
service/repository call paths, EF Core mapping and registration, and existing
test coverage. Findings above were retained only where they could be tied to
the current implementation; endpoint/JWT concerns were explicitly marked
not applicable where those surfaces do not exist.

Copilot assistance does not establish that a design is safe or appropriate.
Human engineering judgment was necessary to connect the global product
identity to multi-store retail, assess stale stock overwrite consequences,
distinguish a future BOLA risk from an active endpoint vulnerability, and
choose where provider, transaction, and API boundaries should live. No
production fixes were applied during this documentation review.

## Issues Copilot Introduced That Required Human Judgment

1. **Missing store scope (retail-specific).** Copilot generated inventory with
   a globally unique `ProductId` and no `StoreId`. This prevents per-store
   stock balances and would make safe store-scoped access impossible without
   changing the model and repository contracts. Recognizing the requirement
   depends on understanding that omnichannel inventory is location-specific,
   not merely detecting a syntax or test failure. Add `StoreId`, use a
   `(StoreId, ProductId)` key, and enforce authorized store scope in queries.

2. **No authorization context in service operations (security upgrade).**
   The generated service accepts product IDs but no authenticated user/store
   scope. There is no HTTP endpoint yet, so this is a design gap and not a
   demonstrated exploit. A human must ensure that future endpoints do not
   assume an identifier grants access; derive scope from authenticated
   context and enforce it on every fetch and mutation.

3. **Stock updates lack concurrency semantics.** The generated read-modify-save
   path has no version check and does not document how competing stock feeds
   are reconciled. A human must understand POS and online stock synchronization
   well enough to specify whether updates are snapshots or deltas and how
   stale updates are rejected.

The implementation does correctly validate against negative quantities and
defines a database check constraint; missing stock validation was not found.
