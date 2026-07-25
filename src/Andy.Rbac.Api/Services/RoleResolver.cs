using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// How a role-by-code resolution failed.
/// </summary>
public enum RoleResolutionErrorKind
{
    /// <summary>No role matched (optionally within the requested application).</summary>
    NotFound,

    /// <summary>
    /// The bare role code matched roles in more than one application and no
    /// applicationCode was given to disambiguate.
    /// </summary>
    Ambiguous
}

/// <summary>
/// Shared (roleCode, applicationCode) → Role resolution used by every
/// assign/revoke path (subjects, teams, service layer, MCP tools).
///
/// Role codes are NOT globally unique — the same code (e.g. "admin") exists
/// once per application — so a code-only lookup is only honoured when it is
/// unambiguous. An ambiguous code without an application scope is an error
/// listing the candidate applications; we never silently bind an arbitrary
/// application's role.
///
/// An <c>applicationCode</c> selects the application-scoped role when one
/// exists and otherwise falls back to the global role of that code (roles with
/// <c>ApplicationId == null</c>, e.g. the seeded <c>super-admin</c>/<c>user</c>).
/// Requiring an exact application match instead made every global role
/// unassignable through <c>RbacHttpClient</c>, which always sends its
/// configured application code, and left an ambiguous-but-also-global code with
/// no selectable value at all. The same "scoped first, then global" precedence
/// is what <c>RoleService.GetAllAsync</c> and
/// <c>PermissionRepository.GetRolesForSubjectAsync</c> already apply when
/// filtering by application.
/// </summary>
public static class RoleResolver
{
    public static async Task<(Role? Role, RoleResolutionErrorKind Kind, string? Error)> ResolveAsync(
        RbacDbContext db, string roleCode, string? applicationCode, CancellationToken ct)
    {
        var candidates = await db.Roles
            .Include(r => r.Application)
            .Where(r => r.Code == roleCode)
            .ToListAsync(ct);

        if (!string.IsNullOrEmpty(applicationCode))
        {
            var scoped = candidates
                .FirstOrDefault(r => r.Application != null && r.Application.Code == applicationCode)
                ?? candidates.FirstOrDefault(r => r.ApplicationId == null);
            if (scoped == null)
            {
                return (null, RoleResolutionErrorKind.NotFound,
                    $"Role '{roleCode}' not found in application '{applicationCode}' or in the global scope");
            }

            return (scoped, default, null);
        }

        if (candidates.Count == 0)
            return (null, RoleResolutionErrorKind.NotFound, $"Role '{roleCode}' not found");

        if (candidates.Count > 1)
        {
            var apps = candidates
                .Select(r => r.Application?.Code ?? "(global)")
                .OrderBy(c => c, StringComparer.Ordinal);
            return (null, RoleResolutionErrorKind.Ambiguous,
                $"Role code '{roleCode}' is ambiguous across applications: {string.Join(", ", apps)}. " +
                "Specify applicationCode to select the intended role.");
        }

        return (candidates[0], default, null);
    }
}
