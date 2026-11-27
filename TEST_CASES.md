# Unit Test Case Documentation

## Abandoned Cart Model

| Test | Location | Behavior |
|---|---|---|
| `CartModelUsesDecimalTotalAndRequiredIndexes` | `tests/CartModelTests.cs` | Verifies decimal totals, items, abandonment status, optimistic version, and required cart indexes. |

## Abandonment Detection

| Test | Location | Behavior |
|---|---|---|
| `CartInactiveForThirtyMinutesIsAbandoned` | `tests/AbandonmentDetectionTests.cs` | Abandoned exactly at 30 minutes. |
| `CartInactiveForLessThanThirtyMinutesIsNotAbandoned` | `tests/AbandonmentDetectionTests.cs` | Pending cart remains active below 30 minutes. |
| `VoucherIsGeneratedAboveDecimalThreshold` | `tests/AbandonmentDetectionTests.cs` | Voucher generated when cart total is above a decimal threshold. |
| `VoucherIsNotGeneratedAtOrBelowDecimalThreshold` | `tests/AbandonmentDetectionTests.cs` | No voucher at or below threshold. |
| `BatchPersistsRestartableKeysetCursor` | `tests/AbandonmentDetectionTests.cs` | Batch scan stores and completes a restartable keyset cursor. |
| `CustomerMultipleCartsOnlyLatestGetsNotification` | `tests/AbandonmentDetectionTests.cs` | Only the latest cart for a customer enters notification workflow. |

## Recovery Tracking

| Test | Location | Behavior |
|---|---|---|
| `ConvertsWithinTwentyFourHours` | `tests/RecoveryTrackingTests.cs` | Marks matching recovery converted at the 24-hour boundary. |
| `OwnerMismatchThrowsAuthorizationException` | `tests/RecoveryTrackingTests.cs` | Rejects order owner mismatches. |
| `OrderAfterWindowExpiresRecovery` | `tests/RecoveryTrackingTests.cs` | Expires an order outside the recovery window. |
| `ExpireRecoveriesMarksOpenCartsAtBoundary` | `tests/RecoveryTrackingTests.cs` | Expires open carts exactly at 24 hours. |
| `DuplicateConversionDoesNotRepeatTransition` | `tests/RecoveryTrackingTests.cs` | Duplicate completion events do not duplicate conversion transitions. |

## API Authorization and Pagination

| Test | Location | Behavior |
|---|---|---|
| `NonAdminScanReturnsMachineReadableForbidden` | `tests/CartApiTests.cs` | Non-admin scan returns `403` with `ADMIN_ROLE_REQUIRED`. |
| `CustomerACannotAccessCustomerBCartRecovery` | `tests/CartApiTests.cs` | Cross-customer recovery access returns `403` with `CART_ACCESS_DENIED`. |
| `AnalyticsUsesCursorPagination` | `tests/CartApiTests.cs` | Analytics page continuation uses `nextCursor`. |

## Inventory

Inventory tests cover store isolation, stock updates, stale optimistic versions, duplicate creates, invalid quantities, and JWT algorithm/lifetime validation.

Run all tests:

```powershell
dotnet test tests/RetailEdge.Tests.csproj
```
