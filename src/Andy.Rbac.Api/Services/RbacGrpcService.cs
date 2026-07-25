using Andy.Rbac.Grpc;
using Grpc.Core;
using Andy.Rbac.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Andy.Rbac.Api.Services;

public class RbacGrpcService : RbacService.RbacServiceBase
{
    private readonly IPermissionEvaluator _evaluator;
    private readonly ILogger<RbacGrpcService> _logger;
    private readonly ISubjectService? _subjectService;
    private readonly IRoleService? _roleService;
    private readonly IResourceInstanceService? _resourceInstanceService;
    private readonly IAuthorizationService? _authorizationService;

    public RbacGrpcService(
        IPermissionEvaluator evaluator,
        ILogger<RbacGrpcService> logger,
        ISubjectService? subjectService = null,
        IRoleService? roleService = null,
        IResourceInstanceService? resourceInstanceService = null,
        IAuthorizationService? authorizationService = null)
    {
        _evaluator = evaluator;
        _logger = logger;
        _subjectService = subjectService;
        _roleService = roleService;
        _resourceInstanceService = resourceInstanceService;
        _authorizationService = authorizationService;
    }

    public override async Task<CheckPermissionResponse> CheckPermission(
        CheckPermissionRequest request,
        ServerCallContext context)
    {
        var user = GetUser(context);
        var provider = user is null
            ? request.HasSubjectProvider ? request.SubjectProvider : null
            : TrustedCallerIdentity.EffectiveProvider(
                user, request.SubjectId, request.HasSubjectProvider ? request.SubjectProvider : null);
        var groups = user is null ? null : TrustedCallerIdentity.GroupsFor(user, request.SubjectId, provider);
        var result = !string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.CheckPermissionForProviderAsync(
                request.SubjectId, provider, request.Permission, groups,
                request.HasResourceInstanceId ? request.ResourceInstanceId : null, context.CancellationToken)
            : await _evaluator.CheckPermissionAsync(
                request.SubjectId, request.Permission, groups,
                request.HasResourceInstanceId ? request.ResourceInstanceId : null, context.CancellationToken);

        return new CheckPermissionResponse
        {
            Allowed = result.Allowed,
            Reason = result.Reason ?? ""
        };
    }

    public override async Task<CheckPermissionResponse> CheckAnyPermission(
        CheckAnyPermissionRequest request,
        ServerCallContext context)
    {
        var user = GetUser(context);
        var provider = user is null
            ? request.HasSubjectProvider ? request.SubjectProvider : null
            : TrustedCallerIdentity.EffectiveProvider(
                user, request.SubjectId, request.HasSubjectProvider ? request.SubjectProvider : null);
        var groups = user is null ? null : TrustedCallerIdentity.GroupsFor(user, request.SubjectId, provider);
        var result = !string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.CheckAnyPermissionForProviderAsync(
                request.SubjectId, provider, request.Permissions, groups,
                request.HasResourceInstanceId ? request.ResourceInstanceId : null, context.CancellationToken)
            : await _evaluator.CheckAnyPermissionAsync(
                request.SubjectId, request.Permissions, groups,
                request.HasResourceInstanceId ? request.ResourceInstanceId : null, context.CancellationToken);

        return new CheckPermissionResponse
        {
            Allowed = result.Allowed,
            Reason = result.Reason ?? ""
        };
    }

    public override async Task<GetPermissionsResponse> GetPermissions(
        GetPermissionsRequest request,
        ServerCallContext context)
    {
        var user = GetUser(context);
        var provider = user is null
            ? request.HasSubjectProvider ? request.SubjectProvider : null
            : TrustedCallerIdentity.EffectiveProvider(
                user, request.SubjectId, request.HasSubjectProvider ? request.SubjectProvider : null);
        var groups = user is null ? null : TrustedCallerIdentity.GroupsFor(user, request.SubjectId, provider);
        var permissions = !string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.GetPermissionsForProviderAsync(
                request.SubjectId, provider, groups,
                request.HasApplicationCode ? request.ApplicationCode : null, context.CancellationToken)
            : await _evaluator.GetPermissionsAsync(
                request.SubjectId, groups,
                request.HasApplicationCode ? request.ApplicationCode : null, context.CancellationToken);

        var response = new GetPermissionsResponse();
        response.Permissions.AddRange(permissions);
        return response;
    }

    public override async Task<GetRolesResponse> GetRoles(
        GetRolesRequest request,
        ServerCallContext context)
    {
        var user = GetUser(context);
        var provider = user is null
            ? request.HasSubjectProvider ? request.SubjectProvider : null
            : TrustedCallerIdentity.EffectiveProvider(
                user, request.SubjectId, request.HasSubjectProvider ? request.SubjectProvider : null);
        var groups = user is null ? null : TrustedCallerIdentity.GroupsFor(user, request.SubjectId, provider);
        var roles = !string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.GetRolesForProviderAsync(
                request.SubjectId, provider, groups,
                request.HasApplicationCode ? request.ApplicationCode : null, context.CancellationToken)
            : await _evaluator.GetRolesAsync(
                request.SubjectId, groups,
                request.HasApplicationCode ? request.ApplicationCode : null, context.CancellationToken);

        var response = new GetRolesResponse();
        response.Roles.AddRange(roles);
        return response;
    }

    public override async Task<SubjectResponse> ProvisionSubject(
        ProvisionSubjectRequest request, ServerCallContext context)
    {
        await EnsureAdministratorAsync(context);
        var service = _subjectService ?? throw Unavailable("Subject service is unavailable");
        var metadata = request.Metadata.Count == 0
            ? null
            : request.Metadata.ToDictionary(pair => pair.Key, pair => (object)pair.Value);
        var result = await service.UpsertAsync(
            request.ExternalId, request.Provider,
            request.HasEmail ? request.Email : null,
            request.HasDisplayName ? request.DisplayName : null,
            metadata,
            context.CancellationToken);
        var subject = result.Subject;
        return new SubjectResponse
        {
            Id = subject.Id.ToString(),
            ExternalId = subject.ExternalId,
            Provider = subject.Provider,
            Email = subject.Email ?? string.Empty,
            DisplayName = subject.DisplayName ?? string.Empty,
            IsActive = subject.IsActive
        };
    }

    public override async Task<AssignRoleResponse> AssignRole(
        AssignRoleRequest request, ServerCallContext context)
    {
        await EnsureAdministratorAsync(context);
        var service = _roleService ?? throw Unavailable("Role service is unavailable");
        DateTimeOffset? expiresAt = request.HasExpiresAtUnix
            ? DateTimeOffset.FromUnixTimeSeconds(request.ExpiresAtUnix)
            : null;
        var result = request.HasSubjectProvider
            ? await service.AssignToSubjectForProviderWithExpiryAsync(
                request.SubjectId, request.SubjectProvider, request.RoleCode,
                request.HasResourceInstanceId ? request.ResourceInstanceId : null,
                request.HasApplicationCode ? request.ApplicationCode : null,
                expiresAt, context.CancellationToken)
            : await service.AssignToSubjectWithExpiryAsync(
                request.SubjectId, request.RoleCode,
                request.HasResourceInstanceId ? request.ResourceInstanceId : null,
                request.HasApplicationCode ? request.ApplicationCode : null,
                expiresAt, context.CancellationToken);
        return new AssignRoleResponse { Success = result.Succeeded, Message = result.Message };
    }

    public override async Task<RevokeRoleResponse> RevokeRole(
        RevokeRoleRequest request, ServerCallContext context)
    {
        await EnsureAdministratorAsync(context);
        var service = _roleService ?? throw Unavailable("Role service is unavailable");
        var result = request.HasSubjectProvider
            ? await service.RevokeFromSubjectForProviderAsync(
                request.SubjectId, request.SubjectProvider, request.RoleCode,
                request.HasResourceInstanceId ? request.ResourceInstanceId : null,
                request.HasApplicationCode ? request.ApplicationCode : null,
                context.CancellationToken)
            : await service.RevokeFromSubjectAsync(
                request.SubjectId, request.RoleCode,
                request.HasResourceInstanceId ? request.ResourceInstanceId : null,
                request.HasApplicationCode ? request.ApplicationCode : null,
                context.CancellationToken);
        return new RevokeRoleResponse { Success = result.Succeeded, Message = result.Message };
    }

    public override async Task<GrantInstancePermissionResponse> GrantInstancePermission(
        GrantInstancePermissionRequest request, ServerCallContext context)
    {
        await EnsureAdministratorAsync(context);
        var service = _resourceInstanceService ?? throw Unavailable("Resource instance service is unavailable");
        var result = await service.GrantAsync(
            request.ApplicationCode, request.ResourceTypeCode, request.ResourceInstanceId,
            request.SubjectId, request.HasSubjectProvider ? request.SubjectProvider : null,
            request.Action,
            request.HasExpiresAtUnix ? DateTimeOffset.FromUnixTimeSeconds(request.ExpiresAtUnix) : null,
            context.CancellationToken);
        return new GrantInstancePermissionResponse { Success = result.Success, Message = result.Error ?? "Permission granted" };
    }

    public override async Task<RevokeInstancePermissionResponse> RevokeInstancePermission(
        RevokeInstancePermissionRequest request, ServerCallContext context)
    {
        await EnsureAdministratorAsync(context);
        var service = _resourceInstanceService ?? throw Unavailable("Resource instance service is unavailable");
        var result = await service.RevokeAsync(
            request.ApplicationCode, request.ResourceTypeCode, request.ResourceInstanceId,
            request.SubjectId, request.HasSubjectProvider ? request.SubjectProvider : null,
            request.Action,
            GetUser(context) is { } caller ? TrustedCallerIdentity.SubjectId(caller) : null,
            context.CancellationToken);
        return new RevokeInstancePermissionResponse { Success = result.Success, Message = result.Error ?? "Permission revoked" };
    }

    private static ClaimsPrincipal? GetUser(ServerCallContext context)
    {
        try
        {
            return context.GetHttpContext().User;
        }
        catch (InvalidOperationException)
        {
            // Unit/in-process contexts need not carry an ASP.NET HttpContext.
            return null;
        }
    }

    /// <summary>
    /// Gates the mutating RPCs on the Administrator policy.
    ///
    /// Fails closed: a missing <see cref="IAuthorizationService"/> denies rather
    /// than allows. It previously returned early, so the consequence of a DI
    /// mistake, a refactor, or a hand-constructed instance was unauthenticated
    /// access to AssignRole/RevokeRole/ProvisionSubject/Grant*, rather than an
    /// outage. Unit tests that exercise these RPCs pass an explicit permissive
    /// authorization service.
    /// </summary>
    private async Task EnsureAdministratorAsync(ServerCallContext context)
    {
        if (_authorizationService is null)
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "RBAC administrator privileges are required"));
        }

        // An absent HttpContext yields an anonymous principal, which the
        // Administrator policy rejects on RequireAuthenticatedUser — the
        // decision stays with the policy rather than being duplicated here.
        var user = GetUser(context) ?? new ClaimsPrincipal(new ClaimsIdentity());

        var result = await _authorizationService.AuthorizeAsync(
            user, resource: null, RbacAuthorizationPolicies.Administrator);
        if (!result.Succeeded)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "RBAC administrator privileges are required"));
    }

    private static RpcException Unavailable(string message) =>
        new(new Status(StatusCode.Unavailable, message));
}
