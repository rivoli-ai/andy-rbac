using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;
using Action = Andy.Rbac.Models.Action;

namespace Andy.Rbac.Api.Data;

/// <summary>
/// Seeds initial RBAC data (actions, applications, base roles).
/// </summary>
public static class DataSeeder
{
    public static async Task SeedAsync(RbacDbContext db, CancellationToken ct = default)
    {
        await SeedActionsAsync(db, ct);
        await SeedApplicationsAsync(db, ct);
        await SeedGlobalRolesAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedActionsAsync(RbacDbContext db, CancellationToken ct)
    {
        var actions = new[]
        {
            new Action { Code = "read", Name = "Read", Description = "View resource", SortOrder = 1 },
            new Action { Code = "write", Name = "Write", Description = "Create or update resource", SortOrder = 2 },
            new Action { Code = "delete", Name = "Delete", Description = "Delete resource", SortOrder = 3 },
            new Action { Code = "share", Name = "Share", Description = "Share resource with others", SortOrder = 4 },
            new Action { Code = "admin", Name = "Admin", Description = "Full administrative access", SortOrder = 5 },
            new Action { Code = "execute", Name = "Execute", Description = "Execute or run resource", SortOrder = 6 },
            new Action { Code = "export", Name = "Export", Description = "Export resource data", SortOrder = 7 },
            new Action { Code = "import", Name = "Import", Description = "Import resource data", SortOrder = 8 },
        };

        foreach (var action in actions)
        {
            if (!await db.Actions.AnyAsync(a => a.Code == action.Code, ct))
            {
                db.Actions.Add(action);
            }
        }
    }

    private static async Task SeedApplicationsAsync(RbacDbContext db, CancellationToken ct)
    {
        var applications = new[]
        {
            new Application
            {
                Code = "andy-auth",
                Name = "Andy Auth",
                Description = "OAuth 2.0/OIDC authentication server"
            },
            new Application
            {
                Code = "andy-docs",
                Name = "Andy Docs",
                Description = "AI-assisted document management and writing platform"
            },
            new Application
            {
                Code = "andy-cli",
                Name = "Andy CLI",
                Description = "Command-line AI assistant"
            },
            new Application
            {
                Code = "andy-agentic-web",
                Name = "Andy Agentic Web",
                Description = "Web-based agentic AI interface"
            },
            new Application
            {
                Code = "andy-rbac",
                Name = "Andy RBAC",
                Description = "Role-Based Access Control service"
            },
            new Application
            {
                Code = "code-index",
                Name = "Andy Code Index",
                Description = "Code indexing and search platform"
            },
            new Application
            {
                Code = "containers",
                Name = "Andy Containers",
                Description = "Container management platform"
            }
        };

        foreach (var app in applications)
        {
            if (!await db.Applications.AnyAsync(a => a.Code == app.Code, ct))
            {
                db.Applications.Add(app);
            }
        }
    }

    private static async Task SeedGlobalRolesAsync(RbacDbContext db, CancellationToken ct)
    {
        // Global roles (no application scope)
        var globalRoles = new[]
        {
            new Role
            {
                Code = "super-admin",
                Name = "Super Administrator",
                Description = "Full access to all systems and applications",
                IsSystem = true,
                ApplicationId = null
            },
            new Role
            {
                Code = "user",
                Name = "User",
                Description = "Standard user with basic access",
                IsSystem = true,
                ApplicationId = null
            }
        };

        foreach (var role in globalRoles)
        {
            if (!await db.Roles.AnyAsync(r => r.Code == role.Code && r.ApplicationId == null, ct))
            {
                db.Roles.Add(role);
            }
        }
    }

    /// <summary>
    /// Seeds application-specific resource types and roles.
    /// Call this after the application is registered.
    /// </summary>
    public static async Task SeedApplicationDataAsync(RbacDbContext db, string applicationCode, CancellationToken ct = default)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Code == applicationCode, ct);
        if (app == null)
            return;

        switch (applicationCode)
        {
            case "andy-docs":
                await SeedAndyDocsAsync(db, app, ct);
                break;
            case "andy-cli":
                await SeedAndyCliAsync(db, app, ct);
                break;
            case "andy-auth":
                await SeedAndyAuthAsync(db, app, ct);
                break;
            case "andy-agentic-web":
                await SeedAndyAgenticWebAsync(db, app, ct);
                break;
            case "code-index":
                await SeedCodeIndexAsync(db, app, ct);
                break;
            case "containers":
                await SeedContainersAsync(db, app, ct);
                break;
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAndyDocsAsync(RbacDbContext db, Application app, CancellationToken ct)
    {
        // Resource types
        var resourceTypes = new[]
        {
            new ResourceType { ApplicationId = app.Id, Code = "document", Name = "Document", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "collection", Name = "Collection", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "template", Name = "Template", SupportsInstances = true },
        };

        foreach (var rt in resourceTypes)
        {
            if (!await db.ResourceTypes.AnyAsync(r => r.ApplicationId == app.Id && r.Code == rt.Code, ct))
            {
                db.ResourceTypes.Add(rt);
            }
        }

        // Roles
        var roles = new[]
        {
            new Role { ApplicationId = app.Id, Code = "admin", Name = "Administrator", Description = "Full access to Andy Docs", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "editor", Name = "Editor", Description = "Can create and edit documents", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "viewer", Name = "Viewer", Description = "Can only view documents", IsSystem = true },
        };

        foreach (var role in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.ApplicationId == app.Id && r.Code == role.Code, ct))
            {
                db.Roles.Add(role);
            }
        }
    }

    private static async Task SeedAndyCliAsync(RbacDbContext db, Application app, CancellationToken ct)
    {
        var resourceTypes = new[]
        {
            new ResourceType { ApplicationId = app.Id, Code = "config", Name = "Configuration", SupportsInstances = false },
            new ResourceType { ApplicationId = app.Id, Code = "session", Name = "Session", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "tool", Name = "Tool", SupportsInstances = true },
        };

        foreach (var rt in resourceTypes)
        {
            if (!await db.ResourceTypes.AnyAsync(r => r.ApplicationId == app.Id && r.Code == rt.Code, ct))
            {
                db.ResourceTypes.Add(rt);
            }
        }

        var roles = new[]
        {
            new Role { ApplicationId = app.Id, Code = "admin", Name = "Administrator", Description = "Can modify CLI configuration", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "user", Name = "User", Description = "Standard CLI user", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "restricted", Name = "Restricted", Description = "Limited CLI access (no config changes)", IsSystem = true },
        };

        foreach (var role in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.ApplicationId == app.Id && r.Code == role.Code, ct))
            {
                db.Roles.Add(role);
            }
        }
    }

    private static async Task SeedAndyAuthAsync(RbacDbContext db, Application app, CancellationToken ct)
    {
        var resourceTypes = new[]
        {
            new ResourceType { ApplicationId = app.Id, Code = "user", Name = "User", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "client", Name = "OAuth Client", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "scope", Name = "Scope", SupportsInstances = true },
        };

        foreach (var rt in resourceTypes)
        {
            if (!await db.ResourceTypes.AnyAsync(r => r.ApplicationId == app.Id && r.Code == rt.Code, ct))
            {
                db.ResourceTypes.Add(rt);
            }
        }

        var roles = new[]
        {
            new Role { ApplicationId = app.Id, Code = "admin", Name = "Administrator", Description = "Full auth server access", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "user-manager", Name = "User Manager", Description = "Can manage users", IsSystem = true },
        };

        foreach (var role in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.ApplicationId == app.Id && r.Code == role.Code, ct))
            {
                db.Roles.Add(role);
            }
        }
    }

    private static async Task SeedAndyAgenticWebAsync(RbacDbContext db, Application app, CancellationToken ct)
    {
        var resourceTypes = new[]
        {
            new ResourceType { ApplicationId = app.Id, Code = "setup", Name = "Setup", Description = "Agent setup/configuration", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "conversation", Name = "Conversation", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "workspace", Name = "Workspace", SupportsInstances = true },
        };

        foreach (var rt in resourceTypes)
        {
            if (!await db.ResourceTypes.AnyAsync(r => r.ApplicationId == app.Id && r.Code == rt.Code, ct))
            {
                db.ResourceTypes.Add(rt);
            }
        }

        var roles = new[]
        {
            new Role { ApplicationId = app.Id, Code = "admin", Name = "Administrator", Description = "Full access", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "user", Name = "User", Description = "Standard user", IsSystem = true },
        };

        foreach (var role in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.ApplicationId == app.Id && r.Code == role.Code, ct))
            {
                db.Roles.Add(role);
            }
        }
    }

    private static async Task SeedCodeIndexAsync(RbacDbContext db, Application app, CancellationToken ct)
    {
        var resourceTypes = new[]
        {
            new ResourceType { ApplicationId = app.Id, Code = "repository", Name = "Repository", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "settings", Name = "Settings", SupportsInstances = false },
            new ResourceType { ApplicationId = app.Id, Code = "task", Name = "Task", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "enrichment", Name = "Enrichment", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "search", Name = "Search", SupportsInstances = false },
        };

        foreach (var rt in resourceTypes)
        {
            if (!await db.ResourceTypes.AnyAsync(r => r.ApplicationId == app.Id && r.Code == rt.Code, ct))
            {
                db.ResourceTypes.Add(rt);
            }
        }

        var roles = new[]
        {
            new Role { ApplicationId = app.Id, Code = "admin", Name = "Administrator", Description = "Full access to Code Index", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "user", Name = "User", Description = "Standard Code Index user", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "viewer", Name = "Viewer", Description = "Read-only access", IsSystem = true },
        };

        foreach (var role in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.ApplicationId == app.Id && r.Code == role.Code, ct))
            {
                db.Roles.Add(role);
            }
        }
    }

    private static async Task SeedContainersAsync(RbacDbContext db, Application app, CancellationToken ct)
    {
        var resourceTypes = new[]
        {
            new ResourceType { ApplicationId = app.Id, Code = "container", Name = "Container", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "image", Name = "Image", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "template", Name = "Template", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "workspace", Name = "Workspace", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "provider", Name = "Provider", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "settings", Name = "Settings", SupportsInstances = false },
        };

        foreach (var rt in resourceTypes)
        {
            if (!await db.ResourceTypes.AnyAsync(r => r.ApplicationId == app.Id && r.Code == rt.Code, ct))
            {
                db.ResourceTypes.Add(rt);
            }
        }

        var roles = new[]
        {
            new Role { ApplicationId = app.Id, Code = "admin", Name = "Administrator", Description = "Full access to Containers", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "user", Name = "User", Description = "Standard Containers user", IsSystem = true },
        };

        foreach (var role in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.ApplicationId == app.Id && r.Code == role.Code, ct))
            {
                db.Roles.Add(role);
            }
        }
    }

    /// <summary>
    /// Seeds super-admin permissions for all resource types and creates a test user subject.
    /// </summary>
    public static async Task SeedSuperAdminPermissionsAsync(RbacDbContext db, CancellationToken ct = default)
    {
        var superAdminRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "super-admin" && r.ApplicationId == null, ct);
        if (superAdminRole == null) return;

        var actions = await db.Actions.ToListAsync(ct);
        var resourceTypes = await db.ResourceTypes.ToListAsync(ct);

        foreach (var rt in resourceTypes)
        {
            foreach (var action in actions)
            {
                // Create permission if it doesn't exist
                var permission = await db.Permissions
                    .FirstOrDefaultAsync(p => p.ResourceTypeId == rt.Id && p.ActionId == action.Id, ct);

                if (permission == null)
                {
                    permission = new Permission
                    {
                        ResourceTypeId = rt.Id,
                        ActionId = action.Id,
                        Description = $"{action.Name} {rt.Name}"
                    };
                    db.Permissions.Add(permission);
                    await db.SaveChangesAsync(ct);
                }

                // Link to super-admin role
                if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == superAdminRole.Id && rp.PermissionId == permission.Id, ct))
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = superAdminRole.Id,
                        PermissionId = permission.Id
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Seeds a test user subject with super-admin role.
    /// </summary>
    public static async Task SeedTestSubjectAsync(RbacDbContext db, string externalId, string email, CancellationToken ct = default)
    {
        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == externalId, ct);
        if (subject == null)
        {
            subject = new Subject
            {
                ExternalId = externalId,
                Provider = "andy-auth",
                Type = SubjectType.User,
                Email = email,
                DisplayName = "Test User",
                IsActive = true
            };
            db.Subjects.Add(subject);
            await db.SaveChangesAsync(ct);
        }

        var superAdminRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "super-admin" && r.ApplicationId == null, ct);
        if (superAdminRole == null) return;

        if (!await db.SubjectRoles.AnyAsync(sr => sr.SubjectId == subject.Id && sr.RoleId == superAdminRole.Id, ct))
        {
            db.SubjectRoles.Add(new SubjectRole
            {
                SubjectId = subject.Id,
                RoleId = superAdminRole.Id,
                GrantedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
