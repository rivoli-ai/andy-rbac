using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// Resolves the persistence identity of a subject. External IDs are not
/// globally unique; the database key is (Provider, ExternalId). Legacy
/// unqualified callers remain supported only when their ID is unambiguous.
/// </summary>
public static class SubjectResolver
{
    public static async Task<SubjectResolution> ResolveAsync(
        RbacDbContext db,
        string externalId,
        string? provider,
        bool tracking,
        CancellationToken ct)
    {
        IQueryable<Subject> query = db.Subjects;
        if (!tracking)
            query = query.AsNoTracking();

        query = query.Where(s => s.ExternalId == externalId);
        if (!string.IsNullOrWhiteSpace(provider))
        {
            var subject = await query.FirstOrDefaultAsync(s => s.Provider == provider, ct);
            return subject is null
                ? SubjectResolution.NotFound
                : new SubjectResolution(subject, IsAmbiguous: false);
        }

        var matches = await query.Take(2).ToListAsync(ct);
        return matches.Count switch
        {
            0 => SubjectResolution.NotFound,
            1 => new SubjectResolution(matches[0], IsAmbiguous: false),
            _ => new SubjectResolution(null, IsAmbiguous: true)
        };
    }
}

public readonly record struct SubjectResolution(Subject? Subject, bool IsAmbiguous)
{
    public static SubjectResolution NotFound => new(null, IsAmbiguous: false);
}
