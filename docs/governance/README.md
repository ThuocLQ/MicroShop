# MicroShop Delivery Baseline

## Design Authority

- [Canonical system design](../product/canonical-system-design.md) defines product scope, bounded contexts, owners, and capability boundaries.
- [Architecture decisions](../adr/README.md), especially ADR-004, record binding design decisions.
- This folder defines the delivery process and the proof required to claim work is complete.

This folder is the single source of truth for deciding whether a feature is ready to build, release, or operate.

Read in this order:

0. `../product/canonical-system-design.md` - binding product scope, bounded contexts, owners, and capability boundary.

1. `product-system-spec.md` - turns the canonical design into customer journeys, ownership checks, and a current release boundary.
2. `delivery-process.md` - roles, lifecycle, decision rights, and change control.
3. `team-and-scrum-operating-model.md` - named role aliases, current work allocation, Scrum cadence, and external-integration gates.
4. `quality-gates.md` - mandatory evidence for ready, done, release, and incident follow-up.

A feature is not accepted because a screen or endpoint exists. It is accepted only when its acceptance criteria and required evidence pass.
## Reusable Template

Start each material feature with [feature-spec-template.md](feature-spec-template.md).
