using System.Net.Http.Json;
using System.Text.Json;

namespace Andy.Rbac.Web.Services;

public class RbacApiService
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RbacApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // Applications
    public async Task<List<ApplicationDto>> GetApplicationsAsync()
    {
        var response = await _httpClient.GetAsync("api/applications");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ApplicationDto>>(JsonOptions) ?? new();
    }

    public async Task<ApplicationDto?> GetApplicationAsync(Guid id)
    {
        return await _httpClient.GetFromJsonAsync<ApplicationDto>($"api/applications/{id}", JsonOptions);
    }

    public async Task<ApplicationDto?> CreateApplicationAsync(CreateApplicationRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/applications", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApplicationDto>(JsonOptions);
    }

    public async Task DeleteApplicationAsync(Guid id)
    {
        var response = await _httpClient.DeleteAsync($"api/applications/{id}");
        response.EnsureSuccessStatusCode();
    }

    // Roles
    public async Task<List<RoleDto>> GetRolesAsync(string? applicationCode = null)
    {
        var url = "api/roles";
        if (!string.IsNullOrEmpty(applicationCode))
            url += $"?applicationCode={applicationCode}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<RoleDto>>(JsonOptions) ?? new();
    }

    public async Task<RoleDto?> CreateRoleAsync(CreateRoleRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/roles", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RoleDto>(JsonOptions);
    }

    public async Task DeleteRoleAsync(Guid id)
    {
        var response = await _httpClient.DeleteAsync($"api/roles/{id}");
        response.EnsureSuccessStatusCode();
    }

    // Subjects
    public async Task<PagedResult<SubjectDto>> GetSubjectsAsync(string? query = null, int skip = 0, int take = 50)
    {
        var url = $"api/subjects?skip={skip}&take={take}";
        if (!string.IsNullOrEmpty(query))
            url += $"&query={Uri.EscapeDataString(query)}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PagedResult<SubjectDto>>(JsonOptions) ?? new();
    }

    public async Task<SubjectDto?> CreateSubjectAsync(CreateSubjectRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/subjects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubjectDto>(JsonOptions);
    }

    public async Task DeleteSubjectAsync(Guid id)
    {
        var response = await _httpClient.PostAsync($"api/subjects/{id}/deactivate", null);
        response.EnsureSuccessStatusCode();
    }

    // Teams
    public async Task<List<TeamDto>> GetTeamsAsync(string? applicationCode = null)
    {
        var url = "api/teams";
        if (!string.IsNullOrEmpty(applicationCode))
            url += $"?applicationCode={applicationCode}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<TeamDto>>(JsonOptions) ?? new();
    }

    public async Task<TeamDto?> CreateTeamAsync(CreateTeamRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/teams", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TeamDto>(JsonOptions);
    }

    public async Task DeleteTeamAsync(Guid id)
    {
        var response = await _httpClient.DeleteAsync($"api/teams/{id}");
        response.EnsureSuccessStatusCode();
    }

    // Dashboard Stats
    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        var apps = await GetApplicationsAsync();
        var roles = await GetRolesAsync();
        var subjects = await GetSubjectsAsync(take: 1);
        var teams = await GetTeamsAsync();

        return new DashboardStats
        {
            Applications = apps.Count,
            Roles = roles.Count,
            Subjects = (int)subjects.Total,
            Teams = teams.Count
        };
    }
}

// DTOs
public record ApplicationDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int ResourceTypeCount,
    int RoleCount,
    DateTimeOffset CreatedAt
);

public record CreateApplicationRequest(string Code, string Name, string? Description);

public record RoleDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string? ApplicationCode,
    string? ParentRoleCode,
    bool IsSystem,
    int PermissionCount,
    int SubjectCount,
    DateTimeOffset CreatedAt
);

public record CreateRoleRequest(string Code, string Name, string? Description, string? ApplicationCode, string? ParentRoleCode);

public record SubjectDto(
    Guid Id,
    string ExternalId,
    string Provider,
    string? Type,
    string? Email,
    string? DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt
);

public record CreateSubjectRequest(string ExternalId, string Provider, string? Type, string? Email, string? DisplayName);

public record TeamDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string? ParentTeamCode,
    string? ApplicationCode,
    int MemberCount,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record CreateTeamRequest(string Code, string Name, string? Description, string? ParentTeamCode, string? ApplicationCode);

public record PagedResult<T>(List<T> Items, long Total, int Skip, int Take)
{
    public PagedResult() : this(new List<T>(), 0, 0, 0) { }
}

public record DashboardStats
{
    public int Applications { get; init; }
    public int Roles { get; init; }
    public int Subjects { get; init; }
    public int Teams { get; init; }
}
