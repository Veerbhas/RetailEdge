- After every user prompt for this assignment, append the prompt verbatim to
  `PROMPTS.md`.
- Do not remove or rewrite earlier prompt entries.
- Before starting any task, file update, or assignment step, read `README.md`
  and use its case-study information, requirements, stack, and engineering
  constraints to guide the work.

## Technology stack

- Use C# 12 and .NET 8 for production and test code.
- Use ASP.NET Core for HTTP APIs and controllers.
- Use xUnit for automated unit and integration tests.
- Use dependency injection for application dependencies.
- Add packages only when they are necessary for an implemented requirement.

## Architecture conventions

- Follow this dependency direction: model -> repository -> service ->
  controller.
- Keep models as the domain/data contracts used by the feature.
- Put persistence operations behind repository interfaces.
- Put cart, abandonment, recovery, voucher, notification, and conversion rules
  in services.
- Keep controllers thin; validate DTO input, call services, and map outcomes to
  HTTP responses.
- Domain code must not depend on ASP.NET, third-party providers, or database
  implementation details.
- Use interfaces at external boundaries and avoid vendor lock-in.

## Coding standards

- Use PascalCase for types and public members, camelCase for parameters and
  local variables, and UPPER_SNAKE_CASE only for environment variables.
- Use explicit C# types and nullable annotations; do not use `dynamic`,
  `as any`, or intentionally disabled nullability.
- Public APIs must validate required, range, format, and scope values.
- Keep methods and classes focused; avoid business logic in controllers.
- Use structured logging with named properties, not string interpolation for
  log message fields.
- Log correlation IDs and relevant operation/cart/recovery IDs when needed.
- Never log passwords, payment details, access tokens, refresh tokens, or
  customer PII.
- Avoid hard-coded thresholds and timing values; expose them through typed
  configuration.

## Retail domain rules

- Use `decimal` for every currency and monetary calculation. Never use `float`
  or `double` for currency.
- Use validated `store_id` and `customer_id` values to scope all store-specific
  and customer-specific operations.
- Never expose or mutate another store's or customer's data.
- Treat customer PII, including names, email addresses, phone numbers, postal
  addresses, and delivery details, as protected data.
- Minimise PII collection and logging, and only use it for an authorised
  customer-facing or operational purpose.
- Personalised notifications must use approved, validated customer contact
  details and must not reveal cart or customer data in URLs or logs.
- Use one-time voucher identifiers and atomic redemption rules to prevent
  voucher reuse.

## Security rules

- Pin JWT validation in middleware to exactly `RS256` or `HS256`; reject
  `alg=none`, inferred algorithms, mixed algorithms, and any unapproved
  algorithm before signature validation.
- Copilot-generated JWT verification code must be reviewed for algorithm,
  signature, key, issuer, and audience handling.
- JWT middleware must explicitly validate `exp`, `iat`, and `nbf` claims in
  addition to the signature.
- Failure of signature, issuer, audience, `exp`, `iat`, or `nbf` validation
  must return `401` with a machine-readable, specific error code.
- Extract and validate `store_id`, `customer_id`, `role`, and `scope` claims
  from the JWT payload. Missing or invalid required claims must be rejected
  with `401` and a specific error code.
- Every write endpoint must accept a Data Transfer Object that explicitly
  allowlists permitted fields. Reject or ignore unknown fields and never
  directly bind the request body to domain entities or persistence models.
- Prohibit mass assignment; never permit clients to set identifiers, ownership,
  role, status, totals, or timestamps unless the endpoint intentionally
  authorised and implemented that field.
- For every resource-by-ID endpoint, verify that the authenticated user owns
  the resource or has explicit permission for that resource. Authentication
  alone is not sufficient for a BOLA-safe lookup or mutation.
- Scope every database query to the authenticated customer and store context.
  A customer must not access cart, order, or voucher records belonging to
  another customer.
- Restrict store-level abandonment scans and analytics to the authenticated
  `store_id`; never allow a store-scoped operation to cross tenant boundaries.
- Never rely only on an object ID supplied by a client.
- Return a standard non-disclosing response for missing, unauthorized, or
  cross-context resources.
- Admin-only endpoints, including scan trigger and analytics summary, must
  require an authorised admin role in middleware. A valid non-admin token must
  return `403` with a machine-readable error code, not `401` or `500`.

## Performance rules

- All list endpoints must use cursor-based pagination with `cursor` and `limit`
  request parameters and `nextCursor` and `hasMore` response fields.
- Offset-based pagination with `page` or `skip` is prohibited for cart and
  recovery collection endpoints.
- Do not return unbounded collections; validate `limit` against configured
  minimum and maximum values.
- The cart abandonment detection scan must accept a configurable `batchSize`
  parameter.
- Commit each scan batch independently; a failure in a later batch must not
  roll back earlier successfully committed batches.
- The scan must be restartable from the last successfully processed cursor.
  Persist or return cursor/checkpoint state and avoid replaying successfully
  processed batches.
- Detection scan failures must stop safely or apply back-pressure instead of
  silently continuing; retries must be idempotent.
- Declare at least two database indexes in `SPEC.md` and in ORM model
  definitions for each data-backed feature where indexes support production
  queries.
- Every index declaration must specify columns, index type (B-tree, composite,
  or partial), and the specific query it supports.
- Cover the primary detection and ownership lookup paths, such as abandonment
  activity plus `store_id`/`customer_id` scoping.
- Add query limits and cancellation support to long-running or externally
  triggered operations.

## Testing expectations

- Add xUnit tests under `tests/` for domain rules and application services.
- Test the 30-minute abandonment boundary and 24-hour recovery-success window.
- Test cart, voucher, notification, analytics, and conversion failures at least
  once for each external boundary.
- Test exactly-once or idempotent behavior for duplicate abandonment,
  notification, and conversion events.
- Test BOLA with cross-user and cross-store resource attempts.
- Test JWT algorithm pinning, `alg=none`, signature failures, expiry,
  future issuance, and not-before validation. Assert `401` and the specific
  machine-readable error code.
- Test missing or invalid `store_id`, `customer_id`, `role`, and `scope`
  claims.
- Test DTO write allowlisting and rejection of unexpected fields.
- Test decimal monetary calculations and verify no floating-point currency type
  is introduced.
- Test cursor pagination `cursor`/`limit` inputs, `nextCursor`/`hasMore`,
  ordering, continuation, and limit bounds.
- Test configurable batch detection, independent batch commits, restart from
  the last successful cursor, duplicate processing, and failure/back-pressure
  behavior.
- Test every admin-only scan trigger and analytics summary endpoint with a
  valid non-admin token; assert `403` and a machine-readable error code.
- Prefer an injectable clock and fake repositories/providers for deterministic
  tests.
- Run the smallest relevant test suite, then build the affected project.