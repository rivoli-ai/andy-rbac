using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andy.Rbac.Api.Tests.Integration;

/// <summary>
/// Mirror of the API's JSON options. Program.cs registers a
/// <see cref="JsonStringEnumConverter"/> on the controller pipeline; without
/// the same converter on the read side, <c>ReadFromJsonAsync&lt;T&gt;</c>
/// chokes on string-encoded enums (e.g. <c>"User"</c> →
/// <see cref="Andy.Rbac.Models.SubjectType"/>).
/// </summary>
public static class TestJsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
