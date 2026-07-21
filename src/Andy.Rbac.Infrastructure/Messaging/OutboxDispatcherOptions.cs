// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Rbac.Infrastructure.Messaging;

// Knobs for the OutboxDispatcher background worker. Bound from the
// "Messaging:Outbox" configuration section by Program.cs. Integration
// tests override PollInterval down to ~50ms so the end-to-end loops
// finish in tens of milliseconds instead of seconds.
public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "Messaging:Outbox";

    // ADR-0001 operational invariant (AK2): PollInterval ≤ 2s across all
    // services on the bus. Exceeding this triggers a startup warning;
    // CI asserts appsettings.json conforms.
    public static readonly TimeSpan MaxRecommendedPollInterval = TimeSpan.FromSeconds(2);

    // Delay between drains when the outbox is empty. When a drain
    // finds rows, the worker loops immediately to keep up with a
    // burst; only the empty-poll path sleeps.
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    // Max rows per drain. Bounds the transaction size and the
    // failure blast-radius of a poison message.
    public int BatchSize { get; set; } = 100;

    public int MaxAttempts { get; set; } = 10;
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}
