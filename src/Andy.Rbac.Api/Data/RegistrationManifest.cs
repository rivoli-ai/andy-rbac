// Copyright (c) Rivoli AI 2026. All rights reserved.
using System.Text.Json.Serialization;

namespace Andy.Rbac.Api.Data;

/// <summary>
/// Wire format of config/registration.json files produced by the andy-service-template.
/// Every Andy ecosystem service ships one; andy-auth, andy-rbac, and andy-settings
/// each read their relevant section on startup and seed from it.
///
/// Schema: ../../andy-service-template/docs/registration.schema.json
/// </summary>
public sealed record RegistrationManifest(
    [property: JsonPropertyName("service")]  RegistrationServiceInfo Service,
    [property: JsonPropertyName("auth")]     RegistrationAuthInfo?   Auth,
    [property: JsonPropertyName("rbac")]     RegistrationRbacInfo?   Rbac,
    [property: JsonPropertyName("settings")] RegistrationSettingsInfo? Settings
);

public sealed record RegistrationServiceInfo(
    [property: JsonPropertyName("name")]                string Name,
    [property: JsonPropertyName("displayName")]         string DisplayName,
    [property: JsonPropertyName("description")]         string Description,
    [property: JsonPropertyName("embeddedProxyPrefix")] string EmbeddedProxyPrefix
);

public sealed record RegistrationAuthInfo(
    [property: JsonPropertyName("audience")] string Audience
);

public sealed record RegistrationRbacInfo(
    [property: JsonPropertyName("applicationCode")] string ApplicationCode,
    [property: JsonPropertyName("applicationName")] string ApplicationName,
    [property: JsonPropertyName("description")]     string? Description,
    [property: JsonPropertyName("resourceTypes")]   RegistrationResourceType[]? ResourceTypes,
    [property: JsonPropertyName("roles")]           RegistrationRole[]? Roles,
    [property: JsonPropertyName("testUserRole")]    string? TestUserRole,
    [property: JsonPropertyName("servicePrincipal")] RegistrationServicePrincipal? ServicePrincipal
);

/// <summary>
/// Declares the cross-service permissions a service's machine-to-machine
/// (client_credentials) principal needs. andy-auth issues these tokens with
/// <c>sub = clientId</c> and no <c>email</c> claim, so they are NOT auto-
/// provisioned as RBAC subjects (see EnsureSubjectMiddleware). Without an
/// explicit grant every service-to-service <c>[RequirePermission]</c> call
/// 403s. The seeder provisions a <see cref="Andy.Rbac.Models.SubjectType.Service"/>
/// subject for <see cref="ClientId"/> and binds it (via a generated
/// <c>service:{clientId}</c> role) to each fully-qualified permission code in
/// <see cref="Permissions"/> (e.g. <c>"andy-agents:agent:read"</c>). The
/// permission must be one the OWNING service's manifest declares — least
/// privilege, no blanket service role.
/// </summary>
public sealed record RegistrationServicePrincipal(
    [property: JsonPropertyName("clientId")]    string ClientId,
    [property: JsonPropertyName("permissions")] string[]? Permissions
);

public sealed record RegistrationResourceType(
    [property: JsonPropertyName("code")]              string Code,
    [property: JsonPropertyName("name")]              string Name,
    [property: JsonPropertyName("supportsInstances")] bool? SupportsInstances
);

public sealed record RegistrationRole(
    [property: JsonPropertyName("code")]        string Code,
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("isSystem")]    bool? IsSystem
);

public sealed record RegistrationSettingsInfo();
