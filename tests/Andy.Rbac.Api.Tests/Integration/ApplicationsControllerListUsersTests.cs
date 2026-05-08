using System.Net;
using System.Net.Http.Json;
using Andy.Rbac.Api.Controllers;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

public class ApplicationsControllerListUsersTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApplicationsControllerListUsersTests(RbacWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListUsers_ValidApp_ReturnsAssignedSubjectsWithScopedRoles()
    {
        var resp = await _client.GetAsync("/api/applications/by-code/test-app/users");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedResult<ApplicationUserDto>>(TestJsonOptions.Default);

        page.Should().NotBeNull();
        page!.Total.Should().Be(3); // admin + editor + viewer (no-role-user excluded)
        page.Items.Should().HaveCount(3);
        page.Items.Should().Contain(u => u.Email == "admin@test.com" && u.Roles.Contains("admin"));
        page.Items.Should().Contain(u => u.Email == "editor@test.com" && u.Roles.Contains("editor"));
        page.Items.Should().Contain(u => u.Email == "viewer@test.com" && u.Roles.Contains("viewer"));
        page.Items.Should().NotContain(u => u.Email == "norole@test.com");
    }

    [Fact]
    public async Task ListUsers_FilterByRole_NarrowsResultSet()
    {
        var resp = await _client.GetAsync("/api/applications/by-code/test-app/users?role=admin");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedResult<ApplicationUserDto>>(TestJsonOptions.Default);

        page.Should().NotBeNull();
        page!.Total.Should().Be(1);
        page.Items.Single().Email.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task ListUsers_QuerySearch_MatchesEmailAndDisplayName_CaseInsensitive()
    {
        // Search by email substring (uppercase) — should match editor@test.com.
        var byEmail = await _client.GetAsync("/api/applications/by-code/test-app/users?query=EDITOR");
        byEmail.StatusCode.Should().Be(HttpStatusCode.OK);
        var emailPage = await byEmail.Content.ReadFromJsonAsync<PagedResult<ApplicationUserDto>>(TestJsonOptions.Default);
        emailPage!.Items.Should().ContainSingle().Which.Email.Should().Be("editor@test.com");

        // Search by display-name substring matches "Admin User".
        var byName = await _client.GetAsync("/api/applications/by-code/test-app/users?query=admin%20user");
        byName.StatusCode.Should().Be(HttpStatusCode.OK);
        var namePage = await byName.Content.ReadFromJsonAsync<PagedResult<ApplicationUserDto>>(TestJsonOptions.Default);
        namePage!.Items.Should().ContainSingle().Which.Email.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task ListUsers_Pagination_RespectsSkipAndTake()
    {
        var resp = await _client.GetAsync("/api/applications/by-code/test-app/users?skip=1&take=1");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedResult<ApplicationUserDto>>(TestJsonOptions.Default);

        page!.Total.Should().Be(3);
        page.Skip.Should().Be(1);
        page.Take.Should().Be(1);
        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListUsers_TakeClampedToMax200()
    {
        var resp = await _client.GetAsync("/api/applications/by-code/test-app/users?take=10000");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedResult<ApplicationUserDto>>(TestJsonOptions.Default);

        page!.Take.Should().Be(200);
    }

    [Fact]
    public async Task ListUsers_UnknownApplication_Returns404()
    {
        var resp = await _client.GetAsync("/api/applications/by-code/does-not-exist/users");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListUsers_FilterByUnknownRole_ReturnsEmptyPage()
    {
        var resp = await _client.GetAsync("/api/applications/by-code/test-app/users?role=ghost-role");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedResult<ApplicationUserDto>>(TestJsonOptions.Default);

        page!.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }
}
