# TOOL_STRATEGY.md

## Feature Usage Log

| # | Where Used | Copilot Feature | Why This Feature Over Another | What Happened |
|---|---|---|---|---|
| 1 | Repository setup and README/case-study documentation | Agent Mode | A cross-file implementation workflow was needed, and Chat Mode alone would have required manual application of every suggested edit. | Created the project structure and integrated the RetailEdge case-study requirements into repository documentation. |
| 2 | Existing inventory and cart source review | `#file` context | Exact source files were more reliable than asking about the project without repository references. | Enabled line-specific review of inventory models, services, migrations, authentication, and cart files. |
| 3 | Repository-wide architecture and rules | Workspace-aware context | The feature touched models, EF Core, services, APIs, tests, and migrations. | Maintained consistent namespaces, decimal handling, dependency injection, DTO boundaries, and test conventions. |
| 4 | Security and authorization implementation | Agent Mode with explicit constraints | Multiple coordinated files needed the same JWT, DTO, and owner-validation rules. | Added machine-readable authorization errors and HTTP/service-level Customer A/Customer B checks. |
| 5 | SQL and EF Core incompatibility corrections | Ask/Chat Mode plus Edit Mode | A focused diagnostic approach was better than broad regeneration. | Identified enum and `DateTimeOffset` translation failures, then added integer EF properties and UTC-tick fields. |
| 6 | Abandonment, recovery, API, and test implementation | `/tests` and test execution | The rules required measurable behavioral evidence rather than source inspection alone. | Ran the focused and complete suites; the final suite passed 24 tests. |
| 7 | Selected cart scan controller explanation | `/explain` | It provided focused behavior review for the selected code. | Clarified authentication, role checks, scan execution, cursor encoding, and `202 Accepted` behavior. |
| 8 | Persistent team behavior | `.github/copilot-instructions.md` | Repository instructions apply consistently across future prompts, whereas a single chat prompt would apply only once. | Defined decimal currency, architecture, JWT, DTO, BOLA, cursor pagination, and testing conventions. |
| 9 | Scope-change impact analysis | Agent/Chat Mode with workspace and `#file` context | The assessment required reasoned multi-area impact rather than a code-only edit. | Produced migration, service, API, PII, marketing, test, effort, and risk analysis. |

## Scenario Responses

### 1. Understanding a legacy 600-line cart service

Use **Ask/Chat Mode with `#file` context and workspace-aware search**. Reference the exact legacy service and ask for responsibilities, dependencies, state mutations, external calls, and modification risks.

Then use `/explain` on the largest methods and follow with workspace searches for cart types and call sites. This is safer than asking Agent Mode to modify code before existing behavior and tests are understood.

### 2. Adding cursor-based pagination across 10 list endpoints

Use **Agent Mode with workspace-aware search** to inventory list endpoints and shared query contracts. Agent Mode can apply the same keyset pagination pattern across controllers, EF queries, response models, and tests.

Before broad changes, use `#file` context for one existing implementation and Ask/Chat Mode to confirm cursor encoding and ordering. Run `/tests` afterward to verify limit bounds, continuation, ordering, and invalid cursor behavior.

### 3. Verifying decimal discount calculations

Use **Ask/Chat Mode with `#file` context** to inspect the discount model and search for `float`, `double`, binary arithmetic, or rounding issues. Ask for exact boundary cases and decimal precision/scale requirements.

Use Edit Mode or Agent Mode to update the implementation and add focused tests. Run `/tests` to prove the discount never becomes binary floating point and remains exact at threshold boundaries.

### 4. Enforcing retail-domain-aware suggestions for the team

Use `.github/copilot-instructions.md` for persistent repository instructions. It is preferable to individual prompts because every developer receives the same default rules.

Include decimal currency, customer/store scoping, PII minimization, BOLA, DTO allowlisting, JWT validation, cursor pagination, and 30-minute/24-hour retail boundaries. Review and version-control the instructions rather than assuming team members always include them in a prompt.

### 5. Reviewing JWT authentication middleware

Use **Ask/Chat Mode with `#file` context** on the authentication middleware, followed by `/tests` or test execution. Ask specifically about algorithm confusion, `alg:none`, signature validation, issuer, audience, `exp`, `iat`, `nbf`, clock skew, and key size.

Treat Copilot as an analysis aid, not the security authority. Review tests and middleware independently and consider threat-model review before production.

### 6. Generating repository boilerplate for 4 models

Use **Agent Mode with `#file` and workspace context** and reference one existing repository/model/DTO pattern. Agent Mode can create coordinated domain entities, repository interfaces, EF mappings, migrations, and allowlisted DTOs.

Run `/tests` for validation and explicitly require no write DTO to expose ownership, status, totals, timestamps, versions, or role fields unless separately authorized.

## Security Review

### 1. JWT Algorithm Confusion

**Risk:** JSON Web Token (JWT) algorithm confusion occurs when a verifier accepts a weaker or different signing algorithm than the token issuer intended. A remote caller could attempt `alg:none` or substitute a symmetric key for an RSA key if algorithm selection is inferred.

**Retail impact:** Forged administrator or customer identities could trigger cart scans, analytics access, conversions, or cross-customer order operations.

**Mitigation:** Pin validation to exactly RS256 or HS256 in [RetailEdgeAuthenticationExtensions.cs](/D:\WorkSpace\RetailEdge\RetailEdge\src\security\RetailEdgeAuthenticationExtensions.cs). Require signature, issuer, audience, `exp`, numeric `iat`, and `nbf`; verify key type and reject unsupported algorithms. Tests cover unsigned, expired, future-issued, wrong-algorithm, and missing lifetime claims in [InventoryJwtAuthenticationTests.cs](/D:\WorkSpace\RetailEdge\RetailEdge\tests\InventoryJwtAuthenticationTests.cs).

### 2. Mass Assignment via Missing Data Transfer Object Whitelisting

**Risk:** Mass assignment occurs when request fields are directly bound to a persistence entity. Copilot can generate direct model binding that allows clients to set customer ownership, cart totals, status, voucher codes, timestamps, or optimistic-concurrency versions.

**Retail impact:** A malicious client could mark carts converted or expired, alter cart value, generate unauthorized voucher state, or overwrite another customer's cart.

**Mitigation:** Add explicit request DTO or service contracts that expose only permitted fields. Domain state transitions should be methods, and ownership/status/totals/timestamps should be server-derived. The cart API should not bind `AbandonedCart` directly.

### 3. Broken Object Level Authorisation

**Risk:** Broken Object Level Authorisation occurs when authentication is implemented but the requested resource is not checked against the authenticated user's ownership or scope.

**Retail impact:** Customer A could read or affect Customer B's abandoned cart, recovery status, voucher, or conversion event.

**Mitigation:** Verify the trusted customer claim against `Cart.CustomerId` in [CartController.cs:58](/D:\WorkSpace\RetailEdge\RetailEdge\src\cart\CartController.cs:58), and repeat the owner check in [RecoveryTrackingService.cs:55](/D:\WorkSpace\RetailEdge\RetailEdge\src\cart\RecoveryTrackingService.cs:55). Cross-owner access should return `403 CART_ACCESS_DENIED`; tests should use Customer A and Customer B IDs and verify no `200`.

### 4. JWT Expiry and Issued-At Validation

**Risk:** AI-generated middleware may validate a signature but omit expiration or lifetime claims. Future-issued or already-expired tokens can remain usable.

**Retail impact:** A stale session may continue scanning carts or accessing customer recovery data after expected authorization expiry.

**Mitigation:** Require `exp`, numeric `iat`, and `nbf`, validate lifetimes, and use bounded clock skew. Existing tests verify missing and invalid lifetime claims.

### 5. Notification or Voucher Idempotency

**Risk:** AI-generated notification and voucher code can run without durable state or correlation IDs, allowing duplicate emails and repeated or predictable vouchers.

**Retail impact:** Duplicate marketing messages increase unsubscribes and complaints, while predictable vouchers create margin loss and fraud.

**Mitigation:** Use durable outbox records or unique touchpoint/correlation keys, transactional state transitions, one-time voucher services, and cryptographically random redemption codes. The current logging notification provider and deterministic voucher placeholder are not production integrations.

## Limitations Encountered

### 1. Incorrect abandoned state

Copilot initially represented abandoned checkout carts with a `Cancelled` state. That mixed two different retail meanings and would have confused inventory, analytics, expiry, and notification workflows.

Detected through domain review and fixed by adding `CartCheckoutStatus.Abandoned` and setting it in `MarkAbandoned()`. In future prompts, allowed enum transitions and outcome-state tests should be specified before generation.

### 2. SQLite and EF Core translation failures

Copilot initially generated LINQ that used enum property comparisons and `DateTimeOffset` ordering/comparisons. Focused tests showed that SQLite/EF Core could not translate those expressions.

Detected through failing xUnit runs and fixed with EF integer-property comparisons and UTC-tick companion fields. In future work, request provider-specific keyset query patterns and require SQLite test execution immediately.

### 3. Missing HTTP-level BOLA coverage

Copilot initially included service-level owner checking but the API test suite did not prove that Customer A receives `403` instead of `200` for Customer B's cart.

Detected through explicit requirement review and fixed by adding [CustomerACannotAccessCustomerBCartRecovery](/D:\WorkSpace\RetailEdge\RetailEdge\tests\CartApiTests.cs:38). In future prompts, require both service-level and HTTP-level BOLA tests before implementation is considered complete.
