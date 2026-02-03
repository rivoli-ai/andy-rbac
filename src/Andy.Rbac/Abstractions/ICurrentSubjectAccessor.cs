namespace Andy.Rbac.Abstractions;

/// <summary>
/// Provides access to the current authenticated subject.
/// </summary>
public interface ICurrentSubjectAccessor
{
    /// <summary>
    /// Gets the external ID of the current subject from the authentication context.
    /// Returns null if not authenticated.
    /// </summary>
    string? GetSubjectId();

    /// <summary>
    /// Gets the provider of the current subject (e.g., "andy-auth", "azure-ad").
    /// </summary>
    string? GetProvider();

    /// <summary>
    /// Gets the email of the current subject if available.
    /// </summary>
    string? GetEmail();

    /// <summary>
    /// Gets the display name of the current subject if available.
    /// </summary>
    string? GetDisplayName();

    /// <summary>
    /// Gets all claims for the current subject.
    /// </summary>
    IReadOnlyDictionary<string, string> GetClaims();

    /// <summary>
    /// Whether the current request is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }
}
