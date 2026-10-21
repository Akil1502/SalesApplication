using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesApp.Api.Json;

/// <summary>
/// Every timestamp is stored as UTC (the fields are named ...AtUtc), but the database hands them back without saying
/// so, and JSON then leaves off the "Z": a browser reads "2026-10-17T09:25:42" as its own local time and shows it
/// hours out (5½ in India). This writes them as UTC ("2026-10-17T09:25:42Z") and reads incoming ones the same way.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
    }
}
