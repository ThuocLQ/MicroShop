# Commercial Payments And Fulfillment Specification

- Feature ID: COMM-VN-COMMERCE-2026-09
- Status: Proposed - Ready gate in progress
- Owners: Product/BA, Solution Architect, Payment Lead, Fulfillment Lead, Security, QA, SRE
- Depends on: `canonical-system-design.md`, authenticated P0 checkout, PaymentService webhook/outbox, Ordering fulfillment aggregate

## 1. Outcome

Enable a Vietnamese commercial checkout with real, server-confirmed payment and carrier-managed delivery while preserving the current ownership boundaries:

- **MoMo** is a hosted/redirect wallet payment. Its IPN callback is authoritative.
- **SePay** is a bank-transfer/QR reconciliation method. A matched inbound transaction webhook is authoritative.
- **Giao Hang Tiet Kiem (GHTK)** creates and tracks a parcel after the order reaches the permitted fulfilment state.

Portfolio and Development continue to use `Sandbox`. A real provider is never silently enabled by default.

## 2. Scope

### In scope

1. Customer selects Sandbox, MoMo, or SePay only when that method is enabled for the environment and order currency is VND.
2. PaymentService creates MoMo payment actions, verifies signed MoMo IPN callbacks, and drives the existing payment/order saga through the transactional outbox.
3. PaymentService creates a unique bank-transfer reference for SePay, returns a server-created QR/payment instruction, verifies SePay webhook authentication, and matches one inbound transaction to one payable payment.
4. OrderingService creates a GHTK shipment request from an eligible paid order, records the carrier label/tracking/fee snapshot, and applies carrier status updates idempotently.
5. Operations can see payment and shipment reconciliation failures; they cannot overwrite provider or carrier state with generic CRUD.
6. The Storefront shows provider action/instructions, pending, failed, expired, paid, carrier tracking, and recovery states from server-confirmed data.

### Explicitly out of scope

- Card data collection or PCI card vaulting.
- COD, split payment, split shipment, multi-warehouse allocation, partial refund, return/restock, cross-border shipment, and a public carrier management portal.
- Automatic re-shipment after a carrier failure without an explicit operations decision.
- Treating redirect return, QR display, or a browser polling response as payment truth.

## 3. Bounded Context And Ownership

| Owner | Responsibility | Must not own |
| --- | --- | --- |
| OrderingService | Order amount/address snapshots, fulfilment policy, shipment aggregate, GHTK request orchestration | Provider transaction lifecycle, raw provider credentials |
| PaymentService | Payment intent, MoMo/SePay provider adapters, webhook/inbox/outbox/reconciliation | Order status direct mutation |
| IdentityService | Customer identity and owned delivery address | Carrier address normalization or shipment status |
| Storefront BFF | Session-bound presentation of server DTOs | Payment confirmation, signing, provider secrets |
| GHTK adapter | Carrier request/response/status mapping | Order business transition policy |

No new `ShippingService` is introduced in this phase. Extract one only when carrier/rate/warehouse/returns ownership becomes independently complex.

## 4. Payment Policy

### Common rules

- Currency is `VND`; amount is an integer VND value at the provider boundary. The authoritative decimal order/payment amount must have no fractional VND before an action is created.
- A customer may start an enabled method only for an owned order in a payable state and before its payment deadline.
- The provider-facing order/reference value is deterministic, bounded, opaque to customers, and unique. It never contains PII.
- `PaymentId`, provider event id, provider transaction id, payload hash, correlation id, and order id are persisted/audited as appropriate. Raw secrets, access keys, card data, and full unredacted provider payloads are not logged.
- Every inbound notification is authenticated before applying state. Duplicate notifications return a provider-compatible success response without duplicate side effects.
- Unknown, mismatched amount/currency/reference, late, or invalid state notifications are persisted for reconciliation and do not mark an order paid.

### MoMo

1. `MoMoPaymentProvider` creates a server-to-server payment request using a configured endpoint and HMAC-SHA256 signature.
2. The client receives only the provider action URL and an opaque payment id; `redirectUrl` is a user-experience return, while `ipnUrl` is the authoritative result path.
3. IPN validation checks the exact signature canonicalization required by the enabled MoMo API, partner code, order/request identity, amount, result code, and replay identity.
4. The initial release uses the product/payment method agreed with the merchant account. Capture/void/refund capability is enabled only where that MoMo product supports it; unsupported operations enter a visible manual reconciliation workflow rather than faking provider success.

### SePay

1. `SePayPaymentProvider` creates a payment instruction with a unique transfer code tied to one payment. QR rendering is derived from server-issued bank account/reference/amount data only.
2. The SePay webhook accepts inbound credits only. It validates the configured authentication method and deduplicates by SePay transaction `id`.
3. A transaction matches only when the configured receiving account, transfer direction, transfer code/reference, exact VND amount, and payment state are valid. Partial, overpaid, ambiguous, or unmatched transfers become reconciliation cases.
4. The initial release does not initiate a debit through SePay and does not infer payment from a customer-uploaded receipt.

## 5. Fulfilment And GHTK Policy

1. A shipment may be submitted to GHTK only for a `Paid` order with a valid immutable delivery-address snapshot and fulfiller-approved shipment data.
2. The partner order id sent to GHTK is deterministic and unique per shipment. An `ORDER_ID_EXIST` response is reconciled against the stored shipment instead of creating another parcel.
3. The adapter records GHTK label, tracking id, quoted fee, insurance fee, estimated pickup/delivery times, request identity, and normalized carrier status history.
4. Carrier webhooks are authenticated according to the contracted GHTK mechanism and deduplicated by provider event identity plus payload hash. If webhook capability is not enabled on the merchant account, a scheduled tracked-order reconciliation job is used temporarily and its lag is monitored.
5. GHTK lifecycle is mapped explicitly; unsupported/ambiguous carrier statuses remain carrier-visible operational events and do not force an invalid Order status transition.
6. Customer tracking is read-only and comes from persisted shipment status. Operations controls are limited to allowed actions such as create shipment, retry a failed carrier submission, and cancel before carrier pickup where the carrier supports it.

## 6. API And Event Contract Direction

### Public/BFF APIs

- `GET /payments/providers`: enabled customer-visible provider descriptors only.
- `POST /orders/{orderId}/payments`: existing create flow gains `provider = Momo | SePay | Sandbox` with a stable action DTO.
- `GET /payments/{paymentId}` and owned order detail expose state, action expiry, next permitted action, and sanitized reconciliation state.
- `GET /orders/{orderId}/shipment`: owned tracking projection, never raw carrier payload.

### Internal contracts

- Provider-specific webhook endpoints stay outside normal customer auth but behind exact route allow-list, dedicated rate limits, body-size limit, signature verification, and audit logging.
- `PaymentAuthorized`, `PaymentCaptured`, `PaymentFailed`, `PaymentVoided`, and `PaymentRefunded` continue through existing outbox/inbox contracts. Provider names and provider ids are metadata, not new order state owners.
- Add `ShipmentSubmissionRequested`, `ShipmentSubmitted`, `ShipmentCarrierStatusChanged`, and `ShipmentSubmissionFailed` integration events only after event version/consumer impact is reviewed.

## 7. Persistence And Migration Direction

### PaymentService

- Keep the generic `Payments` aggregate. Add provider-safe fields only when needed: provider request id, merchant reference, provider status code, action payload version, reconciliation state/reason, and timestamps.
- Add immutable webhook receipt/audit data with unique provider/event identifiers and payload hash. Retention/redaction policy must be explicit.

### OrderingService

- Extend shipment persistence with carrier submission state, GHTK label/tracking id, fee/insurance VND snapshots, submission idempotency key, last carrier status time, and a normalized append-only shipment status history.
- Schema changes are DbUp forward-only migrations. New nullable/read-compatible fields ship before application code starts requiring them.

## 8. Security And Operations

- Credentials are environment secrets only. Local uses ignored secret files; Kubernetes uses Secret or approved secret manager. No provider credential, QR bank account secret, or webhook token is committed.
- Production requires HTTPS public callback URLs, trusted proxy configuration, exact CORS origin, rate limits, request-size limits, and clock synchronization.
- Metrics: provider action create failures/latency, webhook accepted/rejected/duplicate/unmatched, payment reconciliation age, carrier submission failures, carrier webhook failures, shipment age by status, and GHTK polling lag when enabled.
- Runbooks: invalid/duplicate callback, amount mismatch, callback outage, provider API outage, GHTK duplicate partner order id, carrier status stuck, refund/void unsupported, and manual reconciliation closure.

## 9. Delivery Slices And Acceptance

| Slice | Scope | Exit evidence |
| --- | --- | --- |
| C1 - Provider framework | Multi-provider config, feature flags, provider capability model, secret validation, shared webhook hardening | Unit/integration tests; Sandbox unchanged; disabled real providers cannot be selected |
| C2 - MoMo sandbox | Create redirect action + signed IPN verifier + dedup/reconciliation | MoMo sandbox happy, invalid signature, duplicate, amount mismatch, late callback and outage tests |
| C3 - SePay test mode | Bank-transfer instruction/QR + authenticated transaction webhook + exact matching | SePay test-mode inbound credit, duplicate id, wrong account/code/amount, retry and reconciliation tests |
| C4 - GHTK staging | Shipment submission/reconciliation + status mapping + tracking read model | GHTK staging create/idempotent duplicate/status update/error tests; shipment audit visible |
| C5 - Storefront/Operations | Provider selection, payment return/pending states, tracking and reconciliation queues | Browser E2E desktop/mobile; no secret/raw payload in UI |
| C6 - Commercial readiness | Merchant onboarding, public callbacks, alert routing, backup/restore, controlled live canary | Signed release evidence, production runbooks, manual reconciliation owner, rollback plan |

## 10. Ready Gate Inputs Required From The Merchant

Implementation can start with C1 immediately. Enabling C2-C4 beyond test/staging needs:

1. MoMo merchant product selection, sandbox credentials, production credentials, allowed redirect/IPN domains, and confirmation of capture/void/refund capabilities.
2. SePay account/VA model, test-mode or production token, receiving account identifiers, webhook authentication method, payment code policy, and settlement/reconciliation owner.
3. GHTK merchant account, staging/production token, partner code, warehouse/pickup and return address, service coverage, webhook arrangement, COD policy, and carrier support escalation contact.
4. Legal/finance decisions: invoice/tax policy, refund approval authority, bank transfer overpayment/underpayment policy, notification content, support SLA, and privacy retention.

## 11. Definition Of Done

No real method is labelled production-ready until its configured environment passes provider sandbox/staging tests, duplicate/invalid/late/outage drills, observability checks, browser E2E, security review, reconciliation runbook, and release evidence required by `docs/governance/quality-gates.md`.
