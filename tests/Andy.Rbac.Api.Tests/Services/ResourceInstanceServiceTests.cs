using Andy.Rbac.Api.Services;
using Andy.Rbac.Models;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

public sealed class ResourceInstanceServiceTests
{
    [Fact]
    public async Task RegisterAsync_ExistingInstanceWithoutOwner_PreservesOwner()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var resourceTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var ownerId = Guid.Parse("66666666-6666-6666-6666-666666666668");
        context.ResourceInstances.Add(new ResourceInstance
        {
            Id = Guid.NewGuid(), ResourceTypeId = resourceTypeId,
            ExternalId = "doc-owned", OwnerSubjectId = ownerId,
            DisplayName = "Original"
        });
        await context.SaveChangesAsync();
        var service = new ResourceInstanceService(context, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        var result = await service.RegisterAsync(
            "test-app", "document", "doc-owned",
            ownerExternalId: null, ownerProvider: null,
            displayName: "Updated", metadata: null);

        result.Success.Should().BeTrue();
        var instance = context.ResourceInstances.Single(value => value.ExternalId == "doc-owned");
        instance.OwnerSubjectId.Should().Be(ownerId);
        instance.DisplayName.Should().Be("Updated");
    }
}
