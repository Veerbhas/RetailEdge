# PR Description

## Summary

Built the Cart Abandonment Recovery System on top of the remediated Inventory module to identify inactive carts and convert lost purchase intent. The implementation includes abandoned-cart modeling, 30-minute batch detection, restartable cursors, decimal voucher evaluation, notifications, 24-hour recovery conversion, expiry, and state-transition logging.

The system also provides admin-only scan and analytics endpoints, owner-scoped recovery status, and cursor-based analytics pagination.

## AI Tool Disclosure

Copilot features used:

- Agent Mode for cross-file implementation
- Edit Mode for targeted corrections
- Ask/Chat Mode for requirement and code clarification
- `#file` context for exact source references
- Workspace-aware context for repository-wide consistency
- `/tests` and test execution for validation
- `/explain` for controller behavior review
- `.github/copilot-instructions.md` for persistent repository rules

AI output was accepted where it matched repository conventions, especially for C# 12 structure, EF Core mappings, service interfaces, and test organization. It was overridden for:

- Replacing incorrect `Cancelled` cart status with `Abandoned`
- Adding UTC-tick fields for SQLite-compatible filtering and ordering
- Fixing cursor initialization and keyset pagination
- Requiring explicit BOLA owner matching
- Returning machine-readable `403` errors
- Ensuring all currency fields use `decimal`

Estimated code/documentation contribution:

- AI-generated: approximately 75-85%
- Hand-written or manually reviewed and corrected: approximately 15-25%

## Security Note

JWT authentication was reviewed against the existing security implementation rather than trusted solely as AI output:

- JWT validation is pinned to RS256 or HS256.
- Unsigned tokens and unsupported algorithms are rejected.
- Signature, issuer, audience, `exp`, numeric `iat`, and `nbf` are validated.
- JWT tests cover unsigned, expired, future-issued, and wrong-algorithm tokens.

Write operations use allowlisted contracts or service parameters rather than directly binding request bodies to domain entities. Clients cannot assign ownership, status, totals, timestamps, or versions.

Broken Object Level Authorization compares the trusted `customer_id`, `sub`, or name-identifier claim with `Cart.CustomerId`. Cross-owner access returns `403 CART_ACCESS_DENIED`, and the recovery service independently verifies order ownership.

## Testing Coverage

Verified scenarios include:

- Cart inactivity at and below the 30-minute threshold
- Voucher generation above the decimal threshold
- No voucher at or below the decimal threshold
- Conversion within 24 hours
- Expiry at and after the 24-hour boundary
- Most-recent-cart notification priority
- Customer A attempting to access Customer B's cart
- Non-admin scan-trigger request receiving `403`
- Cursor-based analytics pagination
- Duplicate conversion protection
- Optimistic-concurrency conflict handling

The complete test suite passed:

- 24 passed
- 0 failed
- 0 skipped

### Known Gaps

- The notification publisher is currently a durable contract/logging implementation rather than a production email or push provider.
- Voucher codes are deterministic placeholders rather than cryptographically generated, database-redeemed vouchers.
- HTTP integration tests could be expanded to run middleware and JWT authentication end-to-end.

## Risk or Trade-off

SQLite is used for EF persistence, with companion UTC-tick fields for filtering and keyset ordering. This avoids SQLite `DateTimeOffset` LINQ limitations and makes cursor behavior deterministic, but it adds duplicated timestamp storage that domain transitions must keep synchronized. Provider-specific migration and timestamp behavior should be reviewed before moving to a different database engine.

## Self-Review Checklist

- [x] Cart totals and unit prices use `decimal`
- [x] No currency field uses `float` or `double`
- [x] Abandonment threshold is configurable and defaults to 30 minutes
- [x] Detection scan uses configurable independent batches
- [x] Detection and analytics use keyset cursor ordering
- [x] One-time voucher behavior is idempotent within the scan
- [x] Recovery expires after 24 hours
- [x] Converted and expired states cannot be overwritten incorrectly
- [x] Cross-owner cart access returns `403`
- [x] Non-admin scan and analytics access returns `403`
- [x] State transitions are persisted
- [x] ORM indexes are declared for scan and recovery queries
- [x] Project and tests build successfully
- [ ] Production notification provider is still required
- [ ] Production voucher redemption store is still required
