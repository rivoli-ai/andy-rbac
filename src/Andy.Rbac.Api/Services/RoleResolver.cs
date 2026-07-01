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
                .FirstOrDefault(r => r.Application != null && r.Application.Code == applicationCode);
            if (scoped == null)
            {
                return (null, RoleResolutionErrorKind.NotFound,
                    $"Role '{roleCode}' not found in application '{applicationCode}'");
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
