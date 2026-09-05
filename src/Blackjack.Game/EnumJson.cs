using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blackjack.Game;

/// <summary>
/// Writes a list of enums as their names.
///
/// <see cref="JsonStringEnumConverter"/> only claims scalar enum types, so a
/// property holding a collection of them needs its own converter. Without this,
/// AvailableActions goes over the wire as [1, 0, 2] and the client has to know that
/// Stand is 1 -- an ordering it should never depend on, and one that changes
/// silently the moment somebody inserts a value into the middle of the enum.
///
/// Read side accepts either form, because a name is what we now emit but an older
/// client -- or a hand-written request -- may still send numbers.
/// </summary>
public sealed class StringEnumListConverter<T> : JsonConverter<IReadOnlyList<T>>
    where T : struct, Enum
{
    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected an array of {typeof(T).Name}.");
        }

        var values = new List<T>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    var name = reader.GetString();
                    if (!Enum.TryParse<T>(name, ignoreCase: true, out var parsed))
                    {
                        throw new JsonException($"'{name}' is not a {typeof(T).Name}.");
                    }

                    values.Add(parsed);
                    break;

                case JsonTokenType.Number:
                    values.Add((T)Enum.ToObject(typeof(T), reader.GetInt32()));
                    break;

                default:
                    throw new JsonException($"Expected a {typeof(T).Name} name or number.");
            }
        }

        return values;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in value)
        {
            writer.WriteStringValue(item.ToString());
        }

        writer.WriteEndArray();
    }
}
