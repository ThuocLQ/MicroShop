# Delivery Process And Roles

## Delivery lifecycle

`Discover -> Specify -> Design -> Ready -> Build -> Verify -> Release -> Observe -> Learn`

No step may be skipped. A discovered idea is not a build task until the Ready gate passes.

## Required feature specification

Every feature has one issue or spec section containing:

- Problem, persona and measurable outcome.
- In-scope and explicitly out-of-scope behavior.
- Happy path, validation, authorization, retry/idempotency, timeout and failure paths.
- Data ownership, API/event contract, persistence/migration impact.
- UI screen/state matrix: loading, empty, unauthorized, validation error, unavailable, success.
- Acceptance criteria and test evidence required for release.
- Observability: correlation fields, metric, audit/event requirement, runbook impact.
- Rollback or feature-flag plan when behavior is risky.
- For a third-party integration: provider ownership, required credentials, callback/domain prerequisites, environment scope (`mock`, `sandbox`, or `live`), and the evidence needed to claim it works. A placeholder or simulator must be labelled and must not be presented as a completed external integration.

## Decision roles

| Decision | Accountable | Responsible | Consulted |
| --- | --- | --- | --- |
| Scope and acceptance | Product owner / BA | BA | Customer support, Engineering lead |
| Service boundary and data ownership | Solution architect | Engineering lead | Domain owners |
| UX journey and state matrix | Product designer | Frontend lead | BA, support |
| API/event/schema | Engineering lead | Backend owner | Architect, QA |
| Test strategy and release evidence | QA lead | Developers | SRE/operations |
| Security/payment approval | Security owner | Engineering lead | Payment/operations owner |
| Production release/rollback | Release owner | SRE/operations | QA, engineering lead |

For this repository, one person can fill multiple roles, but each decision must still have an explicit accountable role.

## Change control

- New requirement after Ready: update the spec and re-run Ready; do not silently add it to an implementation batch.
- Breaking API/event/schema changes require an ADR and compatibility plan.
- Financial, identity, authorization and inventory changes require security and failure-path review.
- A UI-only change may not create a new business state; it must use an existing approved contract.
- A feature that fails release evidence returns to Verify, not to Done.