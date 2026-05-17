// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Andy.Rbac.Api.Telemetry;

/// <summary>
/// Domain <see cref="ActivitySource"/> and <see cref="Meter"/> for andy-rbac.
///
/// Wired via Andy.Telemetry in <c>Program.cs</c> (see OT4 —
/// rivoli-ai/conductor#1262). Use <see cref="ActivitySource"/> to emit
/// spans around permission-check / role-resolution operations, and
/// <see cref="Meter"/> for domain counters / histograms.
/// </summary>
public static class RbacTelemetry
{
    /// <summary>Activity source name; matches the registration in <c>AddAndyTelemetry</c>.</summary>
    public const string ActivitySourceName = "Andy.Rbac";

    /// <summary>Meter name; matches the registration in <c>AddAndyTelemetry</c>.</summary>
    public const string MeterName = "Andy.Rbac";

    /// <summary>
    /// Activity source for spans emitted by andy-rbac (permission check,
    /// role inheritance, group resolution, ...).
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>
    /// Meter for andy-rbac domain metrics.
    /// </summary>
    public static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Count of permission checks, tagged with <c>rbac.outcome</c>
    /// (granted, denied) and <c>rbac.reason</c> when the check was a
    /// non-trivial denial (subject_inactive, subject_not_found,
    /// permission_denied).
    /// </summary>
    public static readonly Counter<long> ChecksTotal =
        Meter.CreateCounter<long>(
            name: "rbac.check.count",
            unit: "{check}",
            description: "Count of permission-check evaluations by andy-rbac.");
}
