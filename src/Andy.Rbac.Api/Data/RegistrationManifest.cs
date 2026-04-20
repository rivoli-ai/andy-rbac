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
    [property: JsonPropertyName("testUserRole")]    string? TestUserRole
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
