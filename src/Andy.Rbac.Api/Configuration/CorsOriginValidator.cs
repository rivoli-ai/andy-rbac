namespace Andy.Rbac.Api.Configuration;

/// <summary>
/// Issue #50 — startup-time check that the CORS origin list is sane for the
/// current environment. The default CORS policy combines <c>WithOrigins</c>
/// with <c>AllowCredentials()</c>; a wildcard origin in that combination is
/// always wrong (browsers reject it) and an empty list in Production silently
/// blocks legitimate clients while masking misconfiguration. Fail closed at
/// startup instead.
/// </summary>
public static class CorsOriginValidator
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if <paramref name="origins"/>
    /// contains any wildcard (or is empty in non-Development environments).
    /// In Development, an empty list is allowed; the policy registration falls
    /// back to a localhost default.
    /// </summary>
    public static void Validate(IReadOnlyList<string> origins, bool isDevelopment)
    {
        foreach (var origin in origins)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                throw new InvalidOperationException(
                    "Cors:Origins contains a blank entry. List exact origins only.");
            }

            if (origin.Contains('*'))
            {
                throw new InvalidOperationException(
                    $"Cors:Origins entry '{origin}' contains a wildcard. " +
                    "Wildcards are incompatible with AllowCredentials and are rejected by browsers; " +
                    "list exact origins only.");
            }
        }

        if (!isDevelopment && origins.Count == 0)
        {
            throw new InvalidOperationException(
                "Cors:Origins must be configured in non-Development environments.");
        }
    }
}
