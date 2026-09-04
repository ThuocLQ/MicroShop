# Commercial Payments And Fulfillment Delivery Board

Companion board for `commercial-payments-and-fulfillment-spec.md`. Work progresses in slices; no live credential is committed and no slice is marked done without its listed evidence.

## Team Decisions

| Role | Decision / accountability |
| --- | --- |
| Product / BA | Owns VND-only policy, method visibility, transfer mismatch policy, refund authority, COD exclusion, and customer wording. |
| Solution Architect | Keeps provider state in PaymentService and shipment policy in OrderingService; approves only backward-compatible contracts/migrations. |
| Payment Engineer | Owns MoMo and SePay adapters, webhook verification, durable reconciliation records, and provider test evidence. |
| Fulfillment Engineer | Owns GHTK adapter, carrier mapping, shipment idempotency, tracking history, and carrier reconciliation. |
| Security Engineer | Reviews secret loading, signature validation, callback exposure, PII redaction, replay defenses, and staff permissions. |
| Frontend / UX | Owns payment-method selection, action/QR/redirect/pending/retry states, customer tracking, and accessible error recovery. |
| QA | Owns provider simulators/test-mode cases, duplicate/late/outage matrix, browser E2E, and release evidence. |
| SRE / Operations | Owns callback routing, health/metrics/alerts, runbooks, merchant secret deployment, rollback, and reconciliation ownership. |

## Workstream C1 - Shared Foundation

| Item | Owner | Status | Evidence required |
| --- | --- | --- | --- |
| Provider capability/options model | Payment | Implemented | Resolver tests: disabled methods cannot resolve or appear in the customer provider list |
| VND amount validation | Payment | Implemented | Policy tests reject fractional VND and non-VND MoMo/SePay intents; exact provider reference formats are owned by C2/C3 adapters |
| Provider webhook route hardening | Payment + Security | Implemented | JSON-only bounded body, endpoint rate limit, signature processor, persisted webhook log/conflict handling |
| Generic reconciliation persistence | Payment + Operations | Existing and verified | `WebhookLogs` plus `WebhookEventConflicts` persist received/rejected/duplicate/conflicting notifications; C2/C3 add provider-specific operations views |
| Shipment submission state/history extensions | Fulfillment | C4 | Carrier-specific persistence begins with the actual GHTK adapter contract; do not add an unused submission abstraction in C1 |

## Workstream C2 - MoMo

| Item | Owner | Status | Evidence required |
| --- | --- | --- | --- |
| Merchant product/capability confirmation | Product + Payment | Blocked on merchant input | Written confirmation for authorize/capture/void/refund behavior |
| Sandbox payment create adapter | Payment | Implemented, disabled by default | Contract fixture + signed request test pass; sandbox action smoke remains blocked on merchant credentials |
| IPN verifier and dedup/reconciliation | Payment + QA | Implemented, awaiting sandbox validation | valid/invalid/duplicate/wrong amount unit coverage; late-event scenarios require sandbox evidence |
| Storefront return/pending/retry experience | Frontend + QA | Planned after action contract | Browser E2E proving return is not success |

## Workstream C3 - SePay

| Item | Owner | Status | Evidence required |
| --- | --- | --- | --- |
| Bank account/VA and transfer-code policy | Product + Finance | Blocked on merchant input | Exact match and over/underpayment decision record |
| Test-mode instruction/QR adapter | Payment | Blocked on test credentials | No PII/provider secret reaches browser; QR instruction is server-confirmed |
| Authenticated inbound credit webhook | Payment + Security + QA | Planned after credentials | account/code/amount/event-id mismatch and retry tests |
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

- No merchant-owned MoMo credentials/product capability confirmation.
- No SePay account or virtual-account/payment-code policy and test credentials.
- No GHTK merchant staging token, partner code, warehouse/return address, or webhook agreement.

These are intentionally configuration/onboarding blockers, not reasons to weaken the design with hard-coded secrets or fake production confirmations.

## C1 Completion Evidence (2026-09-04)

- Payment provider registry supports explicit enablement and does not expose disabled providers.
- MoMo/SePay policy rejects non-VND or fractional-VND intents before provider action creation.
- Payment webhook endpoints now enforce JSON content, a bounded body, and service-level fixed-window rate limits; existing signature, idempotency, conflict persistence, metrics, and outbox behavior remain intact.
- dotnet build MicroShop.sln --no-restore --nologo -v minimal passed.
- dotnet test Tests/MicroShop.IntegrationTests/MicroShop.IntegrationTests.csproj --no-build --nologo -v minimal passed: 99/99.

## C2 Code Evidence (2026-09-04)

- `MoMoPaymentProvider` creates a signed VND-only `payWithMethod` action and persists the hosted checkout reference through the existing payment-action flow.
- `/webhooks/momo` is registered only when MoMo is explicitly enabled. Its IPN verifier uses a constant-time HMAC comparison, resolves the stored provider session, validates partner/currency/amount, records rejected notifications, and uses existing webhook idempotency persistence.
- A successful auto-capture IPN transitions the payment through `Authorized` and then `Captured`; the aggregate now explicitly permits a verified provider auto-capture after authorization, but still rejects direct pending-to-captured transitions.
- MoMo remains disabled in committed configuration. Enabling it requires secret-backed `PartnerCode`, `AccessKey`, `SecretKey`, HTTPS redirect/IPN URLs, and sandbox merchant confirmation.