using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

public sealed class ManagementAuthorizationTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ManagementAuthorizationTests(RbacWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task AuthenticatedNonAdministrator_CanReadButCannotMutate()
    {
        _client.DefaultRequestHeaders.Add("X-Test-Role", "none");

        (await _client.GetAsync("/api/applications")).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await _client.PostAsJsonAsync(
            "/api/applications", new { Code = "forbidden-app", Name = "Forbidden" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
