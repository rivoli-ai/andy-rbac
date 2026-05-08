# ADR 0001 — Policies as first-class catalog rows

- **Status:** Accepted
- **Date:** 2026-05-08
- **Deciders:** Sami Ben Grine
- **Scope:** andy-rbac (catalog) plus every Andy service that consumes policy decisions: andy-tasks (delegation contracts, auto-gating, retention), andy-docs (archive tier), Conductor (`ActionBus` enforcement).

## Context

andy-rbac already models *who can do what* via roles and permissions — RBAC in the classical sense. That answers the question "is this caller allowed to invoke this endpoint?" but it does not answer the orthogonal question "under what risk profile may an agent execute this task?".

The simulator (`simulator/app.js`) treated this second question as a separate vocabulary — six named **policies** (`read-only`, `write-branch`, `sandboxed`, `no-prod`, `high-risk`, `draft-only`) that bind to delegation contracts, action-bus invocations, and approval gates. Reusing the existing `Permission` table for this would conflate two unrelated concerns: *authorization* (does this principal have rights?) and *risk gating* (how much oversight does this action require?).

Downstream services need a stable cross-service identifier. andy-tasks already stores `Goal.PolicyId` and `DelegationContract.PolicyId` as `varchar(64)` strings. andy-docs's archive tier resolution and andy-tasks's retention sweeper both depend on policy lookup. Without a Policy entity in andy-rbac, every consumer either hardcodes the six names or maintains its own catalog — both of which drift.

## Decision

We model `Policy` as a first-class entity in andy-rbac, separate from `Role` and `Permission`, with the following shape:

```csharp
class Policy {
  Guid Id;                          // storage key; NOT the cross-service identifier
  string Code;                      // unique slug, e.g. "high-risk" — the cross-service id
  string Name;                      // display name
  PolicyCriticality Criticality;    // Low | Medium | High | Critical
  Dictionary<string,object>? Rules; // jsonb on Postgres / TEXT on SQLite
  string? Description;
  bool IsSystem;                    // stock policies cannot be edited or deleted
  DateTimeOffset CreatedAt, UpdatedAt;
}
```

### 1. Identity is by `Code`, not `Id`.

Cross-service references (`Goal.PolicyId`, `DelegationContract.PolicyId`, archive-tier resolution, retention map keys) all use the stable `Code` string. The `Guid Id` is the storage primary key and the wire identifier in events (`andy.rbac.events.policy.{policy_id}.{kind}`), but never the cross-service reference. This keeps codes human-readable in DB diffs, in logs, and in the simulator vocabulary; the Guid is an internal detail.

### 2. Rules are a free-form JSON dictionary, not a typed schema.

Each consumer picks the keys it understands. Stable keys defined as of 2026-05-08:

| Key | Type | Consumer | Meaning |
|---|---|---|---|
| `requirePreGate` | bool | andy-tasks AC3 | emit pre-execution approval gate |
| `requirePostGate` | bool | andy-tasks AC3 | emit post-execution approval gate |
| `blocksDeployTools` | bool | andy-tasks AC3 / Conductor V5 | reject deploy-class tasks under this policy |
| `retentionDays` | int | andy-tasks AD4 | row retention for `agent_runs`, `task_events`, `agent_action_log` |
| `archiveTier` | string | andy-tasks AD3 / andy-docs AJ | archive tier (`default` / `medium` / `high`) for blob retention |

A typed Rules schema was considered and rejected. The set of keys grows as new consumers join (Epic AC, AD, AI, future). Rev'ing the schema across four repos every time a key lands would block on coordination overhead. The current shape lets each consumer ship its keys in lockstep with its own epic. We will revisit if the key surface stabilises.

### 3. Stock policies are seeded with `IsSystem = true`.

The six simulator policies are seeded by `DataSeeder.SeedStockPoliciesAsync` and flagged `IsSystem`. Tenants may register additional non-system policies via `POST /api/policies`. `IsSystem` rows reject `PUT` and `DELETE` with 400; this is a tracker-level immutability guarantee, not a database constraint, but the constraint is enforced consistently in `PolicyService`.

### 4. Mutations stage transactional outbox events.

`PolicyService.CreateAsync` / `UpdateAsync` / `DeleteAsync` call `IRbacEventPublisher.Policy{Created,Updated,Deleted}` on the same `RbacDbContext` as the domain row. The transactional outbox pattern from `andy-tasks/docs/adr/0001-messaging.md` applies unchanged: domain row + outbox row commit atomically; `OutboxDispatcher` drains to NATS at-least-once.

Subjects follow the AL3 scheme:

```
andy.rbac.events.policy.{policy_id}.created
andy.rbac.events.policy.{policy_id}.updated
andy.rbac.events.policy.{policy_id}.deleted
```

`{policy_id}` is the Guid (stable wire identifier); the payload includes both `PolicyId` and `Code` so consumers can resolve either way.

### 5. Read access is broadened; write access is admin-only.

`GET /api/policies` and `GET /api/policies/{id|by-code}` require any authenticated caller. They are also surfaced as MCP tools (V7 — `ListPolicies`, `GetPolicy`) so agent contexts can resolve policies. Write paths (`POST` / `PUT` / `DELETE`) are not surfaced via MCP; the REST endpoints stay behind the admin auth path so policy mutations never travel through a tool call.

## Consequences

### Positive
- Cross-service consumers (andy-tasks AC3, AD4, AD7; andy-docs RetentionCascadeWorker) gain a single source of truth for policy state.
- Adding a new rule key is a one-side change in the consumer; no schema migration in andy-rbac.
- The simulator vocabulary (`read-only`, `high-risk`, …) is preserved verbatim as `Code` values, so old documentation stays accurate.

### Negative
- The free-form `Rules` dictionary makes typos ("require_pre_gate" vs "requirePreGate") fail silently at the consumer rather than at write time. We accept this in exchange for schema-rev velocity; consumer-side tests catch typos in practice.
- `Code` is the cross-service identifier but `Id` is what appears in NATS subjects. Consumers must look up by `Code` after receiving an event; they cannot subscribe to `andy.rbac.events.policy.high-risk.>` directly.

### Neutral
- The Policy table is small (six stock rows + tenant overrides) — there is no scale concern in the next two years.
- `IsSystem` enforcement is at the service layer, not the DB layer. Bypassing it requires writing raw SQL against the DB; that is consistent with how the rest of the andy-rbac system policies (admin/editor/viewer roles) work.

## Alternatives considered

- **Reuse the `Permission` table.** Rejected — conflates authorization with risk gating and forces every consumer to query through the permission evaluator just to ask "what's the retention for this run?".
- **Open Policy Agent (OPA) / Rego.** Rejected — the rule surface is small and shipping an embedded OPA evaluator alongside every consumer is operational overhead far in excess of what the rule shape needs. Revisit if rule complexity grows past simple key/value pairs.
- **Server-side rule evaluation.** Rejected for now. Each consumer makes its own decisions from the rule dict because the consumers are diverse (auto-gating in andy-tasks, retention in andy-tasks, archive tier in andy-docs, action enforcement in Conductor). A central evaluator would either need a universal rule language (see OPA above) or a fan-out RPC per decision point. Both are heavier than the value.
- **Snapshot rules onto the `Goal` / `DelegationContract`.** Adopted as a parallel pattern in andy-tasks AD2a (`PolicySnapshotJson`). The Policy entity is the source of truth; the snapshot is a per-run frozen copy so policy edits don't retroactively change in-flight runs. AD2a complements this ADR; it doesn't replace it.

## Phased rollout

1. **V1–V3 (this ADR's scope)** — entity, seed, REST endpoints, `IRbacEventPublisher` un-stub. ✅ shipped 2026-05-08 (PR #66).
2. **V7** — MCP tools `policy.list` / `policy.get`. ✅ shipped 2026-05-08 (PR #67).
3. **V8** — `andy-rbac policy {list,get}` CLI subcommand. ✅ shipped 2026-05-08 (PR #68).
4. **V11** — this ADR + design doc + README. (current PR.)
5. **V5** (rivoli-ai/conductor) — `ActionBus` enforcement hook reads policy on every Action invocation.
6. **V6** — side-effects contract: andy-tasks AC3 (auto-gating), AD4/AD7 (retention) consume `Rules`.
7. **AO** — cross-service end-to-end flow tests.

## References

- `simulator/app.js` — original Policies tab with the six-policy catalog.
- `andy-tasks/docs/adr/0001-messaging.md` — transactional outbox + NATS subject taxonomy this ADR builds on.
- rivoli-ai/andy-rbac#10 — Epic V parent issue.
- rivoli-ai/andy-rbac#66 / #67 / #68 — PRs implementing V1–V3, V7, V8.
- rivoli-ai/andy-tasks#57 — AC3 auto-gating consumer.
- rivoli-ai/andy-tasks#73 — AD4 retention consumer.
- rivoli-ai/andy-tasks#77 — AD7 critical-flag consumer.
