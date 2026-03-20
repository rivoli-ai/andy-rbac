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
- **Multi-Application Support** - Single RBAC server for multiple applications
- **Team Management** - Organize users into teams with shared permissions
- **gRPC and REST APIs** - High-performance permission checking
- **ASP.NET Core Integration** - Authorization handlers and policy providers
- **Caching** - In-memory caching for fast permission lookups
- **MCP Support** - Model Context Protocol tools for AI assistants

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

API runs at: **https://localhost:5001**

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
- `POST /api/check/permission` - Check single permission
- `POST /api/check/any-permission` - Check if user has any of the permissions
- `GET /api/check/permissions/{subjectId}` - Get all permissions for a user
- `GET /api/check/roles/{subjectId}` - Get all roles for a user

### Management
- `/api/applications` - Application CRUD
- `/api/roles` - Role management
- `/api/subjects` - User/subject management
- `/api/teams` - Team management

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
