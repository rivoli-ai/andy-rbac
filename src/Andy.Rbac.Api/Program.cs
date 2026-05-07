using Andy.Rbac.Api.Data;
using Andy.Rbac.Api.Mcp;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore.Authentication;
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
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
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

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });

    // Allow MCP clients (Claude Desktop, Cursor, etc.) to access /mcp endpoints
    options.AddPolicy("AllowMcpClients", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<RbacGrpcService>();

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

    // Seed dev users with super-admin role (development only).
    // Reads andy-auth's Postgres directly — sourced from .env / compose
    // (ANDY_AUTH_DB_CONNECTION → AndyAuth__DbConnectionString). When the
    // env var is missing or the DB unreachable, falls back to a single
    // hardcoded test subject. The direct-DB read is a known wart and
    // tracked as a follow-up to refactor onto the andy-auth API.
    if (app.Environment.IsDevelopment())
    {
        var authDbConn = app.Configuration["AndyAuth:DbConnectionString"];
        if (string.IsNullOrWhiteSpace(authDbConn))
        {
            app.Logger.LogWarning("AndyAuth:DbConnectionString not set; skipping user seeding from Andy.Auth DB.");
            await DataSeeder.SeedTestSubjectAsync(db, "45abdfa0-da00-4bff-9226-9c91fcda15b1", "test@andy.local");
        }
        else
        {
            try
            {
                using var authConn = new Npgsql.NpgsqlConnection(authDbConn);
                await authConn.OpenAsync();
                using var cmd = new Npgsql.NpgsqlCommand(
                    "SELECT \"Id\", \"Email\" FROM \"AspNetUsers\" WHERE \"IsActive\" = true", authConn);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    await DataSeeder.SeedTestSubjectAsync(db, reader.GetString(0), reader.GetString(1));
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Could not seed from Andy.Auth DB, using fallback");
                await DataSeeder.SeedTestSubjectAsync(db, "45abdfa0-da00-4bff-9226-9c91fcda15b1", "test@andy.local");
            }
        }
    }
}

app.Run();

// Make Program accessible to test project
public partial class Program { }
