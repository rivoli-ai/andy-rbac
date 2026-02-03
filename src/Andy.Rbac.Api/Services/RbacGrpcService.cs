using Andy.Rbac.Grpc;
using Grpc.Core;

namespace Andy.Rbac.Api.Services;

public class RbacGrpcService : RbacService.RbacServiceBase
{
    private readonly IPermissionEvaluator _evaluator;
    private readonly ILogger<RbacGrpcService> _logger;

    public RbacGrpcService(IPermissionEvaluator evaluator, ILogger<RbacGrpcService> logger)
    {
        _evaluator = evaluator;
        _logger = logger;
    }

    public override async Task<CheckPermissionResponse> CheckPermission(
        CheckPermissionRequest request,
        ServerCallContext context)
    {
        var result = await _evaluator.CheckPermissionAsync(
            request.SubjectId,
            request.Permission,
            request.HasResourceInstanceId ? request.ResourceInstanceId : null,
            context.CancellationToken);

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
        var result = await _evaluator.CheckAnyPermissionAsync(
            request.SubjectId,
            request.Permissions,
            request.HasResourceInstanceId ? request.ResourceInstanceId : null,
            context.CancellationToken);

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
        var permissions = await _evaluator.GetPermissionsAsync(
            request.SubjectId,
            request.HasApplicationCode ? request.ApplicationCode : null,
            context.CancellationToken);

        var response = new GetPermissionsResponse();
        response.Permissions.AddRange(permissions);
        return response;
    }

    public override async Task<GetRolesResponse> GetRoles(
        GetRolesRequest request,
        ServerCallContext context)
    {
        var roles = await _evaluator.GetRolesAsync(
            request.SubjectId,
            request.HasApplicationCode ? request.ApplicationCode : null,
            context.CancellationToken);

        var response = new GetRolesResponse();
        response.Roles.AddRange(roles);
        return response;
    }

    // TODO: Implement remaining methods (ProvisionSubject, AssignRole, etc.)
}
