using Andy.Rbac.Api.Data;
using Andy.Rbac.Api.Mcp;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Andy RBAC API", Version = "v1" });
});

// Add gRPC
builder.Services.AddGrpc();

// Add database
builder.Services.AddDbContext<RbacDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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

// Add authentication (integrate with andy-auth)
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
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

// Map MCP endpoint for AI assistants (Claude Desktop, ChatGPT, etc.)
app.MapMcp("/mcp");

// Health check
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

// Apply migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();

    if (app.Environment.IsDevelopment())
    {
        await db.Database.MigrateAsync();
    }

    // Seed initial data
    await DataSeeder.SeedAsync(db);

    // Seed application-specific data
    foreach (var appCode in new[] { "andy-auth", "andy-docs", "andy-cli", "andy-agentic-web" })
    {
        await DataSeeder.SeedApplicationDataAsync(db, appCode);
    }
}

app.Run();

// Make Program accessible to test project
public partial class Program { }
