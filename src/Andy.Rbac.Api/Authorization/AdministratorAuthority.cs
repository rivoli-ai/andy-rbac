using System.Security.Claims;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Api.Authorization;

/// <summary>
/// Options for how RBAC administrator authority is established.
/// Section name: <c>Authorization</c>.
/// </summary>
public sealed class AdministratorAuthorityOptions
{
    public const string SectionName = "Authorization";

    /// <summary>
    /// Role codes that confer RBAC administration when held in the RBAC store.
    /// </summary>
    public string[] AdministratorRoles { get; set; } = ["super-admin", "rbac-admin"];

    /// <summary>
    /// Whether an <c>admin</c>/<c>super-admin</c> claim in the caller's token
    /// confers RBAC administration on its own.
    ///
    /// This was previously the ONLY check, and it is unscoped: a token issued
    /// with a generic admin role — or an admin role meant for some other
    /// application — granted full global RBAC administration, letting the
    /// holder create roles, assign super-admin to anyone, and deactivate
    /// subjects. The service that owns the authorization model did not apply
    /// that model to its own privileged surface.
    ///
    /// It remains available as a bootstrap path, because the first
    /// administrator has to be able to grant themselves a role before any
    /// store-backed grant exists. Turn it off once real grants are seeded.
    /// Defaults to true so existing deployments are not locked out by an
    /// upgrade; a warning is logged whenever it is what allowed a request.
    /// </summary>
    public bool AllowClaimBootstrap { get; set; } = true;

    /// <summary>Claim values honoured by the bootstrap path.</summary>
    public string[] BootstrapClaimRoles { get; set; } = ["super-admin", "admin"];
}

/// <summary>
/// Decides whether a caller may administer RBAC.
///
/// Authority comes from andy-rbac's own store: the caller's Subject must hold
/// one of <see cref="AdministratorAuthorityOptions.AdministratorRoles"/>.
/// The token-claim check survives only as an explicitly-configurable bootstrap
/// (see <see cref="AdministratorAuthorityOptions.AllowClaimBootstrap"/>).
/// </summary>
public interface IAdministratorAuthority
{
    Task<bool> IsAdministratorAsync(ClaimsPrincipal user, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AdministratorAuthority : IAdministratorAuthority
{
    private readonly RbacDbContext _db;
    private readonly IPermissionRepository _permissions;
    private readonly AdministratorAuthorityOptions _options;
    private readonly ILogger<AdministratorAuthority> _logger;

    public AdministratorAuthority(
        RbacDbContext db,
        IPermissionRepository permissions,
        IOptions<AdministratorAuthorityOptions> options,
        ILogger<AdministratorAuthority> logger)
    {
        _db = db;
        _permissions = permissions;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> IsAdministratorAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (user.Identity?.IsAuthenticated != true)
            return false;

        var externalId = TrustedCallerIdentity.SubjectId(user);
        if (string.IsNullOrWhiteSpace(externalId))
            return false;

        // Store-backed authority: the caller's own RBAC roles.
        var resolution = await SubjectResolver.ResolveAsync(
            _db, externalId, TrustedCallerIdentity.Provider(user), tracking: false, ct);

        if (!resolution.IsAmbiguous && resolution.Subject is { IsActive: true } subject)
        {
            var roles = await _permissions.GetRolesForSubjectAsync(subject.Id, applicationCode: null, ct);
            if (roles.Any(role => _options.AdministratorRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
                return true;
        }

        if (!_options.AllowClaimBootstrap)
            return false;

        if (!HasBootstrapClaim(user))
            return false;

        // Loud on purpose: this is the unscoped path, and every hit is a
        // deployment that has not yet granted its administrators a real role.
        _logger.LogWarning(
            "Authorized RBAC administration for {ExternalId} via the token-claim bootstrap. " +
            "Grant a store-backed administrator role ({Roles}) and set " +
            "Authorization:AllowClaimBootstrap=false — the claim is not scoped to andy-rbac.",
            externalId, string.Join("/", _options.AdministratorRoles));
        return true;
    }

    private bool HasBootstrapClaim(ClaimsPrincipal user)
    {
        if (_options.BootstrapClaimRoles.Any(user.IsInRole))
            return true;

        return user.Claims
            .Where(c => c.Type is "role" or "roles" or ClaimTypes.Role)
            .SelectMany(c => c.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            .Any(value => _options.BootstrapClaimRoles.Contains(value, StringComparer.OrdinalIgnoreCase));
    }
}
