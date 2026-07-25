using System.Security.Claims;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;

namespace Andy.Rbac.Api.Authorization;

/// <summary>
/// Decides which external group memberships a permission check may consider.
///
/// Issue #45 established that caller-supplied groups must never be trusted
/// wholesale: any principal could otherwise claim arbitrary groups and collect
/// their mapped permissions. The fix made the server derive groups from the
/// validated token, and honour them only when the caller is checking its own
/// subject.
///
/// That closed the escalation but broke the legitimate service-to-service case
/// the clients were built around: a service checking a permission *on behalf of
/// a user* holds that user's group memberships (from its own session/IdP
/// context) and has no way to assert them. <c>RbacHttpClient</c> still sends
/// them, and the server silently dropped them — a false denial with no
/// diagnostic (see the client/server contract mismatch this type resolves).
///
/// The trust rule here:
///   1. Caller is checking itself   → groups come from its validated token.
///   2. Caller is an active Service subject (a manifest-declared M2M principal,
///      seeded by <c>DataSeeder.SeedServicePrincipalGrantsAsync</c>) → its
///      asserted groups are honoured. These principals are already trusted to
///      make authorization decisions for other subjects.
///   3. Anyone else asserting groups for another subject → ignored and logged.
///      A human user's token can never satisfy rule 2, which is the #45 threat.
/// </summary>
public interface ICallerGroupsResolver
{
    Task<IReadOnlyList<string>?> ResolveAsync(
        ClaimsPrincipal? user,
        string subjectExternalId,
        string? selectedProvider,
        IEnumerable<string>? requestedGroups,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CallerGroupsResolver : ICallerGroupsResolver
{
    private readonly RbacDbContext _db;
    private readonly ILogger<CallerGroupsResolver> _logger;

    public CallerGroupsResolver(RbacDbContext db, ILogger<CallerGroupsResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>?> ResolveAsync(
        ClaimsPrincipal? user,
        string subjectExternalId,
        string? selectedProvider,
        IEnumerable<string>? requestedGroups,
        CancellationToken ct = default)
    {
        // Rule 1 — self-check. Groups come from the validated token, never the
        // request body, so a caller cannot inflate its own memberships.
        if (user is not null)
        {
            var ownGroups = TrustedCallerIdentity.GroupsFor(user, subjectExternalId, selectedProvider);
            if (ownGroups is not null)
                return ownGroups;
        }

        var asserted = requestedGroups?
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (asserted is null || asserted.Count == 0)
            return null;

        // Rule 2 — delegated assertion by a trusted service principal.
        if (user is not null && await IsTrustedServicePrincipalAsync(user, ct))
            return asserted;

        // Rule 3 — refuse, and say so. The previous behaviour dropped these
        // silently, which surfaced as an unexplained denial at the caller.
        _logger.LogWarning(
            "Ignoring {GroupCount} caller-asserted group(s) for subject {SubjectExternalId}: " +
            "the caller is neither that subject nor an active service principal.",
            asserted.Count, subjectExternalId);
        return null;
    }

    /// <summary>
    /// A trusted asserter is an active <see cref="SubjectType.Service"/> subject
    /// matching the caller's token identity. Resolution is provider-qualified
    /// wherever the token carries a provider claim, since external IDs are only
    /// unique per provider.
    /// </summary>
    private async Task<bool> IsTrustedServicePrincipalAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var callerExternalId = TrustedCallerIdentity.SubjectId(user);
        if (string.IsNullOrWhiteSpace(callerExternalId))
            return false;

        var resolution = await SubjectResolver.ResolveAsync(
            _db, callerExternalId, TrustedCallerIdentity.Provider(user), tracking: false, ct);

        var caller = resolution.Subject;
        return !resolution.IsAmbiguous
            && caller is { IsActive: true, Type: SubjectType.Service };
    }
}
