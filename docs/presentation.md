---
marp: true
theme: default
paginate: true
size: 16:9
header: 'Andy RBAC — End-to-End Walkthrough'
footer: 'Rivoli AI · andy-rbac'
style: |
  section { font-size: 24px; }
  section h1 { color: #1f4e79; }
  section h2 { color: #2e75b6; border-bottom: 2px solid #2e75b6; padding-bottom: 4px; }
  code { background: #f4f4f4; padding: 2px 4px; border-radius: 3px; }
  pre { font-size: 18px; }
  table { font-size: 20px; }
---

<!-- _class: lead -->
<!-- _paginate: false -->

# Andy RBAC
## End-to-End System Walkthrough

The central role-based access control service for the Andy ecosystem.

*Designed for engineers who have never seen this service before.*

---

## What is Andy RBAC?

A **centralized authorization service** that every other Andy service calls to answer "can subject X do action Y on resource Z?".

- **Hierarchical roles** with inheritance
- **Instance-level permissions** (per-document sharing)
- **Teams** with roles inherited by members
- **External group mapping** (LDAP, Azure AD) → roles
- **Temporary assignments** with optional expiration
- **Subject-agnostic** — users, service accounts, groups from any IdP
- **NuGet client**, attribute-based enforcement, in-memory cache

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 8.0 |
| API | REST + gRPC + MCP |
| Frontend | **Blazor Server** admin UI |
| Database | PostgreSQL (prod) / SQLite (embedded) |
| ORM | Entity Framework Core 8 |
| Auth | JWT Bearer (delegated to Andy Auth) |
| Cache | `IMemoryCache` |
| Logging | Serilog |
| Testing | xUnit + FluentAssertions + Moq |

---

## Solution Layout

```
andy-rbac/
├── src/
│   ├── Andy.Rbac/                   ← models, abstractions, attributes
│   ├── Andy.Rbac.Api/               ← REST + gRPC + MCP
│   ├── Andy.Rbac.Infrastructure/    ← DbContext, repos, migrations
│   ├── Andy.Rbac.Client/            ← NuGet client library
│   ├── Andy.Rbac.Web/               ← Blazor admin UI
│   └── Andy.Rbac.Cli/               ← CLI
└── tests/
    ├── Andy.Rbac.Api.Tests/         ← integration
    ├── Andy.Rbac.Client.Tests/
    └── Andy.Rbac.Tests/             ← unit
```

---

## Domain Model — The Core

`Andy.Rbac/Models/`:

- **Application** — owning app (`andy-docs`, `andy-agents`, …)
- **ResourceType** — e.g. `document`, `folder` (scoped to Application; `SupportsInstances`)
- **Action** — global: `read | write | delete | share | admin | execute | export | import`
- **Permission** — composite of (ResourceType + Action); code = `{app}:{resource}:{action}`
- **Role** — code, `ApplicationId?`, `ParentRoleId?` (inheritance)
- **RolePermission** — N:M between Role and Permission
- **Subject** — `ExternalId`, `Provider` (`andy-auth`/`ldap`/…), `Type` (User/Service/Group)

---

## Domain Model — Assignments

- **SubjectRole** — grants a Role to a Subject; optional `ResourceInstanceId` (scope) and `ExpiresAt`
- **ResourceInstance** — concrete resource (e.g. `doc-123`) with `OwnerSubjectId`, `Metadata`
- **InstancePermission** — direct grant of a Permission to a Subject on a ResourceInstance (sharing)
- **Team** — hierarchy via `ParentTeamId`; optional `ApplicationId`
- **TeamMember** — join with `MembershipRole`: Member / Admin / Owner
- **TeamRole** — roles inherited by every team member
- **ExternalGroupMapping** — provider + external group id → Role (for JWT/claim-based flows)
- **ApiKey**, **RbacAuditLog** — service auth + audit trail

---

## Application Interfaces

**Abstractions** (`Andy.Rbac/Abstractions/`):

- **`IPermissionService`** — `HasPermissionAsync`, `HasAnyPermissionAsync`, `HasAllPermissionsAsync`, `GetPermissionsAsync`, `GetRolesAsync`
- **`IRbacClient`** — same contract, HTTP/gRPC implementation
- **`ICurrentSubjectAccessor`** — extract subject from HttpContext
- **`IResourceAuthorizationService`** — ownership + instance checks
- **`IRbacCache`** — in-memory cache of decisions

**DTOs**: `CheckPermissionRequest`, `CheckAnyPermissionRequest`, `CheckPermissionResponse { Allowed, Reason }`, `GetPermissionsResponse`, `GetRolesResponse`.

---

## Infrastructure — DbContext

**`RbacDbContext`** (`src/Andy.Rbac.Infrastructure/Data/RbacDbContext.cs`):

- 13+ DbSets: Applications, ResourceTypes, Actions, Permissions, Roles, RolePermissions, Subjects, SubjectRoles, ResourceInstances, InstancePermissions, ExternalGroupMappings, Teams, TeamMembers, TeamRoles, ApiKeys, AuditLogs
- **PostgreSQL or SQLite** via `DatabaseProviderExtensions`
- **JSON columns** (jsonb in PG, TEXT in SQLite): `Subject.Metadata`, `ResourceInstance.Metadata`, `RbacAuditLog.Context`, `ApiKey.Scopes`
- Unique indexes on (ApplicationCode), (AppId, Code), (Provider, ExternalId), etc.
- Migration: `20260203013532_InitialCreate`

---

## Permission Repository

**`PermissionRepository.HasPermissionAsync`** walks:

```
Subject
  → SubjectRoles (honor ExpiresAt, filter by ResourceInstanceId)
    → Role (and parents via ParentRoleId)
      → RolePermissions
        → Permission (match code)

Subject
  → InstancePermission (direct share on the resource)
    → Permission (match code)

Groups in JWT
  → ExternalGroupMapping
    → Role → (as above)
```

Returns `true` on first hit. Optionally cached.

---

## REST API — `/api/check`

**`CheckController`**:

- `POST /api/check` — single permission
- `POST /api/check/any` — any of many
- `GET /api/check/permissions/{subjectId}?groups=...&applicationCode=...` — list all permissions
- `GET /api/check/roles/{subjectId}?...` — list all roles

Plus admin surfaces:

- `/api/applications` — register apps + resource types
- `/api/roles` — define + assign permissions
- `/api/subjects` — provision subjects
- `/api/teams` — team/member/role management

---

## gRPC Surface

`proto/rbac.proto`:

- `CheckPermission` / `CheckAnyPermission`
- `GetPermissions` / `GetRoles`
- `ProvisionSubject` → `SubjectResponse`
- `AssignRole` / `RevokeRole`
- `GrantInstancePermission` / `RevokeInstancePermission`

Same evaluation logic as REST but faster for service-to-service traffic.

Middleware: JWT Bearer (delegates to Andy Auth), CORS (defaults to `localhost:3000/5173`, prod `rbac.rivoli.ai`).

---

## MCP Tools

`Mcp/RbacMcpTools.cs` (exposed at `/mcp`):

- `CheckPermission(subjectId, permission, groups?, resourceInstanceId?)`
- `GetUserPermissions`, `GetUserRoles`
- `ListApplications` / `GetApplication`
- Role assignment + team management tools

OAuth resource metadata at `/.well-known/oauth-protected-resource` (RFC 8707) so Claude Desktop / Cursor can discover scopes.

---

## How Other Services Consume RBAC

**NuGet package** `Andy.Rbac.Client`:

```csharp
builder.Services.AddRbacClient(options => {
    options.ApiBaseUrl = "https://rbac-api.example.com";
    options.ApplicationCode = "andy-docs";
});
```

Then on controllers:

```csharp
[RequirePermission("andy-docs:document:read")]
[RequireAnyPermission("andy-docs:document:write", "andy-docs:admin:*")]
[RequireRole("andy-docs-admin")]
```

Client supports both HTTP and gRPC backends, Polly retry, in-memory cache.

---

## Seeding & Application Registration

**`DataSeeder`** (`src/Andy.Rbac.Api/Data/DataSeeder.cs`):

1. `SeedActionsAsync` — 8 global actions
2. `SeedApplicationsAsync` — 12 known apps (andy-auth, andy-docs, andy-issues, andy-agents, code-index, containers, subscription, narration, andy-tasks, andy-rbac, andy-cli, andy-agentic-web)
3. `SeedApplicationDataAsync(appCode)` — per-app resource types + roles
4. `SeedSuperAdminPermissionsAsync` — super-admin gets everything
5. `SeedTestSubjectAsync` — dev-only

Apps can also register dynamically via `POST /api/applications`.

---

## Blazor Admin UI

`src/Andy.Rbac.Web/Pages/`:

- `/Applications` — apps + resource types
- `/Roles` — roles + permission assignments
- `/Subjects` — search users, assign roles
- `/Teams` — members + team roles
- `/AuditLogs` — RBAC event history

OIDC login via Andy Auth. Actions themselves are guarded by RBAC client checks (dogfooding).

---

## CLI (`Andy.Rbac.Cli`)

```bash
andy-rbac applications list
andy-rbac roles assign --role admin --subject <id>
andy-rbac users provision --provider andy-auth --external-id <id>
andy-rbac teams create --code dev-team
andy-rbac check permission <user-id> andy-docs:document:read
```

Thin wrapper over the REST API — useful for ops scripts and CI setup.

---

## Data Flow — Permission Check

```
1. User sends request to andy-docs with JWT.
2. Controller has [RequirePermission("andy-docs:document:read")].
3. Attribute → IPermissionService.HasPermissionAsync(subjectId, permission).
4. RbacHttpClient calls POST /api/check
   { "SubjectId":"user-123",
     "Permission":"andy-docs:document:read",
     "Groups":[...from JWT],
     "ResourceInstanceId":"doc-456" }
5. CheckController → PermissionEvaluator.CheckPermissionAsync
6. PermissionRepository walks SubjectRoles / InstancePermissions /
   ExternalGroupMappings → inheritance chain.
7. Returns CheckPermissionResponse { Allowed: true, Reason: "…" }.
8. andy-docs controller proceeds (or 403).
9. Audit event written to RbacAuditLog.
```

Optional client-side cache short-circuits repeat checks.

---

## Configuration & Ports

| Port | Purpose |
|------|---------|
| 5003 / 7003 | RBAC API HTTPS (dev/docker) |
| 5180 | Blazor admin UI |
| 5432 / 5433 | PostgreSQL |

Key settings:

```json
"Auth": { "Authority": "https://auth.rivoli.ai", "Audience": "urn:andy-rbac-api" },
"Database": { "Provider": "PostgreSql" },
"Mcp": { "ServerUrl": "https://rbac-api.rivoli.ai", "McpPath": "/mcp" },
"Cors": { "Origins": ["http://localhost:3000", "https://rbac.rivoli.ai"] }
```

---

## Docker

`docker-compose.yml`:

- `postgres:16-alpine` (port 5433)
- API (`7003:8443` / `7004:8080`)
- Web (`5180:8443` / `5181:8080`)

Multi-stage Dockerfile:

1. SDK build + publish
2. Runtime image with self-signed dev cert
3. Corporate CAs mountable

Environment override supported: `Database__Provider=Sqlite` for Conductor embedded mode.

---

## Testing

- **`Andy.Rbac.Api.Tests`** — `RbacWebApplicationFactory` integration tests: CheckController, ApplicationsController, RolesController, SubjectsController, gRPC service
- **`Andy.Rbac.Client.Tests`** — HTTP/gRPC client, cache behavior, retry policies
- **`Andy.Rbac.Tests`** — models, authorization attributes, permission evaluation

Patterns: Moq for repositories, FluentAssertions for readable checks, seeded fixtures for consistent test data.

`dotnet test` + Coverlet for coverage.

---

<!-- _class: lead -->

# Where to start reading

1. `src/Andy.Rbac/Models/Role.cs` — the core inheritance model
2. `src/Andy.Rbac/Abstractions/IPermissionService.cs`
3. `src/Andy.Rbac.Infrastructure/Repositories/PermissionRepository.cs` — the traversal
4. `src/Andy.Rbac.Api/Controllers/CheckController.cs` — the HTTP entry point
5. `src/Andy.Rbac.Client/Attributes/RequirePermissionAttribute.cs` — how callers wire it up
6. `src/Andy.Rbac.Api/Data/DataSeeder.cs` — what gets preloaded

Blazor admin: port 5180 · MCP: `/mcp`.
