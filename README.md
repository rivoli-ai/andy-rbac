# Andy RBAC

Role-Based Access Control (RBAC) system for Andy applications.

> **ALPHA RELEASE WARNING**
>
> This software is in ALPHA stage. **NO GUARANTEES** are made about its functionality, stability, or safety.
>
> **CRITICAL WARNINGS:**
> - Permission management is **NOT FULLY TESTED** and may have security vulnerabilities
> - Authorization decisions may be **INCORRECT** or **INCONSISTENT**
> - **DO NOT USE** in production environments
> - **DO NOT USE** to protect sensitive resources or data
> - The authors assume **NO RESPONSIBILITY** for unauthorized access or security breaches
>
> **USE AT YOUR OWN RISK**

## Features

- **Role-Based Access Control** - Hierarchical roles with permission inheritance
- **Fine-Grained Permissions** - Resource-level and instance-level permissions
- **Policies** - Execution-time risk profiles (`read-only`, `high-risk`, `sandboxed`, `no-prod`, `draft-only`, `write-branch`) governing agent actions, gates, and retention
- **Multi-Application Support** - Single RBAC server for multiple applications
- **Team Management** - Organize users into teams with shared permissions
- **gRPC and REST APIs** - High-performance permission checking
- **ASP.NET Core Integration** - Authorization handlers and policy providers
- **Caching** - In-memory caching for fast permission lookups
- **MCP Support** - Model Context Protocol tools for AI assistants
- **CLI** - `andy-rbac` for managing applications, roles, teams, users, and policies

## Quick Start

### Prerequisites

- .NET 8.0 SDK
- Docker Desktop (for PostgreSQL)

### Local Development

```bash
# Start PostgreSQL
docker-compose up -d

# Run the API server
cd src/Andy.Rbac.Api
dotnet run
```

API runs at: **https://localhost:5003**

### Database bootstrap

Schema creation and seeding are separate switches, and both default to on in
Development and off elsewhere:

| Setting | Default | Purpose |
|---------|---------|---------|
| `Database:MigrateOnStartup` | `true` in Development | Apply EF migrations (PostgreSQL) or `EnsureCreated` + drift heal (SQLite) at startup. |
| `Database:SeedOnStartup` | follows `MigrateOnStartup` | Seed actions, applications, global roles, stock policies and manifest data. |

Outside Development, apply migrations out of band so concurrent instances don't
race — as an init container, a release job, or:

```bash
dotnet run --project src/Andy.Rbac.Api -- --migrate   # migrate + seed, then exit
```

Seeding requires the schema to exist but not to have been created by the same
process, so an init container can migrate while the app seeds on startup.

### Administrator authority

RBAC administration is granted by holding a global `super-admin` or `rbac-admin`
role **in the RBAC store** — not by a token claim. An `admin` claim is unscoped,
so honouring it means any application's admin can administer RBAC globally.

Because the first administrator has to grant themselves a role before any
store-backed grant exists, the claim remains available as a bootstrap:

| Setting | Default | Purpose |
|---------|---------|---------|
| `Authorization:AdministratorRoles` | `["super-admin", "rbac-admin"]` | Store roles conferring RBAC administration. |
| `Authorization:AllowClaimBootstrap` | `true` | Honour an `admin`/`super-admin` **claim**. Every use logs a warning. |
| `Authorization:BootstrapClaimRoles` | `["super-admin", "admin"]` | Claim values the bootstrap accepts. |

Once real administrators hold a store-backed role, set
`Authorization:AllowClaimBootstrap=false`.

## Project Structure

```
src/
  Andy.Rbac/                 Core library (models, abstractions, authorization)
  Andy.Rbac.Api/             REST and gRPC API server
  Andy.Rbac.Client/          HTTP/gRPC client library
  Andy.Rbac.Infrastructure/  EF Core, repositories
  Andy.Rbac.Web/             Admin UI (Blazor)
  Andy.Rbac.Cli/             Command-line interface

tests/
  Andy.Rbac.Tests/           Core library tests
  Andy.Rbac.Api.Tests/       API integration tests
  Andy.Rbac.Client.Tests/    Client library tests
```

## NuGet Packages

| Package | Description |
|---------|-------------|
| [Andy.Rbac](https://www.nuget.org/packages/Andy.Rbac) | Core RBAC library with models, abstractions, and authorization |
| [Andy.Rbac.Client](https://www.nuget.org/packages/Andy.Rbac.Client) | HTTP and gRPC client for the RBAC API |
| [Andy.Rbac.Cli](https://www.nuget.org/packages/Andy.Rbac.Cli) | Command-line tool for managing RBAC resources |

Install the client library to integrate RBAC into your application:

```bash
dotnet add package Andy.Rbac.Client
```

### Usage

```csharp
// Add to Program.cs
builder.Services.AddRbacClient(options =>
{
    options.BaseUrl = "https://rbac-api.example.com";
    options.ApplicationCode = "my-app";
});

// Use in controllers
[RequirePermission("document:read")]
public async Task<IActionResult> GetDocument(string id) { }

[RequireAnyPermission("document:write", "document:admin")]
public async Task<IActionResult> UpdateDocument(string id) { }

[RequireRole("admin")]
public async Task<IActionResult> DeleteDocument(string id) { }
```

## Permission Format

Permissions follow the format: `{app-code}:{resource-type}:{action}`

Examples:
- `andy-docs:document:read`
- `andy-docs:document:write`
- `andy-docs:folder:create`

## API Endpoints

### Permission Checking
- `POST /api/check` - Check single permission
- `POST /api/check/any` - Check if a user has any of the permissions
- `GET /api/check/permissions/{subjectId}` - Get all permissions for a user
- `GET /api/check/roles/{subjectId}` - Get all roles for a user

### Management
- `/api/applications` - Application CRUD
- `/api/roles` - Role management
- `/api/subjects` - User/subject management
- `/api/teams` - Team management
- `/api/policies` - Policy catalog (Epic V — see [docs/policies.md](docs/policies.md))

## Policies

A **policy** is a named risk profile that downstream services (andy-tasks, andy-docs, Conductor) consume to decide auto-gating, retention, and action enforcement. Six stock policies ship pre-seeded (`read-only`, `write-branch`, `sandboxed`, `no-prod`, `high-risk`, `draft-only`); tenants may register additional policies via `POST /api/policies`.

See **[docs/policies.md](docs/policies.md)** for the full catalog, rule key reference, event taxonomy, and FAQ. The design rationale is captured in **[ADR 0001 — Policies as first-class catalog rows](docs/adr/0001-policies.md)**.

## CLI

The `andy-rbac` CLI (`src/Andy.Rbac.Cli`) wraps the REST API for terminal-based admin and agent automation:

```bash
# Applications, roles, teams, users
andy-rbac app list
andy-rbac role list --application andy-tasks
andy-rbac team list
andy-rbac user search jane

# Policies (Epic V)
andy-rbac policy list                       # six stock policies + tenant overrides
andy-rbac policy list --criticality High    # filter by criticality
andy-rbac policy get high-risk              # full rule body for a policy

# Permission checks
andy-rbac check permission user-123 andy-tasks:goal:read

# Output formats: --output table (default) | json | csv
```

Global flags: `--api-url` / `-u` (env `ANDY_RBAC_URL`), `--api-key` / `-k` (env `ANDY_RBAC_API_KEY`), `--output` / `-o`.

### Authenticating the CLI

The CLI sends its key as an `X-API-Key` header. Keys are minted against an
existing subject and act as that subject:

```bash
# Mint a key (administrator only). The plaintext key is returned ONCE.
curl -X POST https://localhost:5003/api/apikeys \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"subjectExternalId":"admin-user","name":"laptop CLI"}'

# => { "keyPrefix": "rbac_live_...", "key": "rbac_live_....<secret>", ... }

export ANDY_RBAC_API_KEY='rbac_live_....<secret>'
andy-rbac role list --application andy-tasks
```

Only a SHA-256 hash of the secret is stored, so a lost key cannot be recovered —
mint a replacement and revoke the old one:

```bash
curl -X POST https://localhost:5003/api/apikeys/{id}/revoke -H "Authorization: Bearer $TOKEN"
```

A key presents its owner's roles, read from the RBAC store at authentication
time. Passing `scopes` at creation narrows that set — the intersection of the
declared scopes and the roles the owner actually holds — so a key can never
carry more authority than its subject. Keys stop working immediately when
revoked, when `expiresAt` passes, or when their owning subject is deactivated.

## MCP Tools

The `andy-rbac` API exposes MCP tools for AI assistants. Read-only tools cover permission checks and the Policy catalog; write tools cover application/role/team/user management.

```
# Permission checks
CheckPermission, GetUserPermissions, GetUserRoles

# Catalog reads
ListApplications, GetApplication, ListRoles, ListTeams, SearchUsers, GetUser
ListPolicies, GetPolicy

# Mutations (admin-only paths)
CreateApplication, CreateRole, AssignRoleToUser, RevokeRoleFromUser,
CreateTeam, AddUserToTeam, AssignRoleToTeam, CreateUser
```

Policy mutations (`POST` / `PUT` / `DELETE`) are intentionally not surfaced via MCP — they stay on the REST surface so admin authorization paths don't move through tool calls.

## Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"
```

## Technology Stack

- **Framework**: ASP.NET Core 8.0
- **Database**: PostgreSQL with EF Core
- **APIs**: REST + gRPC
- **Caching**: IMemoryCache
- **Testing**: xUnit, FluentAssertions, Moq

## License

Apache 2.0 - see the [LICENSE](LICENSE) file for details.
