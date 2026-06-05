using Andy.Rbac.Api.Data;
using Andy.Rbac.Api.Mcp;
using Andy.Rbac.Api.Middleware;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Api.Telemetry;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Messaging;
using Andy.Rbac.Infrastructure.Repositories;
using Andy.Rbac.Messaging;
using Andy.Telemetry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore.Authentication;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Andy RBAC API", Version = "v1" });
});

// Add gRPC
builder.Services.AddGrpc();

// Add database
//
// Provider switch (PostgreSQL for Docker / hosted, SQLite for embedded
// Conductor): the active provider is selected by the `Database:Provider`
// configuration key. `appsettings.json` pins it to PostgreSql so the
// historic deployment paths are unchanged; Conductor's embedded launcher
// overrides it via the `Database__Provider=Sqlite` env var.
var dbProvider = DatabaseProviderExtensions.GetDatabaseProvider(builder.Configuration);
var dbConnectionString = DatabaseProviderExtensions.ResolveConnectionString(builder.Configuration, dbProvider);

builder.Services.AddDbContext<RbacDbContext>(options =>
{
    DatabaseProviderExtensions.ConfigureDbContext(options, dbProvider, dbConnectionString);
});

// Add repositories
builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();

// Add services
builder.Services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<ISubjectService, SubjectService>();
builder.Services.AddScoped<IPolicyService, PolicyService>();
// SM.2.11 — grant lifecycle (admin revoke + server-side expiry push).
builder.Services.AddScoped<IGrantService, GrantService>();
builder.Services.Configure<GrantExpiryWorkerOptions>(
    builder.Configuration.GetSection(GrantExpiryWorkerOptions.SectionName));
builder.Services.AddHostedService<GrantExpiryWorker>();

// Epic AL — NATS messaging substrate.
//
// AL1: IMessageBus (NATS or in-memory). The InMemoryMessageBus default
//      keeps tests + the embedded Conductor launcher running without
//      needing nats-server up. Production wiring binds Messaging:Nats
//      and swaps in NatsMessageBus + NatsStreamProvisioner.
// AL2: OutboxDispatcher background worker drains the OutboxEntry table
//      to whichever IMessageBus is registered.
// AL3 + AL4: IRbacEventPublisher stages outbox rows for Role/SubjectRole
//      events; RoleService is the only caller today.
builder.Services.Configure<NatsOptions>(builder.Configuration.GetSection(NatsOptions.SectionName));
builder.Services.Configure<OutboxDispatcherOptions>(builder.Configuration.GetSection(OutboxDispatcherOptions.SectionName));

var natsUrl = builder.Configuration[$"{NatsOptions.SectionName}:Url"];
if (!string.IsNullOrWhiteSpace(natsUrl))
{
    builder.Services.AddSingleton<IMessageBus, NatsMessageBus>();
    builder.Services.AddHostedService<NatsStreamProvisioner>();
}
else
{
    builder.Services.AddSingleton<IMessageBus, InMemoryMessageBus>();
}

builder.Services.AddScoped<IRbacEventPublisher, RbacEventPublisher>();
builder.Services.AddHostedService<OutboxDispatcher>();

// Add MCP Server for AI assistant integration
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddScoped<RbacMcpTools>();

// Add HttpClient for DCR proxy
builder.Services.AddHttpClient();

// Configure MCP server URL - must be the actual public URL for the deployment.
// Sourced from .env / compose env (ANDY_RBAC_API_BASE_URL → Mcp__ServerUrl).
var serverUrl = builder.Configuration["Mcp:ServerUrl"]
    ?? throw new InvalidOperationException("Mcp:ServerUrl is required (set via ANDY_RBAC_API_BASE_URL or Mcp__ServerUrl env var).");
var mcpPath = builder.Configuration["Mcp:McpPath"] ?? "/mcp";
var protectedResourceUrl = $"{serverUrl}{mcpPath}";

// Configure Andy.Auth authority. Sourced from .env / compose
// (ANDY_AUTH_AUTHORITY → AndyAuth__Authority / Auth__Authority).
var andyAuthAuthority = builder.Configuration["AndyAuth:Authority"]
    ?? builder.Configuration["Auth:Authority"]
    ?? throw new InvalidOperationException("AndyAuth:Authority is required (set via ANDY_AUTH_AUTHORITY env var).");

// Add authentication (integrate with andy-auth)
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        // Match the fallback chain used by `andyAuthAuthority` above
        // (AndyAuth:Authority → Auth:Authority). Reading only
        // `Auth:Authority` here left JwtBearer's Authority null when the
        // env var was passed as `AndyAuth__Authority` (Conductor's
        // embedded launcher), and the resulting JWT validation failed with
        // `IDX10204: ValidIssuer is null or whitespace` on every request —
        // andy-rbac silently 401'd every authenticated downstream call
        // (and so every Conductor panel that depends on RBAC checks).
        options.Authority = andyAuthAuthority;
        options.Audience = builder.Configuration["AndyAuth:Audience"]
            ?? builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = builder.Configuration.GetValue<bool?>("AndyAuth:RequireHttpsMetadata")
            ?? builder.Configuration.GetValue<bool?>("Auth:RequireHttpsMetadata")
            ?? !builder.Environment.IsDevelopment();
        if (builder.Environment.IsDevelopment())
        {
            options.BackchannelHttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
        }
    })
    .AddMcp(options =>
    {
        // Configure OAuth Protected Resource Metadata (RFC 8707)
        options.ResourceMetadataUri = new Uri($"{serverUrl}/mcp/.well-known/oauth-protected-resource");
        options.ResourceMetadata = new()
        {
            Resource = new Uri(protectedResourceUrl),
            ResourceDocumentation = new Uri("https://github.com/rivoli-ai/andy-rbac"),
            // Point to Andy.Auth as the authorization server
            AuthorizationServers = { new Uri(andyAuthAuthority) },
            ScopesSupported = ["openid", "profile", "email"],
        };

        // Log when metadata is served
        options.Events.OnResourceMetadataRequest = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var meta = context.ResourceMetadata;
            logger.LogInformation("MCP ResourceMetadata requested. Resource={Resource} AuthServers={AuthServers}",
                meta?.Resource, meta is null ? "<null>" : string.Join(",", meta.AuthorizationServers.Select(a => a.ToString())));
            return Task.CompletedTask;
        };
    });

// Post-configure JWT bearer to accept MCP resource URLs as valid audiences
builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var existingAudiences = options.TokenValidationParameters.ValidAudiences?.ToList() ?? new List<string>();
    if (!string.IsNullOrEmpty(options.TokenValidationParameters.ValidAudience) &&
        !existingAudiences.Contains(options.TokenValidationParameters.ValidAudience))
    {
        existingAudiences.Add(options.TokenValidationParameters.ValidAudience);
    }

    // Add MCP resource URLs as valid audiences
    existingAudiences.Add(protectedResourceUrl);

    options.TokenValidationParameters.ValidAudiences = existingAudiences;
    options.TokenValidationParameters.ValidAudience = null;  // Use ValidAudiences instead
});

// Override default authentication schemes for MCP challenge
builder.Services.Configure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
{
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
});

builder.Services.AddAuthorization();

// --- OpenTelemetry (via Andy.Telemetry) ---
// OT4 (rivoli-ai/conductor#1262): andy-rbac ships zero OTel DLLs today.
// Wire OTLP export to Conductor's local receiver at :4318. The Conductor
// embedded launcher sets OTEL_EXPORTER_OTLP_ENDPOINT/_PROTOCOL/_SERVICE_NAME
// (see Conductor/Core/ServiceHost/Services/RbacServiceConfig.swift); the
// AndyTelemetry config block is the fallback for non-Conductor launches.
//
// Conductor's UnifiedProxy already emits server-side request spans, so
// EnableAspNetCoreInstrumentation stays off here to avoid double-counting.
builder.Services.AddAndyTelemetry(builder.Configuration, o =>
{
    if (string.IsNullOrWhiteSpace(o.ServiceName))
        o.ServiceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "andy-rbac";
    if (string.IsNullOrWhiteSpace(o.OtlpEndpoint))
        o.OtlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
    if (string.IsNullOrWhiteSpace(o.Protocol) || o.Protocol == "grpc")
    {
        var envProtocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL");
        if (!string.IsNullOrWhiteSpace(envProtocol))
            o.Protocol = envProtocol;
    }
    o.ActivitySources.Add(RbacTelemetry.ActivitySourceName);
    o.Meters.Add(RbacTelemetry.MeterName);
    o.EnableAspNetCoreInstrumentation = false;
});
// EF Core tracing is service-specific (not bundled in Andy.Telemetry).
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddEntityFrameworkCoreInstrumentation());

// Add CORS — fail closed on misconfigured origin lists (issue #50). Wildcards
// in an AllowCredentials policy are silently rejected by browsers anyway; an
// empty list in Production usually means a deploy-time config drift. Throwing
// at startup is louder than logging.
var configuredCorsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
Andy.Rbac.Api.Configuration.CorsOriginValidator.Validate(
    configuredCorsOrigins, builder.Environment.IsDevelopment());
var effectiveCorsOrigins = configuredCorsOrigins.Length > 0
    ? configuredCorsOrigins
    : new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(effectiveCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });

    // Allow MCP clients (Claude Desktop, Cursor, etc.) to access /mcp endpoints.
    // No AllowCredentials here — wildcard origin only works without credentials.
    options.AddPolicy("AllowMcpClients", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure middleware
// HC.8.1 of rivoli-ai/conductor#1245: expose the OpenAPI
// document in every environment so Conductor's in-app Help Center
// can ingest /openapi.json from the bundled service. The Swagger
// UI itself stays development-only.
app.UseSwagger();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI();
}
// Stable alias so every andy-* service exposes the same
// path. HC.8.1 of rivoli-ai/conductor#1245.
app.MapGet("/openapi.json", () => Results.Redirect("/swagger/v1/swagger.json"))
    .ExcludeFromDescription();

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<EnsureSubjectMiddleware>();

app.MapControllers().RequireAuthorization();
app.MapGrpcService<RbacGrpcService>().RequireAuthorization();

// Map MCP Server endpoint at /mcp with permissive CORS for MCP clients
// Require authorization so clients (e.g., Claude Desktop) receive an OAuth challenge
app.MapMcp("/mcp")
    .RequireCors("AllowMcpClients")
    .RequireAuthorization();

// JSON options for OAuth metadata - omit null values per RFC 8707
var oauthMetadataJsonOptions = new System.Text.Json.JsonSerializerOptions
{
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower
};

// Serve protected resource metadata under /mcp/.well-known for MCP clients
app.MapGet("/mcp/.well-known/oauth-protected-resource", (IServiceProvider sp) =>
{
    var optionsMonitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<McpAuthenticationOptions>>();
    var options = optionsMonitor.Get(McpAuthenticationDefaults.AuthenticationScheme);
    return Results.Json(options.ResourceMetadata, oauthMetadataJsonOptions);
})
.AllowAnonymous()
.RequireCors("AllowMcpClients");

// Serve protected resource metadata at the default root path
app.MapGet("/.well-known/oauth-protected-resource", (IServiceProvider sp) =>
{
    var optionsMonitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<McpAuthenticationOptions>>();
    var options = optionsMonitor.Get(McpAuthenticationDefaults.AuthenticationScheme);
    return Results.Json(options.ResourceMetadata, oauthMetadataJsonOptions);
})
.AllowAnonymous()
.RequireCors("AllowMcpClients");

// OpenID Configuration - redirect to Andy.Auth
app.MapGet("/.well-known/openid-configuration", () =>
    Results.Redirect($"{andyAuthAuthority}/.well-known/openid-configuration", permanent: false))
    .AllowAnonymous()
    .RequireCors("AllowMcpClients");

app.MapGet("/.well-known/oauth-authorization-server", () =>
    Results.Redirect($"{andyAuthAuthority}/.well-known/openid-configuration", permanent: false))
    .AllowAnonymous()
    .RequireCors("AllowMcpClients");

// Redirect authorization and token endpoints to Andy.Auth
app.MapGet("/authorize", (HttpContext ctx) =>
{
    var qs = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : string.Empty;
    return Results.Redirect($"{andyAuthAuthority}/connect/authorize{qs}", permanent: false);
})
    .AllowAnonymous()
    .RequireCors("AllowMcpClients");

app.MapPost("/token", (HttpContext ctx) =>
{
    var qs = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : string.Empty;
    ctx.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
    ctx.Response.Headers.Location = $"{andyAuthAuthority}/connect/token{qs}";
    return Task.CompletedTask;
})
    .AllowAnonymous()
    .RequireCors("AllowMcpClients");

// Health check
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }))
    .AllowAnonymous();

// --- Prometheus metrics scraping (via Andy.Telemetry) ---
// OT4 (rivoli-ai/conductor#1262). Exposes /metrics for the Conductor
// scraper; OTLP push is independent.
app.MapAndyTelemetry();

// Apply migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();

    if (app.Environment.IsDevelopment())
    {
        // Schema bootstrap differs by provider:
        //   - PostgreSQL: apply EF migrations (committed under Data/Migrations/).
        //   - SQLite: use `EnsureCreated` so a fresh embedded install gets a
        //     schema generated from the current EF model. SQLite migrations
        //     are tracked separately under G2.1.
        if (dbProvider == DatabaseProvider.Sqlite)
        {
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            await db.Database.MigrateAsync();
        }
    }

    // Seed initial data
    await DataSeeder.SeedAsync(db);

    // Manifest-driven app + roles + resource types (reads config/registration.json
    // files from sibling services via REGISTRATIONS__MANIFEST_PATHS env var or
    // Registrations:ManifestPaths config). Must run AFTER SeedAsync so the global
    // roles and actions exist for FK references.
    await DataSeeder.SeedFromManifestsAsync(db, app.Configuration, app.Logger);

    // Seed application-specific data for the consumer apps and out-of-scope
    // services that don't yet ship a registration manifest. The 10 in-scope
    // Andy services (auth/rbac/docs/code-index/containers/issues/agents/tasks/
    // policies/models) are handled by SeedFromManifestsAsync above.
    foreach (var appCode in new[] { "andy-cli", "andy-agentic-web", "narration", "subscription" })
    {
        await DataSeeder.SeedApplicationDataAsync(db, appCode);
    }

    // Seed super-admin permissions for all resource types
    await DataSeeder.SeedSuperAdminPermissionsAsync(db);

    // Real users get their Subject row created lazily on first authenticated
    // request — see Andy.Rbac.Api.Middleware.EnsureSubjectMiddleware. The
    // well-known dev test subject (test@andy.local) is upserted by
    // SeedFromManifestsAsync above when binding the manifest's testUserRole.
}

app.Run();

// Make Program accessible to test project
public partial class Program { }
