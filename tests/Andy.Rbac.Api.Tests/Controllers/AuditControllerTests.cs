using Andy.Rbac.Api.Controllers;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Andy.Rbac.Api.Tests.Controllers;

public sealed class AuditControllerTests
{
    [Fact]
    public async Task Get_WithSkip_ReturnsEventsBeyondFirstFiveHundred()
    {
        using var context = TestDbContextFactory.Create();
        var start = DateTimeOffset.UtcNow.AddMinutes(-600);
        context.AuditLogs.AddRange(Enumerable.Range(0, 550).Select(index => new RbacAuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = start.AddMinutes(index),
            EventType = AuditEventTypes.PermissionCheck,
            PermissionCode = $"app:document:read-{index}",
            Result = "allowed"
        }));
        await context.SaveChangesAsync();
        var controller = new AuditController(context);

        var action = await controller.Get(skip: 500, take: 50);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<AuditPage>().Subject;
        page.Total.Should().Be(550);
        page.Items.Should().HaveCount(50);
        page.Skip.Should().Be(500);
    }
}
