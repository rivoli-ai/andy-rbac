namespace Andy.Rbac.Api.Services;

/// <summary>
/// The request conflicts with existing state — a duplicate code, or a delete
/// blocked by a dependent row. Controllers map this to 409.
///
/// Issue #118: these cases previously reached the database as-is and surfaced
/// as an unhandled <c>DbUpdateException</c> (HTTP 500 with a stack trace) for
/// what is an ordinary, actionable client mistake. A distinct type keeps the
/// mapping off the "does the message start with Error:" convention that #117
/// tracks.
/// </summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
