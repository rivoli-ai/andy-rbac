using System.Security.Claims;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Middleware;

/// <summary>
/// Lazily provisions a <see cref="Subject"/> row for the authenticated user on
/// first request. Replaces the previous startup-time bulk seed that opened a
/// direct Postgres connection to andy-auth's database.
///
/// Skips:
///   - Anonymous requests.
///   - Service-account tokens (client_credentials grant): andy-auth issues
///     these with sub=client_id and no email claim. We use the absence of
///     the email claim as the signal that this isn't a human user.
///
/// On a hit, refreshes <see cref="Subject.LastSeenAt"/> at most once per
/// 5 minutes per subject — bounded write load on otherwise idle DB.
/// </summary>
public class EnsureSubjectMiddleware
{
    private static readonly TimeSpan LastSeenRefreshInterval = TimeSpan.FromMinutes(5);
    private readonly RequestDelegate _next;

    public EnsureSubjectMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, RbacDbContext db, ILogger<EnsureSubjectMiddleware> logger)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sub = context.User.FindFirst("sub")?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = context.User.FindFirst("email")?.Value
                ?? context.User.FindFirst(ClaimTypes.Email)?.Value;

            if (!string.IsNullOrEmpty(sub) && !string.IsNullOrEmpty(email))
            {
                var existing = await db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == sub);
                if (existing is null)
                {
                    var name = context.User.FindFirst("name")?.Value
                        ?? context.User.FindFirst("preferred_username")?.Value
                        ?? email;
                    db.Subjects.Add(new Subject
                    {
                        ExternalId = sub,
                        Provider = "andy-auth",
                        Type = SubjectType.User,
                        Email = email,
                        DisplayName = name,
                        IsActive = true,
                        LastSeenAt = DateTimeOffset.UtcNow,
                    });
                    await db.SaveChangesAsync();
                    logger.LogInformation("Auto-provisioned Subject for {Email} ({Sub}).", email, sub);
                }
                else if (existing.LastSeenAt is null
                    || DateTimeOffset.UtcNow - existing.LastSeenAt.Value > LastSeenRefreshInterval)
                {
                    existing.LastSeenAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
        }

        await _next(context);
    }
}
