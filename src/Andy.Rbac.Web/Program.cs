using Andy.Rbac.Client;
using Andy.Rbac.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add RBAC client
builder.Services.AddRbacClient(builder.Configuration);

// Add RBAC API service for admin UI. Sourced from compose
// (Rbac__ApiBaseUrl). In compose this is the internal docker network URL
// `https://api:8443`; in dev it points at the API's local URL.
var apiBaseUrl = builder.Configuration["Rbac:ApiBaseUrl"]
    ?? throw new InvalidOperationException("Rbac:ApiBaseUrl is required (set via Rbac__ApiBaseUrl env var).");
builder.Services.AddHttpClient<RbacApiService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Allow self-signed certs in development
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

// Configure authentication with OpenID Connect. Sourced from .env / compose
// (ANDY_AUTH_AUTHORITY → AndyAuth__Authority).
var andyAuthAuthority = builder.Configuration["AndyAuth:Authority"]
    ?? throw new InvalidOperationException("AndyAuth:Authority is required (set via ANDY_AUTH_AUTHORITY env var).");
var andyAuthBrowserAuthority = andyAuthAuthority.Replace("host.docker.internal", "localhost");
var clientId = builder.Configuration["AndyAuth:ClientId"] ?? "andy-rbac-web";
var clientSecret = builder.Configuration["AndyAuth:ClientSecret"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = "AndyRbac.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
})
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.Authority = andyAuthAuthority;
    options.ClientId = clientId;
    // Public client — no secret, use PKCE
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.UsePkce = true;
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();


    if (builder.Environment.IsDevelopment())
    {
        options.BackchannelHttpHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
    }

    // Scopes
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.Scope.Add("offline_access");

    // Map claims
    options.ClaimActions.MapJsonKey("email", "email");
    options.ClaimActions.MapJsonKey("name", "name");
    options.ClaimActions.MapJsonKey("picture", "picture");

    // Token validation
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        NameClaimType = "name",
        RoleClaimType = "role",
        ValidIssuers = new[]
        {
            andyAuthAuthority, andyAuthAuthority.TrimEnd('/') + "/",
            andyAuthBrowserAuthority, andyAuthBrowserAuthority.TrimEnd('/') + "/"
        }.Distinct().ToArray()
    };

    // Handle events
    options.Events = new OpenIdConnectEvents
    {
        // Browser redirects must use localhost, not host.docker.internal
        OnRedirectToIdentityProvider = context =>
        {
            context.ProtocolMessage.IssuerAddress =
                context.ProtocolMessage.IssuerAddress.Replace("host.docker.internal", "localhost");
            return Task.CompletedTask;
        },
        OnRedirectToIdentityProviderForSignOut = context =>
        {
            context.ProtocolMessage.IssuerAddress =
                context.ProtocolMessage.IssuerAddress.Replace("host.docker.internal", "localhost");
            return Task.CompletedTask;
        },
        OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("OIDC");
            logger.LogError(context.Failure, "OIDC remote failure: {Error}", context.Failure?.Message);
            context.Response.Redirect("/?error=" + Uri.EscapeDataString(context.Failure?.Message ?? "unknown"));
            context.HandleResponse();
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Add HttpContextAccessor and authentication state provider for Blazor Server
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
    Microsoft.AspNetCore.Components.Server.ServerAuthenticationStateProvider>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Authentication endpoints
app.MapGet("authentication/login", async (HttpContext context, string? returnUrl) =>
{
    returnUrl ??= "/";
    await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = returnUrl
    });
}).AllowAnonymous();

app.MapPost("authentication/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = "/"
    });
}).AllowAnonymous();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

/// <summary>
/// Rewrites outgoing HTTP requests from one hostname to another,
/// allowing localhost:5001 URLs to be routed to host.docker.internal:5001 inside Docker.
/// </summary>
internal class HostRewriteHandler : DelegatingHandler
{
    private readonly string _from;
    private readonly string _to;

    public HostRewriteHandler(string fromHost, string toHost)
    {
        _from = fromHost;
        _to = toHost;
        InnerHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.Host == _from)
        {
            var builder = new UriBuilder(request.RequestUri) { Host = _to };
            request.RequestUri = builder.Uri;
        }
        return base.SendAsync(request, ct);
    }
}
