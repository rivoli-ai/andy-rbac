namespace Andy.Rbac.Api.Services;

/// <summary>
/// How a role/team mutation ended.
/// </summary>
public enum MutationOutcome
{
    /// <summary>The mutation was applied, or the desired state already held.</summary>
    Ok,

    /// <summary>A referenced subject, team or role does not exist.</summary>
    NotFound,

    /// <summary>An identifier matched more than one row and needs scoping.</summary>
    Ambiguous,

    /// <summary>The request was well-formed but cannot be applied as asked.</summary>
    Invalid
}

/// <summary>
/// Result of a role/team mutation.
///
/// Issue #117: these operations used to return prose, and callers branched on
/// whether it started with "Error:" — <c>RolesController</c> chose its status
/// code that way and <c>RbacGrpcService</c> derived its <c>Success</c> flag from
/// it. Rewording a message, or ever localising one, would silently turn a 400
/// into a 200 and a failed RPC into a successful one. The outcome is now typed;
/// <see cref="Message"/> is for humans only and carries no control flow.
///
/// The newer services already worked this way — see
/// <c>ResourceInstanceMutationResult</c> and <c>RevokeGrantResult</c>.
/// </summary>
/// <param name="Outcome">Machine-readable result.</param>
/// <param name="Message">Human-readable detail. Never parsed.</param>
public readonly record struct MutationResult(MutationOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == MutationOutcome.Ok;

    public static MutationResult Ok(string message) => new(MutationOutcome.Ok, message);
    public static MutationResult NotFound(string message) => new(MutationOutcome.NotFound, message);
    public static MutationResult Ambiguous(string message) => new(MutationOutcome.Ambiguous, message);
    public static MutationResult Invalid(string message) => new(MutationOutcome.Invalid, message);
}
