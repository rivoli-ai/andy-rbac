# Policies

andy-rbac models two orthogonal concerns:

- **Permissions** — *who can call what?* The classical RBAC model (subjects → roles → permissions).
- **Policies** — *under what risk profile may an agent execute this work?*

This document covers the second. For the first, see the README's *Permission Format* section.

## Why policies exist

Permissions answer "is this caller authorized?" Policies answer "how much oversight does this action require?". The two are independent: an admin (with every permission) running a task tagged `high-risk` still gets pre- and post-execution approval gates; a service account (with no admin permissions) running a `read-only` task hits no gates at all.

A policy is a named risk profile with a small bundle of rules attached. Consumers (andy-tasks auto-gating, andy-tasks retention, andy-docs archive tier, Conductor `ActionBus`) read the rules and decide what to do.

## Stock catalog

Six policies are seeded as `IsSystem` rows at first boot:

| Code | Criticality | Pre-gate | Post-gate | Blocks deploy | Row retention | Archive tier | Use when… |
|---|---|---|---|---|---|---|---|
| `read-only` | Low | — | — | — | 30 d | default | Agent reads repo state but never mutates anything. |
| `write-branch` | Low | — | — | — | 30 d | default | Agent commits and pushes to non-default branches only. |
| `sandboxed` | Medium | — | — | — | 30 d | default | Agent runs in an isolated sandbox; no external network or production access. |
| `no-prod` | High | ✓ | — | ✓ | 365 d | medium | Pre-execution gate on any deploy-class task; blocks production-touching tools. |
| `high-risk` | Critical | ✓ | ✓ | — | 2555 d (~7 y) | high | Pre + post execution gates on every task; full action-log retention. |
| `draft-only` | Medium | — | ✓ | — | 30 d | default | Post-execution gate on publishable artifacts; outputs marked draft until reviewed. |

System policies cannot be edited or deleted via the REST API (`PUT` / `DELETE` return 400). To change stock policy behaviour you change `DataSeeder.SeedStockPoliciesAsync` and ship a migration.

## Rule keys

`Policy.Rules` is a free-form dictionary persisted as `jsonb` on Postgres and `TEXT` on SQLite. Stable keys defined as of 2026-05-08:

| Key | Type | Consumer | Default | Meaning |
|---|---|---|---|---|
| `requirePreGate` | bool | andy-tasks AC3 | false | Emit a pre-execution `ApprovalGate` row when a task with this policy is created. |
| `requirePostGate` | bool | andy-tasks AC3 | false | Emit a post-execution `ApprovalGate` row when a task with this policy is verified. |
| `blocksDeployTools` | bool | andy-tasks AC3 / Conductor V5 | false | Reject task creation when `DelegationContract.ToolsAllowed` contains deploy-class tools (`deploy`, `kubectl`, …). |
| `retentionDays` | int | andy-tasks AD4 | 30 | Days before the retention sweeper hard-deletes `agent_runs`, `task_events`, `agent_action_log`. |
| `archiveTier` | string | andy-tasks AD3 / andy-docs AJ | `"default"` | Archive tier (`default` / `medium` / `high`) governing andy-docs blob retention. |

**Adding a rule key**: introduce it in your consumer, document it here, and ship a follow-up commit that sets the key on the relevant stock policies in `DataSeeder.SeedStockPoliciesAsync`. No andy-rbac schema migration is needed — the column is `jsonb`.

**Typo handling**: unknown keys are ignored silently by consumers. Misspelt keys (e.g. `require_pre_gate` instead of `requirePreGate`) won't break anything, they just won't take effect. Consumer-side unit tests catch typos in practice.

## Authoring a tenant policy

Stock policies cover the common cases. To register a tenant-specific policy:

```bash
curl -X POST https://rbac.example.com/api/policies \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "code": "tenant-financial-data",
    "name": "Tenant — financial data",
    "criticality": "Critical",
    "description": "Financial data domain — requires double-sign-off and 7-year retention.",
    "rules": {
      "requirePreGate": true,
      "requirePostGate": true,
      "retentionDays": 2555,
      "archiveTier": "high"
    }
  }'
```

Or via CLI:

```bash
andy-rbac policy list                           # see what's there
andy-rbac policy get high-risk                  # inspect the stock high-risk policy as a template
# (write paths via REST or admin UI; the CLI list/get surface is read-only.)
```

## Event taxonomy

Mutations emit on the `andy.rbac.events.policy.*` subject family per ADR-0001 (subject scheme):

```
andy.rbac.events.policy.{policy_id}.created
andy.rbac.events.policy.{policy_id}.updated
andy.rbac.events.policy.{policy_id}.deleted
```

`{policy_id}` is the policy's `Guid Id`. Payloads include both `PolicyId` and `Code` so consumers can resolve either way.

Consumers subscribe wildcard:

```csharp
await bus.SubscribeAsync("andy.rbac.events.policy.>", handler, ct);
```

Typical reactions:

- **andy-tasks PDP cache** invalidates a cached `Policy` on `*.updated` / `*.deleted`.
- **andy-tasks AC3** re-evaluates open auto-gates if a policy's `requirePreGate` / `requirePostGate` flips.
- **andy-tasks AD4a** propagates retention changes to the andy-docs archive tier when `retentionDays` or `archiveTier` changes.

## Resolving a policy at runtime

andy-tasks and andy-docs both look up policies by `Code`:

```csharp
// andy-tasks: resolve the policy named on a Goal
var policy = await rbacClient.GetPolicyByCodeAsync(goal.PolicyId, ct);
var requiresPreGate = policy.Rules?.GetValueOrDefault("requirePreGate") as bool? ?? false;
```

The PDP-cache pattern (cache by `Code`, invalidate on `andy.rbac.events.policy.*.updated`) is the recommended consumer side. See `andy-tasks/docs/adr/0001-messaging.md` §6 for the cache invalidation protocol.

## FAQ

**Q: Why was my action denied?**
The denying consumer (Conductor `ActionBus`, andy-tasks `AutoGatingPolicyEvaluator`) logs the policy code and the rule key that triggered the deny. Look at the corresponding `RbacAuditLog` row or task-event payload — both record the policy `Code`, the matched rule, and the input that triggered it.

**Q: Can I downgrade a `high-risk` policy on a finished run?**
No. Policy state at run-creation time is snapshotted into `PolicySnapshotJson` (andy-tasks AD2a). Editing the policy doesn't retroactively affect frozen snapshots. The retention sweeper reads the snapshot, not the live policy.

**Q: How do I extend retention on an existing run?**
`AppSettings.Audit.RetentionOverrides` (per-tenant). Override always extends, never shortens. To shorten retention you need an admin-signed `RetentionDecreaseApproval` per AD4 §"Reconciliation update (2026-04-20)".

**Q: Is the policy catalog the same across deployments?**
The six stock policies are seeded everywhere. Tenant policies live in each deployment's RBAC database. There is no global policy registry across tenants — each `andy-rbac` instance owns its catalog.

## See also

- [ADR 0001 — Policies as first-class catalog rows](adr/0001-policies.md)
- `andy-tasks/docs/adr/0001-messaging.md` — outbox + subject taxonomy
- rivoli-ai/andy-rbac#10 — Epic V parent issue
- README *Quick Start* + *API Endpoints*
