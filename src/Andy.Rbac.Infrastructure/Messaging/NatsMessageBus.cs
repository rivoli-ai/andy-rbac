// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Andy.Rbac.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Andy.Rbac.Infrastructure.Messaging;

// IMessageBus backed by NATS JetStream per ADR 0001. The connection is
// created once at construction and disposed when the DI container shuts
// down. Stream provisioning (CreateOrUpdateStream) is handled by the
// separate NatsStreamProvisioner hosted service which runs before any
// BackgroundService, guaranteeing the stream exists before the
// OutboxDispatcher or GoalCreatedHandler start publishing/subscribing.
public sealed class NatsMessageBus : IMessageBus, IAsyncDisposable
{
    // AK6: meter name registered in Program.cs OTel pipeline so the
    // generation-breach counter is exported via OTLP.
    public const string MeterName = "Andy.Rbac.Messaging";
    private const string ServiceTag = "andy-tasks";

    private static readonly Meter _meter = new(MeterName);
    private static readonly Counter<long> _generationBreachCounter =
        _meter.CreateCounter<long>(
            name: "rivoli_nats_generation_limit_breach_total",
            unit: "{breach}",
            description: "Cumulative count of messages whose generation exceeded the ADR-0001 limit (drop on publish, DLQ on consume).");

    private readonly NatsOptions _options;
    private readonly ILogger<NatsMessageBus> _logger;
    private readonly NatsConnection _connection;
    private readonly INatsJSContext _jsContext;

    public NatsMessageBus(IOptions<NatsOptions> options, ILogger<NatsMessageBus> logger)
    {
        _options = options.Value;
        _logger = logger;
        _connection = new NatsConnection(new NatsOpts { Url = _options.Url });
        _jsContext = new NatsJSContext(_connection);
    }

    internal INatsJSContext JetStream => _jsContext;
    internal NatsConnection Connection => _connection;

    // Eagerly connect the underlying TCP socket. Called by
    // NatsStreamProvisioner.StartAsync before any publish/subscribe
    // so we don't pay the lazy-connect cost on the first hot-path
    // operation.
    internal async Task ConnectAsync(CancellationToken ct = default)
    {
        await _connection.ConnectAsync();
    }

    public async Task PublishAsync(
        string subject,
        object payload,
        MessageHeaders headers,
        CancellationToken ct = default)
    {
        if (headers.ExceedsGenerationLimit)
        {
            _logger.LogError(
                "Dropping message {MsgId} on {Subject} — generation {Gen} exceeds limit {Max}. " +
                "Correlation: {CorrId} Causation: {CausedBy}",
                headers.MsgId, subject, headers.Generation, MessageHeaders.MaxGeneration,
                headers.CorrelationId, headers.CausationId);
            RecordGenerationBreach(direction: "publish", subject);
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, EventJson.Options);
        var natsHeaders = ToNatsHeaders(headers);

        var ack = await _jsContext.PublishAsync(subject, bytes, headers: natsHeaders, cancellationToken: ct);

        if (ack.Error is not null)
        {
            throw new InvalidOperationException(
                $"NATS JetStream publish rejected on {subject}: {ack.Error.Code} {ack.Error.Description}");
        }
    }

    public async IAsyncEnumerable<IncomingMessage> SubscribeAsync(
        string subjectFilter,
        SubscriptionOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // NATS 2.10+ embeds the consumer name in the subject for the
        // CREATE API ($JS.API.CONSUMER.CREATE.<stream>.<name>). Dots in
        // the name break subject parsing. Sanitize to dashes.
        var safeDurableName = options.DurableName.Replace('.', '-');

        var consumerConfig = new ConsumerConfig(safeDurableName)
        {
            FilterSubject = options.SubjectFilter ?? subjectFilter,
            AckPolicy = options.ManualAck
                ? ConsumerConfigAckPolicy.Explicit
                : ConsumerConfigAckPolicy.None,
            MaxDeliver = options.MaxDeliver
        };

        // AK5: with two streams (ANDY_PROGRESS / ANDY_DOMAIN) we resolve
        // the owning stream by client-side subject-pattern matching. NATS
        // routes publishes by subject, but the consumer-create API needs
        // the explicit stream name.
        var streamName = ResolveStreamName(consumerConfig.FilterSubject ?? subjectFilter);

        var consumer = await _jsContext.CreateOrUpdateConsumerAsync(
            streamName, consumerConfig, ct);

        _logger.LogDebug(
            "Subscription opened on {Filter} durable {Durable}",
            subjectFilter, options.DurableName);

        await foreach (var jsMsg in consumer.ConsumeAsync<byte[]>(cancellationToken: ct))
        {
            var parsed = TryParseHeaders(jsMsg);
            if (parsed is null)
            {
                _logger.LogWarning(
                    "Dropping message on {Subject} — missing or malformed required headers. " +
                    "Acking to prevent redelivery loop",
                    jsMsg.Subject);
                await PublishToDlqAsync(jsMsg.Subject, jsMsg.Data, jsMsg.Headers, ct);
                await jsMsg.AckAsync(cancellationToken: ct);
                continue;
            }

            if (parsed.ExceedsGenerationLimit)
            {
                _logger.LogError(
                    "Dropping message {MsgId} on {Subject} — generation {Gen} exceeds limit {Max}. " +
                    "Correlation: {CorrId} Causation: {CausedBy}",
                    parsed.MsgId, jsMsg.Subject, parsed.Generation, MessageHeaders.MaxGeneration,
                    parsed.CorrelationId, parsed.CausationId);
                RecordGenerationBreach(direction: "consume", jsMsg.Subject);
                await PublishToDlqAsync(jsMsg.Subject, jsMsg.Data, jsMsg.Headers, ct);
                await jsMsg.AckAsync(cancellationToken: ct);
                continue;
            }

            yield return new NatsIncomingMessage(jsMsg)
            {
                Headers = parsed,
                Subject = jsMsg.Subject,
                Payload = jsMsg.Data ?? ReadOnlyMemory<byte>.Empty,
                ReceivedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private static NatsHeaders ToNatsHeaders(MessageHeaders headers)
    {
        return new NatsHeaders
        {
            { "Nats-Msg-Id", headers.MsgId.ToString() },
            { "Andy-Correlation-Id", headers.CorrelationId.ToString() },
            { "Andy-Causation-Id", headers.CausationId?.ToString() ?? "" },
            { "Andy-Generation", headers.Generation.ToString() }
        };
    }

    private static MessageHeaders? TryParseHeaders(INatsJSMsg<byte[]> jsMsg)
    {
        if (jsMsg.Headers is null)
            return null;

        var h = jsMsg.Headers;

        if (!h.TryGetValue("Nats-Msg-Id", out var msgIdValues)
            || !Guid.TryParse(msgIdValues.ToString(), out var msgId))
            return null;

        if (!h.TryGetValue("Andy-Correlation-Id", out var corrValues)
            || !Guid.TryParse(corrValues.ToString(), out var correlationId))
            return null;

        if (!h.TryGetValue("Andy-Generation", out var genValues)
            || !int.TryParse(genValues.ToString(), out var generation))
            return null;

        Guid? causationId = null;
        if (h.TryGetValue("Andy-Causation-Id", out var causValues))
        {
            var raw = causValues.ToString();
            if (!string.IsNullOrEmpty(raw) && Guid.TryParse(raw, out var parsed))
                causationId = parsed;
        }

        return new MessageHeaders(msgId, correlationId, causationId, generation);
    }

    private static void RecordGenerationBreach(string direction, string subject)
    {
        _generationBreachCounter.Add(1,
            new KeyValuePair<string, object?>("service", ServiceTag),
            new KeyValuePair<string, object?>("direction", direction),
            new KeyValuePair<string, object?>("subject_root", TruncateSubjectRoot(subject)));
    }

    // Keep at most the first three dot-segments so a runaway breach storm
    // can't explode metric cardinality through the {id} segment.
    internal static string TruncateSubjectRoot(string subject)
    {
        if (string.IsNullOrEmpty(subject))
            return string.Empty;

        var firstDot = subject.IndexOf('.');
        if (firstDot < 0) return subject;
        var secondDot = subject.IndexOf('.', firstDot + 1);
        if (secondDot < 0) return subject;
        var thirdDot = subject.IndexOf('.', secondDot + 1);
        return thirdDot < 0 ? subject : subject[..thirdDot];
    }

    private string ResolveStreamName(string subscribeFilter)
    {
        foreach (var stream in _options.Streams)
        {
            foreach (var streamSubject in stream.Subjects)
            {
                if (StreamSubjectCovers(streamSubject, subscribeFilter))
                    return stream.Name;
            }
        }
        throw new InvalidOperationException(
            $"No configured Messaging:Nats:Streams entry covers subscribe filter '{subscribeFilter}'.");
    }

    // True iff every concrete subject the subscribe filter could match
    // is also matched by streamSubject (i.e. streamSubject is at least
    // as permissive as subscribeFilter, token-by-token, with NATS '*'
    // and '>' semantics).
    internal static bool StreamSubjectCovers(string streamSubject, string subscribeFilter)
    {
        var streamTokens = streamSubject.Split('.');
        var filterTokens = subscribeFilter.Split('.');

        for (var i = 0; i < streamTokens.Length; i++)
        {
            var s = streamTokens[i];
            if (s == ">")
                return i <= filterTokens.Length;
            if (i >= filterTokens.Length)
                return false;
            if (s == "*")
                continue;
            if (s != filterTokens[i])
                return false;
        }
        return streamTokens.Length == filterTokens.Length;
    }

    private async Task PublishToDlqAsync(
        string originalSubject,
        byte[]? payload,
        NatsHeaders? originalHeaders,
        CancellationToken ct)
    {
        try
        {
            var dlqSubject = $"{_options.DlqPrefix}.{originalSubject}";
            await _jsContext.PublishAsync(
                dlqSubject,
                payload ?? [],
                headers: originalHeaders,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish to DLQ for {OriginalSubject} — message is lost",
                originalSubject);
        }
    }
}

// Wraps an INatsJSMsg<byte[]> so consumers call Ack/Nack through the
// IncomingMessage abstraction without knowing about the NATS client.
internal sealed class NatsIncomingMessage : IncomingMessage
{
    private readonly INatsJSMsg<byte[]> _jsMsg;

    internal NatsIncomingMessage(INatsJSMsg<byte[]> jsMsg)
    {
        _jsMsg = jsMsg;
    }

    public override async Task AckAsync(CancellationToken ct = default)
    {
        await _jsMsg.AckAsync(cancellationToken: ct);
    }

    public override async Task NackAsync(CancellationToken ct = default)
    {
        await _jsMsg.NakAsync(cancellationToken: ct);
    }
}
