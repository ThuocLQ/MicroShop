# Commercial Payments And Fulfillment Delivery Board

Companion board for `commercial-payments-and-fulfillment-spec.md`. Work progresses in slices; no live credential is committed and no slice is marked done without its listed evidence.

## Team Decisions

| Role | Decision / accountability |
| --- | --- |
| Product / BA | Owns currency/method visibility, transfer mismatch policy, refund authority, COD exclusion, and customer wording. |
| Solution Architect | Keeps provider state in PaymentService and shipment policy in OrderingService; approves only backward-compatible contracts/migrations. |
| Payment Engineer | Owns PayPal and SePay adapters, webhook verification, durable reconciliation records, and provider test evidence. |
| Fulfillment Engineer | Owns GHTK adapter, carrier mapping, shipment idempotency, tracking history, and carrier reconciliation. |
| Security Engineer | Reviews secret loading, signature validation, callback exposure, PII redaction, replay defenses, and staff permissions. |
| Frontend / UX | Owns payment-method selection, action/QR/redirect/pending/retry states, customer tracking, and accessible error recovery. |
| QA | Owns provider simulators/test-mode cases, duplicate/late/outage matrix, browser E2E, and release evidence. |
| SRE / Operations | Owns callback routing, health/metrics/alerts, runbooks, merchant secret deployment, rollback, and reconciliation ownership. |

## Workstream C1 - Shared Foundation

| Item | Owner | Status | Evidence required |
| --- | --- | --- | --- |
| Provider capability/options model | Payment | Implemented | Resolver tests: disabled methods cannot resolve or appear in the customer provider list |
| Currency capability policy | Payment | Implemented | Payment creation enforces the provider's backend-owned currency allow-list. Sandbox advertises `*`, PayPal reads a validated Checkout-currency allow-list (default `USD`), and Vietnamese providers advertise `VND`. |
| Provider webhook route hardening | Payment + Security | Implemented | JSON-only bounded body, endpoint rate limit, signature processor, persisted webhook log/conflict handling |
| Generic reconciliation persistence | Payment + Operations | Existing and verified | `WebhookLogs` plus `WebhookEventConflicts` persist received/rejected/duplicate/conflicting notifications; C2/C3 add provider-specific operations views |
| Shipment submission state/history extensions | Fulfillment | C4 | Carrier-specific persistence begins with the actual GHTK adapter contract; do not add an unused submission abstraction in C1 |

## Workstream C2 - PayPal

| Item | Owner | Status | Evidence required |
| --- | --- | --- | --- |
| Sandbox business/buyer accounts and capability confirmation | Product + Payment | Blocked on account setup | Sandbox app, client id/secret, webhook id, return/cancel/webhook domains, and authorization/capture/void/refund behaviour are recorded in the merchant test register |
| Hosted order create adapter | Payment | Code-ready, disabled by default | Existing server-side order-create action uses a stable request id; add PayPal currency allow-list and request/response contract fixtures before enablement |
| Webhook verifier and dedup/reconciliation | Payment + QA | Code-ready, awaiting Sandbox validation | valid/invalid/duplicate/wrong payment-or-currency/late event tests plus a real PayPal dashboard resend evidence |
| Storefront provider selection, return/pending/retry experience | Frontend + QA | In progress | Only enabled/currency-compatible providers are delivered by the authenticated BFF. Browser E2E is not complete until a Sandbox provider is enabled and its real approval/return/webhook lifecycle passes. |

## Workstream C3 - SePay

| Item | Owner | Status | Evidence required |
| --- | --- | --- | --- |
| Bank account/VA and transfer-code policy | Product + Finance | Blocked on merchant input | Exact match and over/underpayment decision record |
| Test-mode instruction/QR adapter | Payment | Planned after onboarding | No PII/provider secret reaches browser; QR instruction is server-confirmed, VND-only, and carries a one-payment transfer code |
| Authenticated inbound credit webhook | Payment + Security + QA | Planned after onboarding | HMAC-SHA256 plus timestamp validation; account/code/amount/event-id mismatch, duplicate, replay, and retry tests |
| Reconciliation queue and operations workflow | Operations + Payment | Planned | Audit trail and closure reason; no direct payment status edit |

## Workstream C4 - GHTK

| Item | Owner | Status | Evidence required |
| --- | --- | --- | --- |
| Merchant warehouse/return/service configuration | Operations + Product | Blocked on merchant input | Valid pickup/return address and service coverage recorded |
| Staging create-order adapter | Fulfillment | Blocked on staging credentials | create, duplicate partner id, rejection and retry tests |
| Carrier event/poll reconciliation | Fulfillment + SRE | Planned after webhook agreement | dedup/order mapping/status monotonicity/lag metrics |
| Storefront tracking and operations exception views | Frontend + QA | Planned after tracking contract | Browser E2E; no fabricated tracking state |

## Ready And Release Checkpoints

1. **C1 Ready:** security/architecture review of options, route model, persistence and failure policy.
2. **Provider Ready:** merchant test credentials and callback domains are stored in the secret target; provider product capabilities are documented.
3. **Staging Ready:** public HTTPS callback endpoint, alert routing and a named reconciliation operator exist.
4. **Commercial Ready:** all quality gates pass, finance approves reconciliation/refund procedures, and a controlled canary/rollback plan is signed.

## Current Blockers

- No PayPal Sandbox business/buyer accounts, Sandbox app credentials, webhook id, or merchant capability confirmation.
- No SePay linked account/VA, HMAC configuration, payment-code policy, or test credentials.
- No GHTK merchant staging token, partner code, warehouse/return address, or webhook agreement.

These are intentionally configuration/onboarding blockers, not reasons to weaken the design with hard-coded secrets or fake production confirmations.

## C1 Completion Evidence (2026-09-04)

- Payment provider registry supports explicit enablement and does not expose disabled providers.
- MoMo/SePay policy rejects non-VND or fractional-VND intents before provider action creation. PayPal uses a validated, explicit currency allow-list before it may be enabled.
- Payment webhook endpoints now enforce JSON content, a bounded body, and service-level fixed-window rate limits; existing signature, idempotency, conflict persistence, metrics, and outbox behavior remain intact.
- dotnet build MicroShop.sln --no-restore --nologo -v minimal passed.
- dotnet test Tests/MicroShop.IntegrationTests/MicroShop.IntegrationTests.csproj --no-build --nologo -v minimal passed: 99/99.

## PayPal Code Baseline (2026-09-05)

- `PayPalPaymentProvider` creates a server-side `v2/checkout/orders` action with a stable `PayPal-Request-Id`, stores the provider session through the existing action flow, and returns only a validated HTTPS PayPal approval URL.
- `/webhooks/paypal` is registered only when PayPal is explicitly enabled. Its processor calls PayPal's webhook-signature verification endpoint, resolves the persisted payment via `custom_id` or related PayPal order id, and hands idempotency/conflict handling to the existing webhook persistence flow.
- The code maps authorization/capture/void/refund/denied lifecycle events, but has not been exercised with a PayPal Sandbox account. Therefore this is **code-ready**, not sandbox-verified.
- PayPal remains disabled in committed configuration. Enabling it requires secret-backed ClientId, ClientSecret, WebhookId, HTTPS return/cancel routes, a strict supported-currency allow-list, and Sandbox evidence.

## C2 Code-Ready Evidence (2026-09-06)

- PayPal order-create and signature-verification HTTP contracts are tested through an in-memory handler: server-derived amount/currency, deterministic request id, opaque identifiers, approval URL validation, and PayPal webhook-id forwarding are verified without a network call.
- Unsupported `VND` is rejected before the PayPal provider sends any HTTP request. Invalid PayPal webhook authentication is mapped at the API boundary to `401`, while malformed verified bodies return a controlled `400`.
- `compose.local-prod.yml` now defaults explicitly to Sandbox with `AllowSandbox=true`. PayPal requires the deliberate triple switch: `MICROSHOP_PAYMENT_PROVIDER=PayPal`, `MICROSHOP_PAYPAL_ENABLED=true`, and non-placeholder secret values. Configuration fails fast otherwise.
- Focused C1/C2 tests passed: `19/19`; `PaymentService` build passed with zero warnings and errors. The set includes strict capture ordering, authenticated provider auto-capture, PayPal order creation, signature verification, and duplicate-event behavior. No external PayPal request was made.
- Docker-backed full integration verification passed: `111/111`. PayPal Sandbox validation and dashboard resend evidence remain blocked only on merchant onboarding.
## C1 Currency Capability Evidence (2026-09-06)

- `IPaymentProvider` now owns `SupportedCurrencies`; the resolver exposes that same metadata to Storefront and `CreatePaymentHandler` enforces it before a provider action is created.
- PayPal uses a configured, validated subset of current PayPal Checkout currencies with `USD` as the default. `VND` is rejected before any PayPal API request. Sandbox uses `*`; SePay/MoMo remain whole-VND-only.
- Storefront filters only backend-supplied capabilities, so Sandbox no longer disappears because its currency metadata was empty. Payment return polling now has valid React hook dependencies and no longer has a parse error.
- Focused C1 tests passed: `14/14`. Storefront lint completed with `0` errors and its production build passed.
- Docker-backed full integration suite passed: `111/111` on 2026-09-06. This verifies the current payment, ordering, projection, and persistence contracts locally; it does not replace real provider Sandbox evidence.
## Provider Transition Note

- MoMo code is retained disabled for backwards compatibility while the PayPal + SePay path is delivered. It is not a selected provider, is not included in commercial readiness, and must not be shown in Storefront.
- Official PayPal and SePay documentation define the acceptance contract. Community samples are reference-only and require security/license review before reuse.
