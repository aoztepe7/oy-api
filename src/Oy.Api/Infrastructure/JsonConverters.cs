using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oy.Api.Infrastructure;

/// <summary>Serializes <see cref="TimeOnly"/> as "HH:mm" (accepts "HH:mm" or "HH:mm:ss").</summary>
public sealed class TimeOnlyHhMmConverter : JsonConverter<TimeOnly>
{
    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TimeOnly.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
}
