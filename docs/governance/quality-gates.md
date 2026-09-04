# Quality Gates

## Definition of Ready

A feature may start only when all are true:

- [ ] User problem, persona, scope and acceptance criteria are written.
- [ ] Domain owner and authorization policy are named.
- [ ] API/event/schema contract is reviewed; compatibility is clear.
- [ ] UI state matrix and real data source are defined.
- [ ] Failure, duplicate, timeout and compensation behavior are defined.
- [ ] Test plan and release evidence are defined.
- [ ] Third-party dependencies are classified as mock, sandbox, or live. Required merchant/account access, secrets, callback URLs, and external acceptance evidence are explicit; no fake adapter is used to conceal a blocker.

## Definition of Done

- [ ] Implementation respects architecture boundaries and does not leak infrastructure into API/Application.
- [ ] Migration is versioned, forward-only and reviewed where persistence changed.
- [ ] Authorization, input validation, idempotency and stable errors are covered.
- [ ] Unit/integration tests cover success plus relevant duplicate/failure paths.
- [ ] Customer/operations UI uses real API data and covers loading, empty, error and unauthorized states.
- [ ] Logs/traces contain correlation and business identifiers; sensitive data is excluded.
- [ ] Documentation/ADR/runbook is updated only when the feature changes a contract or operation.

## Release gate

- [ ] CI build, tests, frontend lint/build, migration naming, compose and Helm validation pass.
- [ ] Target environment health/readiness pass.
- [ ] Critical smoke flow passes through Gateway/BFF, not direct service URLs.
- [ ] Migration upgrade and rollback behavior are understood.
- [ ] Release owner records deployed image/tag and rollback target.
- [ ] For financial changes: provider sandbox/live webhook, duplicate callback and reconciliation drill pass.
- [ ] A third-party integration is described truthfully in release notes: code-ready, sandbox-verified, or live-verified. Only the latter two may be called integrated.

## Evidence matrix

| Change type | Minimum evidence |
| --- | --- |
| UI | Screenshot/browser flow plus frontend build/lint |
| API | Contract tests, auth/error tests, OpenAPI verification where enabled |
| Database | Migration test, upgrade evidence, query/index review |
| Eventing | Outbox/inbox duplicate and restart test, correlation trace |
| Payment/Inventory | Integration test for duplicate, timeout, compensation, and manual failure drill |
| Deployment | Compose/Helm validate, health smoke, rollback command |

Existing CI is a baseline gate, not proof of product readiness. Manual acceptance and runtime smoke remain mandatory.

## Release Tag Policy

- Work commits use a scoped conventional message. Historical learning tags are retained; new deployable releases use annotated SemVer tags such as `v0.1.0`.
- A release tag is created only from `main` after CI, image build, compose/Helm validation, and the applicable smoke evidence pass.
- The manual Create MicroShop Release workflow validates the SemVer tag and that its commit is the current main HEAD, then uses the short-lived microshop-release[bot] GitHub App token to create the tag and release. The operator must provide CI and smoke-test evidence; the workflow does not deploy an environment.
- GHCR deployment resolves a mutable environment tag to an immutable `main-<commit-sha>` image digest. The release record contains the Git tag, commit SHA, image digests, migration version, smoke evidence, and rollback tag.
- Rollback selects the previous verified immutable image tag; database migrations are forward-only and are not rolled back automatically.
