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
    /// <summary>
    /// External identifier of the well-known dev/test user (<c>test@andy.local</c>),
    /// pinned to the same Guid that andy-auth's <c>DbSeeder.TestUserWellKnownId</c>
    /// assigns. Pre-binding manifest-declared <c>testUserRole</c> entries to this
    /// Subject would silently miss real tokens if these constants drifted; both
    /// repos must be updated together. See rivoli-ai/andy-auth#56 +
    /// rivoli-ai/andy-rbac#52.
    /// </summary>
    public const string TestUserWellKnownExternalId = "00000000-0000-0000-0000-000000000001";
    public const string TestUserWellKnownEmail = "test@andy.local";

    public static async Task SeedAsync(RbacDbContext db, CancellationToken ct = default)
    {
        await SeedActionsAsync(db, ct);
        await SeedApplicationsAsync(db, ct);
        await SeedGlobalRolesAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Manifest-driven application + resource-type + role seeding. Reads every
    /// available registration manifest and inserts its RBAC section into the
    /// database. Idempotent via existence checks. The legacy hardcoded
    /// SeedApplicationsAsync / SeedApplicationDataAsync paths still run
    /// alongside during the transition until every service ships a manifest.
    /// </summary>
    public static async Task SeedFromManifestsAsync(
        RbacDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        var manifests = RegistrationManifestLoader.LoadAll(configuration, logger);
        if (manifests.Count == 0)
        {
            logger.LogInformation("No registration manifests found; relying on legacy hardcoded RBAC seeding.");
            return;
        }

        foreach (var manifest in manifests)
        {
            if (manifest.Rbac is null) continue;

            var app = await db.Applications.FirstOrDefaultAsync(a => a.Code == manifest.Rbac.ApplicationCode, ct);
            if (app is null)
            {
                app = new Application
                {
                    Code = manifest.Rbac.ApplicationCode,
                    Name = manifest.Rbac.ApplicationName,
                    Description = manifest.Rbac.Description ?? manifest.Service.Description
                };
                db.Applications.Add(app);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("[manifest] Registered RBAC application: {Code}", app.Code);
            }

            foreach (var rt in manifest.Rbac.ResourceTypes ?? Array.Empty<RegistrationResourceType>())
            {
                if (!await db.ResourceTypes.AnyAsync(r => r.ApplicationId == app.Id && r.Code == rt.Code, ct))
                {
                    db.ResourceTypes.Add(new ResourceType
                    {
                        ApplicationId = app.Id,
                        Code = rt.Code,
                        Name = rt.Name,
                        SupportsInstances = rt.SupportsInstances ?? false
                    });
                }
            }

            foreach (var role in manifest.Rbac.Roles ?? Array.Empty<RegistrationRole>())
            {
                if (!await db.Roles.AnyAsync(r => r.ApplicationId == app.Id && r.Code == role.Code, ct))
                {
                    db.Roles.Add(new Role
                    {
                        ApplicationId = app.Id,
                        Code = role.Code,
                        Name = role.Name,
                        Description = role.Description ?? string.Empty,
                        IsSystem = role.IsSystem ?? true
                    });
                }
            }
        }

        // Persist applications + roles before processing testUserRole bindings —
        // the latter need the Role rows queryable by code.
        await db.SaveChangesAsync(ct);

        await SeedTestUserRoleBindingsAsync(db, manifests, configuration, logger, ct);
    }

    /// <summary>
    /// Manifest-declared <c>testUserRole</c> bindings (#52). For each manifest
    /// that declares one, ensures the well-known dev test subject
    /// (<c>test@andy.local</c>, andy-auth's <see cref="TestUserWellKnownExternalId"/>)
    /// has the named role on this manifest's application. Skipped in Production
    /// — <c>testUserRole</c> is a dev convenience and never appropriate for prod.
    /// Idempotent; safe across repeated startups.
    /// </summary>
    private static async Task SeedTestUserRoleBindingsAsync(
        RbacDbContext db,
        IReadOnlyList<RegistrationManifest> manifests,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct)
    {
        // Defense-in-depth gate (issue #49): the binding requires BOTH a
        // non-Production environment AND an explicit `Rbac:AllowTestUserSeed`
        // opt-in. A leaked `ASPNETCORE_ENVIRONMENT=Development` to a real
        // deployment alone is no longer enough to activate the well-known
        // backdoor subject — the operator would also have to flip the
        // explicit flag.
        var environment = configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";
        var allowTestUserSeed = configuration.GetValue<bool>("Rbac:AllowTestUserSeed");
        var isProduction = string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);

        if (isProduction || !allowTestUserSeed)
        {
            // Walk the manifests once to log which bindings were skipped — operators
            // running a misconfigured deployment should see them in logs.
            var reason = isProduction
                ? "environment is Production"
                : "Rbac:AllowTestUserSeed is not set";
            foreach (var m in manifests)
            {
                if (!string.IsNullOrWhiteSpace(m.Rbac?.TestUserRole))
                {
                    logger.LogInformation(
                        "[manifest] Skipping testUserRole binding ({Role}) on {App} — {Reason}.",
                        m.Rbac.TestUserRole, m.Rbac.ApplicationCode, reason);
                }
            }
            return;
        }

        Subject? testSubject = null;

        foreach (var manifest in manifests)
        {
            var roleCode = manifest.Rbac?.TestUserRole;
            if (string.IsNullOrWhiteSpace(roleCode)) continue;

            var app = await db.Applications.FirstOrDefaultAsync(a => a.Code == manifest.Rbac!.ApplicationCode, ct);
            if (app is null)
            {
                logger.LogWarning(
                    "[manifest] testUserRole '{Role}' declared for application '{App}' but the application row was not found — skipping.",
                    roleCode, manifest.Rbac!.ApplicationCode);
                continue;
            }

            var role = await db.Roles.FirstOrDefaultAsync(r => r.ApplicationId == app.Id && r.Code == roleCode, ct);
            if (role is null)
            {
                logger.LogWarning(
                    "[manifest] testUserRole '{Role}' declared for '{App}' but no matching role exists in this manifest — skipping.",
                    roleCode, manifest.Rbac!.ApplicationCode);
                continue;
            }

            // Lazily provision the test subject on first need so we don't create
            // an orphan row for manifests that don't declare a testUserRole.
            testSubject ??= await GetOrCreateTestSubjectAsync(db, ct);

            if (!await db.SubjectRoles.AnyAsync(sr => sr.SubjectId == testSubject.Id && sr.RoleId == role.Id, ct))
            {
                db.SubjectRoles.Add(new SubjectRole
                {
                    SubjectId = testSubject.Id,
                    RoleId = role.Id,
                    GrantedAt = DateTimeOffset.UtcNow
                });
                logger.LogInformation(
                    "[manifest] Granted role '{Role}' on '{App}' to test user {Email} ({ExternalId}).",
                    roleCode, manifest.Rbac!.ApplicationCode, TestUserWellKnownEmail, TestUserWellKnownExternalId);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Subject> GetOrCreateTestSubjectAsync(RbacDbContext db, CancellationToken ct)
    {
        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == TestUserWellKnownExternalId, ct);
        if (subject is not null) return subject;

        subject = new Subject
        {
            ExternalId = TestUserWellKnownExternalId,
            Provider = "andy-auth",
            Type = SubjectType.User,
            Email = TestUserWellKnownEmail,
            DisplayName = "Test User",
            IsActive = true
        };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync(ct);
        return subject;
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

    /// <summary>
    /// Legacy hardcoded application seeding. Only registers consumer apps and
    /// out-of-scope services that don't yet ship a <c>config/registration.json</c>:
    /// andy-cli, andy-agentic-web, subscription, narration. Every other Andy
    /// service now registers itself via <see cref="SeedFromManifestsAsync"/>.
    /// </summary>
    private static async Task SeedApplicationsAsync(RbacDbContext db, CancellationToken ct)
    {
        var applications = new[]
        {
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
                Code = "subscription",
                Name = "Andy Subscription",
                Description = "Subscription management, billing, and entitlements"
            },
            new Application
            {
                Code = "narration",
                Name = "Andy Narration",
                Description = "Text-to-speech narration and audiobook publishing"
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
            case "andy-cli":
                await SeedAndyCliAsync(db, app, ct);
                break;
            case "andy-agentic-web":
                await SeedAndyAgenticWebAsync(db, app, ct);
                break;
            case "subscription":
                await SeedSubscriptionAsync(db, app, ct);
                break;
            case "narration":
                await SeedNarrationAsync(db, app, ct);
                break;
        }

        await db.SaveChangesAsync(ct);
    }

    // SeedAndyDocsAsync: removed — andy-docs now manifest-driven (S1/S2 refactor).

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

    // SeedAndyAuthAsync: removed — andy-auth now manifest-driven.

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

    // SeedCodeIndexAsync: removed — andy-code-index now manifest-driven.

    // SeedContainersAsync: removed — andy-containers now manifest-driven.

    private static async Task SeedSubscriptionAsync(RbacDbContext db, Application app, CancellationToken ct)
    {
        var resourceTypes = new[]
        {
            new ResourceType { ApplicationId = app.Id, Code = "plan", Name = "Subscription Plan", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "subscription", Name = "User Subscription", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "invoice", Name = "Invoice", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "usage", Name = "Usage Record", SupportsInstances = false },
            new ResourceType { ApplicationId = app.Id, Code = "entitlement", Name = "Entitlement", SupportsInstances = false },
            new ResourceType { ApplicationId = app.Id, Code = "addon", Name = "Add-On", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "promo-code", Name = "Promo Code", SupportsInstances = true },
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
            new Role { ApplicationId = app.Id, Code = "admin", Name = "Administrator", Description = "Full access to Subscription management", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "billing-manager", Name = "Billing Manager", Description = "Manage billing and invoices", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "viewer", Name = "Viewer", Description = "Read-only access to subscription data", IsSystem = true },
        };

        foreach (var role in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.ApplicationId == app.Id && r.Code == role.Code, ct))
            {
                db.Roles.Add(role);
            }
        }
    }

    private static async Task SeedNarrationAsync(RbacDbContext db, Application app, CancellationToken ct)
    {
        var resourceTypes = new[]
        {
            new ResourceType { ApplicationId = app.Id, Code = "audio-job", Name = "Audio Job", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "voice", Name = "Voice", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "audio-export", Name = "Audio Export", SupportsInstances = true },
            new ResourceType { ApplicationId = app.Id, Code = "publishing-target", Name = "Publishing Target", SupportsInstances = true },
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
            new Role { ApplicationId = app.Id, Code = "admin", Name = "Administrator", Description = "Full access to Andy Narration", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "narrator", Name = "Narrator", Description = "Can create and manage narration jobs", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "publisher", Name = "Publisher", Description = "Can publish audiobooks to platforms", IsSystem = true },
            new Role { ApplicationId = app.Id, Code = "viewer", Name = "Viewer", Description = "Read-only access to narration data", IsSystem = true },
        };

        foreach (var role in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.ApplicationId == app.Id && r.Code == role.Code, ct))
            {
                db.Roles.Add(role);
            }
        }
    }

    // SeedAndyIssuesAsync, SeedAndyAgentsAsync, SeedAndyTasksAsync: removed —
    // all now manifest-driven via each service's config/registration.json.

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
