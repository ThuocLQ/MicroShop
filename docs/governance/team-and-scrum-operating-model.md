# MicroShop Team And Scrum Operating Model

## Purpose

This document assigns named project-role aliases so every delivery item has an accountable decision maker, a delivery owner, and a reviewer. These names are planning aliases, not a claim that separate people have been hired. A real person must be named before an external provider account, finance decision, or production release is approved.

## Team

| Alias | Role | Primary accountability | Backup / reviewer |
| --- | --- | --- | --- |
| ThuocLQ | Dev Lead / Engineering Lead | Technical delivery, architecture fit, API/event compatibility, code review, merge approval, technical release sign-off | Quang Huy |
| Mai Anh | Product Owner / BA | Customer problem, scope, acceptance criteria, payment and fulfilment policy, stakeholder acceptance | ThuocLQ for technical feasibility |
| Khang Nguyen | Scrum Master / Delivery Manager | Sprint health, board hygiene, impediment escalation, ceremony facilitation, delivery metrics | Mai Anh |
| Quang Huy | Solution Architect | Bounded contexts, ADRs, data ownership, service contracts, scalability and migration strategy | ThuocLQ |
| Thanh Dat | Backend Payment Engineer | PaymentService, PayPal/SePay adapters, webhook verification, reconciliation and payment tests | ThuocLQ |
| Gia Bao | Backend Fulfilment Engineer | OrderingService fulfilment policy, GHTK adapter, shipment idempotency and tracking contracts | Quang Huy |
| Lan Chi | Frontend / UX Lead | Storefront journeys, accessible payment/tracking states, real API usage and browser acceptance | Mai Anh |
| Khanh Linh | QA Lead | Acceptance matrix, API/browser E2E, duplicate/failure regression suite and release evidence | ThuocLQ |
| Huu Phuc | SRE / DevOps | Local-prod, CI/CD, container health, logs/metrics, deployment, rollback and runbooks | ThuocLQ |
| Bao Tran | Security Engineer | Secrets, auth boundaries, webhook signatures, PII redaction, threat review and go-live approval | Quang Huy |
| Minh Chau | Finance / Operations Owner | Merchant/provider onboarding, settlement/refund policy, manual reconciliation and carrier operations | Mai Anh |

## Authority And Working Agreement

- **ThuocLQ is the Dev Lead.** No application, infrastructure, schema, event-contract, security-boundary, or release-impacting change is merged without ThuocLQ review.
- **Mai Anh owns what and why; ThuocLQ owns how and technical feasibility.** A scope conflict is resolved by Mai Anh. A safety, architecture, or release conflict is resolved by ThuocLQ.
- **Quang Huy writes/reviews an ADR** for breaking contracts, a new bounded context, cross-service data ownership, persistence technology changes, or an operationally significant trade-off.
- **Bao Tran must approve** webhook authentication, public callback exposure, credentials, payment-state transition changes, identity/authorization, or PII retention changes.
- **Khanh Linh owns evidence, not only test code.** A ticket cannot be marked Done without the evidence required by [quality-gates.md](quality-gates.md).
- **Huu Phuc owns environment evidence.** A deployment claim requires the immutable image/tag, health evidence, smoke result, and rollback target.
- Anyone may raise a defect or risk. The accountable owner must classify it as `blocker`, `critical`, `major`, or `normal` within one working day.

## Scrum Cadence

Use one-week sprints. A shorter sprint keeps the external-provider blockers visible while the product is still evolving.

| Event | Owner | Timebox | Required output |
| --- | --- | --- | --- |
| Backlog refinement | Mai Anh + Khang Nguyen | 45 minutes, mid-sprint | Stories meet Definition of Ready or have a named blocker |
| Sprint planning | Mai Anh + ThuocLQ + team | 60 minutes | Sprint goal, selected stories, owner/reviewer, acceptance evidence and risk list |
| Daily async stand-up | Khang Nguyen | Before 10:00 each workday | `Done / Next / Blocked / Need decision` in the project board |
| Technical design review | ThuocLQ + Quang Huy | Per material story | Contract/ADR decision, dependency classification and test strategy |
| Sprint review | Mai Anh + Khanh Linh | 45 minutes, last day | Demonstrated user flow using real data plus acceptance evidence |
| Retrospective | Khang Nguyen | 30 minutes, last day | One improvement owner and due date; no anonymous action items |

## Board And Ticket Standard

Use GitHub Issues/Projects as the source of work state. One user-visible or operational outcome equals one story. Do not use a catch-all implementation ticket.

Required fields for every story:

1. `ID` and concise outcome title.
2. Product owner, delivery owner, reviewer, and test owner.
3. Linked spec/ADR and acceptance criteria.
4. Dependency classification: `mock`, `local simulation`, `sandbox`, `code-ready`, or `live-verified`.
5. Explicit blocker, decision deadline, and external owner when applicable.
6. Test/release evidence links before moving to Done.

Workflow: `Backlog -> Ready -> In Progress -> Code Review -> Verify -> Ready for Release -> Done`.

- A blocked item stays in its current state and carries a `blocked` label; it must not be moved to Done because a simulator exists.
- Branches use `codex/<issue-id>-<short-topic>` unless the repository policy says otherwise.
- Pull requests name the linked issue, expected behavior, validation evidence, migration/rollback impact, and external-integration classification.
- No direct push to the release branch. ThuocLQ reviews technical changes; Khanh Linh confirms test evidence; Huu Phuc confirms deployment evidence where applicable.

## Current Work Allocation

| ID | Outcome | Accountable | Delivery owner | Reviewer / verifier | Status and next evidence |
| --- | --- | --- | --- | --- | --- |
| PAY-01 | Keep provider capability, currency policy and strict lifecycle transitions safe | ThuocLQ | Thanh Dat | Bao Tran + Khanh Linh | Done locally: build, focused payment `19/19`, full integration `111/111` |
| PAY-02 | Verify PayPal hosted checkout and webhook through a real Sandbox merchant | Mai Anh | Thanh Dat | Khanh Linh + Bao Tran | `code-ready`, blocked on Sandbox business/buyer accounts, client id/secret, webhook id and public HTTPS callback |
| PAY-03 | Deliver SePay VND transfer instruction, QR and authenticated reconciliation | Minh Chau | Thanh Dat | Bao Tran + Khanh Linh | `planned`, blocked on linked account/VA, HMAC configuration, transfer-code and mismatch policy |
| FUL-01 | Deliver GHTK staging shipment submission and tracking reconciliation | Minh Chau | Gia Bao | Khanh Linh + Huu Phuc | `planned`, blocked on staging token, partner code, pickup/return address and webhook agreement |
| FE-01 | Finish payment selection, return/pending/error recovery and browser E2E with real provider data | Mai Anh | Lan Chi | Khanh Linh | In progress; Sandbox and code-ready UI build pass, real provider E2E waits for PAY-02/PAY-03 |
| OPS-01 | Persist Data Protection keys and decide whether the optional Npgsql Kerberos warning needs an image package | ThuocLQ | Huu Phuc | Bao Tran + Khanh Linh | Done locally: Payment key persisted across restart, Gateway health `200`, and local Postgres connections set `GSS Encryption Mode=Disable`. Production follow-up: protect keys at rest with an approved secret store, KMS, or certificate. |

## External-Integration Gate

| Dependency | Current classification | Real-world owner action required before implementation is called integrated |
| --- | --- | --- |
| Sandbox provider | `local simulation` | None for current internal simulation; it must never be described as a bank or PayPal integration |
| PayPal | `code-ready` | A nominated merchant owner creates Sandbox business/buyer accounts, app credentials, webhook id and callback allow-list. Thanh Dat then runs the Sandbox checklist. |
| SePay | `planned` | A nominated finance/merchant owner supplies linked receiving account/VA, HMAC webhook configuration, test access and approved reconciliation rules. |
| GHTK | `planned` | A nominated operations owner supplies staging credentials, partner code, warehouse/return addresses, supported service and webhook agreement. |

No one may substitute fake credentials, a hard-coded success response, or a browser return for provider confirmation.

## Git Provenance And Automation Bots

- The existing CI runs as `github-actions[bot]` with the repository `GITHUB_TOKEN`. Its default permissions are read-only; only image-publishing jobs receive `packages: write`.
- Team aliases describe work ownership in Issues, pull requests, commit trailers, and release evidence. They are not Git users and must not be used as fabricated commit authors.
- Commit authors remain the real developer identity, normally ThuocLQ. Use trailers such as `Workstream-Owner: Thanh Dat (Payment Engineer)` and `Reviewed-by: ThuocLQ` to retain the delivery history.
- When a separate release actor is needed, create a private GitHub App named `microshop-release`. Limit it to the MicroShop repository and grant only `Contents: read/write`, `Pull requests: read`, `Issues: write` and `Deployments: write` if those APIs are actually automated. Store the App ID and private key only in GitHub Actions secrets; installation tokens are short-lived.
- Do not create one GitHub credential per team alias. Separate bot accounts are justified only when they have a distinct permission boundary: CI checks, release/deployment, or operational notifications.
## Definition Of Done For A Sprint Story

A story closes only when its acceptance criteria and the applicable gates in [quality-gates.md](quality-gates.md) pass. For payment, inventory, carrier and eventing stories this always includes duplicate/failure behavior, observability, and a documented manual recovery path.

For an external integration, the release note must use the exact classification above. Only `sandbox-verified` or `live-verified` may be called an integration.