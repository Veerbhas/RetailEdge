# IMPACT_ANALYSIS.md

## Scope Change Summary

The product team proposes two additions:

1. Send a second recovery touchpoint six hours after the first notification if the cart has not converted.
2. Send the follow-up with a higher discount and include recommendations from an external recommendation engine in the recovery email.

These changes affect scheduling, voucher issuance, notification identity, data sharing, email consent, analytics, and database migrations.

## Proposed Design

### Data Model Changes

Add the following fields to `AbandonedCart`:

- `FirstNotificationTimestamp: DateTimeOffset?`
- `FollowUpDueTimestamp: DateTimeOffset?`
- `FollowUpSentTimestamp: DateTimeOffset?`
- `FirstVoucherCode: string?`
- `FollowUpVoucherCode: string?`
- `FollowUpDiscountAmount: decimal?`
- `FollowUpDiscountPercent: decimal?`
- `FollowUpStatus: RecoveryTouchpointStatus`
- `RecommendationRequestCorrelationId: string?`
- `RecommendationStatus: RecommendationDeliveryStatus`
- `MarketingConsentCheckedAt: DateTimeOffset?`

Use `decimal` for discount amount or percentage values. Do not use `float` or `double`.

Add a separate `RecoveryTouchpoint` entity instead of overloading the cart:

- `Id: Guid`
- `CartId: Guid`
- `TouchpointType: FirstNotification | FollowUp`
- `Status: Scheduled | Sent | Failed | Cancelled | Converted`
- `ScheduledAt: DateTimeOffset`
- `SentAt: DateTimeOffset?`
- `VoucherCode: string?`
- `CorrelationId: string`
- `AttemptCount: int`
- `Version: int`

Keep the existing version integer concurrency token and add optimistic concurrency to touchpoint writes. A follow-up must be unique per cart and correlation ID.

Add EF indexes:

- Composite index on `(CartId, TouchpointType, Status, ScheduledAt)`
- Composite index on `(FollowUpStatus, FollowUpDueTimestamp)`
- Composite index on `(RecommendationStatus, UpdatedTimestamp)`

The existing cart scan indexes remain unchanged.

### Migration

Create a new EF Core migration:

- Add nullable touchpoint timestamps and nullable follow-up voucher fields.
- Add `RecoveryTouchpoint` and its indexes.
- Populate existing carts conservatively:
  - Set `FirstNotificationTimestamp` from the existing `NotificationAccepted` transition, if available.
  - Set `FollowUpDueTimestamp = FirstNotificationTimestamp + 6 hours` only for open carts whose 24-hour window has not expired.
  - Create a scheduled follow-up row only when consent and recommendation eligibility have been verified.
  - Leave historical converted or expired carts with no new follow-up.
- Backfill in batches and keep both old and new columns readable until application deployment is complete.
- Deploy the migration additively: add columns and tables, then deploy code that understands them, then backfill.
- Do not drop existing voucher or transition columns in the same release.
- Use a migration transaction where supported and a bounded repair script otherwise.
- Validate row counts and no-data-loss checks before removing compatibility fields.
- Add a rollback migration that preserves data or is a forward-only, explicitly documented migration if data loss is unacceptable.

This is required because adding non-null touchpoint fields or dropping existing fields directly could break running replicas or erase existing recovery history.

### Service Changes

#### Abandonment Detection

- Preserve the 30-minute detection and existing first notification behavior.
- After the first notification is durably accepted, schedule `FollowUpDueTimestamp = FirstNotificationTimestamp + 6 hours`.
- Do not schedule a follow-up for converted or expired carts.
- Use independent scheduled-worker batches and persist each scheduled touchpoint transactionally.

#### Follow-Up Recovery Service

Add a follow-up worker that:

- Finds open carts whose follow-up is due.
- Requires an unconverted, unexpired cart before scheduling or sending.
- Uses a configurable higher discount as a decimal amount or percentage.
- Prevents duplicate follow-up vouchers and duplicate sends with the cart, touchpoint type, and correlation ID.
- Rechecks customer marketing consent immediately before send.
- Checks opt-out status and suppresses follow-up if consent is withdrawn.
- Marks a touchpoint as failed for retry without advancing the 24-hour conversion window.
- Respects the 24-hour conversion deadline.

The existing voucher implementation must become a production one-time voucher service. A deterministic `RE-{cartId}` code is not a safe production voucher.

#### Recommendation Integration

Add an interface for a recommendation provider. The service should:

- Request ranked candidate product IDs by opaque customer/cart identifiers.
- Supply only the minimum cart context: cart ID, item product IDs, currency, and recommended-candidate filters.
- Avoid sending email address, name, postal address, phone number, free-text cart data, or payment details.
- Receive product IDs, ranking, and confidence or a catalog key.
- Verify each recommendation against the store/catalog and supported currency.
- Persist only a recommendation request ID and status, not raw provider PII.
- Treat a recommendation failure as non-blocking; send the existing cart summary or email template without recommendations.

### API Changes

No public recovery or analytics endpoint is necessarily required for sending the first follow-up, but the existing response contracts should be extended carefully:

- `CartRecoveryApiResponse` may expose:
  - `FollowUpStatus`
  - `FollowUpDueTimestamp`
  - `FollowUpSentTimestamp`
  - `VoucherCode` only if the endpoint owner is authorized
- Add no request fields that allow a client to set status, timestamps, customer ownership, discount level, or opt-out state.
- Use allowlisted DTOs for any admin configuration endpoints.
- Keep `/carts/{id}/recovery` owner-verified.
- Keep `/scan/abandoned-carts` admin-only.
- Keep `/analytics/recovery` admin-only and cursor-paginated.

Recommended new endpoint:

- `POST /recommendations/refresh` should be internal/admin-only or invoked by a worker, not exposed to public customers.

Do not expose recommendation-provider secrets, internal confidence scores, or customer PII in analytics.

## Effort Estimate

| Area | Estimate |
|---|---|
| Domain model and EF migration | 2-3 person-days |
| Follow-up scheduler and outbox | 3-5 person-days |
| One-time voucher replacement | 2-4 person-days |
| Recommendation provider integration | 3-5 person-days |
| Email template, consent, and opt-out changes | 2-4 person-days |
| API and analytics updates | 1-2 person-days |
| Tests and migration validation | 3-4 person-days |
| Security, privacy, and operations review | 2-3 person-days |
| **Estimated total** | **18-30 person-days** |

The range depends on whether email/voucher/recommendation systems already exist and whether compliance/legal review is required.

## Risk Assessment

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Higher discount damages margin | Medium | High | Set campaign caps, exclude low-margin products, enforce one voucher per touchpoint, A/B test. |
| Over-messaging causes unsubscribes or complaints | Medium | High | Check consent, suppress duplicate sends, frequency-cap, provide clear opt-out. |
| Follow-up races conversion or expiry | Medium | High | Recheck status in the transaction and use optimistic concurrency. |
| Duplicate notification or voucher | Medium | High | Durable outbox, unique correlation key, touchpoint status, and idempotent provider API. |
| Recommendation data leaks PII | Low-Medium | High | Share pseudonymous IDs and product IDs only; sign the contract and minimize fields. |
| Migration downtime or backfill failure | Medium | Medium | Additive migration, batched backfill, replica-compatible deployment, rollback rehearsal. |
| Provider latency blocks recovery email | Medium | Medium | Timeout, retry budget, circuit breaker, fallback to cart-only email. |
| Cross-customer recommendation mismatch | Low-Medium | High | Validate owner and customer scope before provider or email delivery. |

## Test Updates

Existing tests to update:

- [AbandonmentDetectionTests.cs](/D:\WorkSpace\RetailEdge\RetailEdge\tests\AbandonmentDetectionTests.cs)
  - Assert first notification does not create a follow-up for converted or expired carts.
  - Add follow-up scheduling exactly six hours after first notification.
  - Verify no follow-up before six hours.
- [RecoveryTrackingTests.cs](/D:\WorkSpace\RetailEdge\RetailEdge\tests\RecoveryTrackingTests.cs)
  - Verify conversion cancels pending follow-up.
  - Verify expiry cancels pending follow-up.
- [CartApiTests.cs](/D:\WorkSpace\RetailEdge\RetailEdge\tests\CartApiTests.cs)
  - Verify owner-only follow-up status access.
  - Verify follow-up configuration remains outside public write DTOs.
  - Verify admin endpoint authorization remains unchanged.
- [CartModelTests.cs](/D:\WorkSpace\RetailEdge\RetailEdge\tests\CartModelTests.cs)
  - Verify new decimal fields never use float/double.
  - Verify touchpoint uniqueness and migration compatibility.

Minimum new tests:

- Follow-up sent exactly after six hours
- Follow-up suppressed without marketing consent
- Higher discount voucher generated once
- No duplicate follow-up after retry
- Conversion cancels scheduled follow-up
- Expiry cancels scheduled follow-up
- Recommendation unavailable falls back to cart-only email
- Recommendation payload contains no PII
- Existing abandoned records receive no unsafe duplicate follow-up
- Optimistic concurrency rejects stale touchpoint updates
- Follow-up is never sent after the 24-hour recovery deadline

## Dependencies and Integrations

This scope change affects:

- Durable notification/outbox infrastructure
- Email or push provider
- Marketing consent and suppression service
- One-time voucher/redemption service
- Catalog or inventory service for recommended products
- External recommendation engine
- Recommendation engine data-sharing agreement
- Analytics warehouse and event schema
- EF Core migration tooling
- Configuration and secrets management
- Monitoring, retry queues, and dead-letter handling

The existing inventory module is not the primary integration dependency, but recommended products must be validated for available catalog data before rendering.

## Retail Business Impact

The follow-up may increase voucher redemption and recovery conversion, but it increases:

- Discount margin exposure: every follow-up voucher can be redeemed, and a "higher" discount raises the maximum concession.
- Unplanned stacking: follow-up vouchers must not stack with other campaigns or vouchers unless explicitly approved.
- Email frequency and complaint risk: two touches in six hours require valid marketing consent and frequency controls.
- Opt-out obligations: supporting unsubscribe, suppression, and consent withdrawal is mandatory before send; opt-out must propagate quickly across notification providers.
- Jurisdiction-specific rules: the implementation should be reviewed against CAN-SPAM, GDPR/ePrivacy, CASL, or applicable regional rules. This repository should not claim legal compliance solely through software controls.

Recommended controls:

- Store consent evidence and source region.
- Enforce a suppression list at the final send boundary.
- Set an unsubscribe link and operational complaint handling.
- Cap the maximum follow-up discount with configuration.
- Track margin, redemption, unsubscribes, complaints, and conversion lift.
- A/B test one follow-up versus no follow-up before full rollout.

## Recommendation Engine Data Contract

Recommended outbound fields:

- `customer_id` or a non-reversible pseudonymous customer key
- `cart_id` or a non-reversible campaign correlation key
- `product_ids` currently in the cart
- `currency`
- `request_timestamp`
- `correlation_id`

Recommended fields should not include:

- Name
- Email address
- Phone number
- Postal address
- Cart-item quantities that are unnecessary
- Unit prices
- Payment or voucher secrets

The engine may return:

- Product IDs
- Rank
- Catalog availability key
- Reason codes

The integration should use contracts rather than sharing EF entities. A new or amended data-sharing agreement is required when RetailEdge sends customer-linked identifiers or cart behavior to an external recommendation provider. The agreement should cover purpose, retention, access controls, deletion, subprocessors, encryption, incident notification, and audit requirements.

Prefer an in-region or pseudonymous contract. A cart ID alone is safer than a raw customer ID, but stable identity can still create linkage; use the privacy team's approved key.

## Migration Plan

1. Add nullable columns and the `RecoveryTouchpoint` table.
2. Generate and review migration SQL.
3. Deploy the migration during low traffic, or deploy additively through the normal schema rollout.
4. Deploy backward-compatible code that can read old and new rows.
5. Backfill existing open carts in small batches.
6. Schedule follow-ups only for eligible, consented carts and avoid automatic messages for expired or converted carts.
7. Deploy recommendation integration behind a feature flag.
8. Monitor migration, touchpoint, provider, delivery, opt-out, and conversion metrics.
9. Roll back code or disable the feature flag if issues occur.
10. Do not delete old columns until data is reconciled and a retention period is approved.

Existing abandoned cart records are recoverable and should not be marked converted or expired merely because of the migration. Historical records may remain without follow-up.

## Recommendation

**Phase the change; negotiate the "higher discount" policy separately.**

Recommended rollout:

1. Add follow-up touchpoint persistence and scheduling.
2. Integrate consent, suppression, and one-time voucher redemption.
3. Launch recommendation-engine integration as a fallback-capable enhancement.
4. Run a low-volume A/B test.
5. Negotiate the exact higher-discount amount/percent, frequency, eligible margin tier, and target jurisdictions before release.

Accepting as-is risks margin, duplicate messaging, PII transfer, and sending to customers whose conversion or opt-out state changed. It should not proceed as a zero-risk extension of the initial cart feature.

## How Copilot Assisted This Analysis

- **Prompt:** I asked Copilot to inspect the existing cart model, EF context, abandonment and recovery services, API controller, migrations, and tests, then assess the scope change across the full workflow.
- **Feature used:** Agent/Chat Mode with workspace-aware file context and `#file` references.
- **What it produced:** A cross-file impact map, migration fields, touchpoint entity proposal, effort/risk estimates, test cases, data-contract proposals, and rollout recommendations.
- **Validation performed:** I checked the findings against the existing `AbandonedCart`, `CartScanCursor`, `RecoveryTrackingService`, `CartController`, and migration definitions.
- **Overrides and corrections:** I did not accept generic recommendations to share raw customer PII or make direct provider calls inside domain code. I separated the recommendation provider behind an interface and explicitly included consent, pseudonymous identifiers, fallback behavior, migration backfill, and service-level race controls.
