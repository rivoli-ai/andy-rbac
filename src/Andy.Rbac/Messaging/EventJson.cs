// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andy.Rbac.Messaging;

// Canonical JSON options for every ADR 0001 event payload. Producers
// (GoalService, future task/run services) serialize outbox rows with
// these options; consumers (IncomingMessage.Deserialize) decode with
// the same. Centralizing them here is the difference between "works
// by coincidence" and "works by contract" — without it, a publisher
// using snake_case and a consumer using default camelCase silently
// deserialize to default-valued fields.
//
// Snake case is picked to match the convention already used by
// RbacDbContext's JSON-backed columns and the Andy ecosystem's wire
// format broadly. Strings for enums so consumers don't depend on
// ordinal stability when a new enum member lands.
public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
