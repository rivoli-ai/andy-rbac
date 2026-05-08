// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Rbac.Infrastructure.Messaging;

public sealed class NatsOptions
{
    public const string SectionName = "Messaging:Nats";

    public string Url { get; set; } = "nats://localhost:4222";

    // ADR-0001 §"Resolved decisions" #1 (AK5): retention is split by class.
    // Two streams are provisioned by NatsStreamProvisioner:
    //   ANDY_PROGRESS — short-lived run/container progress events (7 days).
    //   ANDY_DOMAIN   — long-lived goal/task/issue/agents events + DLQs (90 days).
    // Production wiring binds this from appsettings.json. Default left empty
    // because IConfiguration.Bind APPENDS to existing array properties — a
    // non-empty default would produce duplicates after bind. Tests / callers
    // that don't bind a config section can use DefaultStreams() explicitly.
    public NatsStreamSpec[] Streams { get; set; } = [];

    public string DlqPrefix { get; set; } = "andy.rbac.dlq";

    // andy-rbac is a publisher only (Epic AL — AL3 subject scheme + AL4
    // emission). One long-retention stream covers both event + DLQ
    // subjects scoped to the andy.rbac.* namespace.
    public static NatsStreamSpec[] DefaultStreams() =>
    [
        new NatsStreamSpec
        {
            Name = "ANDY_RBAC",
            Subjects =
            [
                "andy.rbac.events.>",
                "andy.rbac.dlq.>"
            ],
            MaxAge = TimeSpan.FromDays(90)
        }
    ];
}

public sealed class NatsStreamSpec
{
    public string Name { get; set; } = "";
    public string[] Subjects { get; set; } = [];
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(7);
}
