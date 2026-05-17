// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Api.Telemetry;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Telemetry;

/// <summary>
/// Verifies that OT4 (rivoli-ai/conductor#1262) telemetry wiring in
/// andy-rbac actually records spans / counters for every
/// permission-check evaluation. If the source name drifts away from
/// what Program.cs registers, every span and every counter silently
/// disappears — these tests fence that.
/// </summary>
public class RbacTelemetryTests
{
    [Fact]
    public void ActivitySource_StartsAnActivity_WhenListenerSubscribes()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RbacTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = captured.Add,
        };
        ActivitySource.AddActivityListener(listener);

        using (var activity = RbacTelemetry.ActivitySource.StartActivity("PermissionCheck"))
        {
            activity.Should().NotBeNull();
            activity!.SetTag("rbac.outcome", "denied");
        }

        captured.Should().ContainSingle();
        captured[0].OperationName.Should().Be("PermissionCheck");
        captured[0].GetTagItem("rbac.outcome").Should().Be("denied");
    }

    [Fact]
    public void ChecksTotalCounter_IsOnTheCanonicalMeter()
    {
        RbacTelemetry.ChecksTotal.Name.Should().Be("rbac.check.count");
        RbacTelemetry.ChecksTotal.Meter.Name.Should().Be(RbacTelemetry.MeterName);
    }

    [Fact]
    public async Task PermissionEvaluator_EmitsSpanAndCounterOnDeny()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RbacTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        // Build an in-memory RbacDbContext with no subjects so the evaluator
        // returns "Subject not found" — the simplest deny path.
        var options = new DbContextOptionsBuilder<RbacDbContext>()
            .UseInMemoryDatabase(databaseName: $"rbac-telemetry-{Guid.NewGuid()}")
            .Options;
        await using var db = new RbacDbContext(options);
        var repo = new PermissionRepository(db);
        var evaluator = new PermissionEvaluator(db, repo, NullLogger<PermissionEvaluator>.Instance);

        var result = await evaluator.CheckPermissionAsync(
            subjectExternalId: "missing-subject",
            permission: "andy.test.read");

        result.Allowed.Should().BeFalse();
        spans.Should().ContainSingle("PermissionCheck must always emit exactly one span");
        var span = spans[0];
        span.OperationName.Should().Be("PermissionCheck");
        // OT7 (rivoli-ai/conductor#1265). Dual-emit: the new
        // `andy.rbac.*` namespace and the legacy `rbac.*` names
        // both ship until Andy.Telemetry 0.3.0.
        span.GetTagItem("andy.rbac.permission").Should().Be("andy.test.read");
        span.GetTagItem("andy.rbac.outcome").Should().Be("denied");
        // Legacy names — deprecated but still asserted to catch a
        // premature removal during the 0.2.4 transition window.
        span.GetTagItem("rbac.permission").Should().Be("andy.test.read");
        span.GetTagItem("rbac.outcome").Should().Be("denied");
    }
}
