namespace Andy.Rbac.Api.Authorization;

public static class RbacAuthenticationSchemes
{
    /// <summary>
    /// Policy scheme that dispatches to <see cref="ApiKeyAuthenticationHandler"/>
    /// when the request carries an API key header, and to JWT bearer otherwise.
    /// Registered as the default authenticate scheme so endpoints accept either
    /// credential without opting in individually.
    /// </summary>
    public const string Default = "RbacDefault";
}
