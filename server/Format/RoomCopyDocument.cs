using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboRoomCopy.Format;

/// <summary>
/// A copied room, "room copy v1" (README): where it was copied from, its settings, its floor plan,
/// its furni with their places and stuff data, and its wired boxes with the furni they pick named
/// by <see cref="RoomCopyFloorItem.Ref"/>.
/// </summary>
public sealed record RoomCopyDocument
{
    public const int VERSION = 1;

    public int Version { get; init; }

    public RoomCopySource? Source { get; init; }

    public RoomCopyRoom? Room { get; init; }

    public RoomCopyModel? Model { get; init; }

    public List<RoomCopyFloorItem> FloorItems { get; init; } = [];

    public List<RoomCopyWallItem> WallItems { get; init; } = [];

    public List<RoomCopyWired> Wired { get; init; } = [];

    public static readonly JsonSerializerOptions JSON = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

public sealed record RoomCopySource
{
    public string? Hotel { get; init; }

    [JsonConverter(typeof(RefConverter))]
    public string? RoomId { get; init; }

    public string? Build { get; init; }

    public string? CapturedAt { get; init; }

    public string? Owner { get; init; }
}

public sealed record RoomCopyRoom
{
    public string? Name { get; init; }

    public string? Description { get; init; }

    public int? MaxUsers { get; init; }

    public int? DoorMode { get; init; }

    public int? TradeMode { get; init; }

    public bool? AllowPets { get; init; }

    public bool? AllowPetsEat { get; init; }

    public bool? HideOwner { get; init; }

    public bool? HideWalls { get; init; }

    public int? WallThickness { get; init; }

    public int? FloorThickness { get; init; }

    public int? WallHeight { get; init; }

    public RoomCopyPaint? Paint { get; init; }

    public RoomCopyChat? Chat { get; init; }

    public RoomCopyModeration? Moderation { get; init; }

    public List<string> Tags { get; init; } = [];
}

public sealed record RoomCopyPaint
{
    public string? Wall { get; init; }

    public string? Floor { get; init; }

    public string? Landscape { get; init; }
}

/// <summary>The room's chat settings as Habbo sends them (RoomChatSettings); this server keeps the flood setting.</summary>
public sealed record RoomCopyChat
{
    public int? Flood { get; init; }

    public int? Flow { get; init; }

    public int? BubbleWidth { get; init; }

    public int? ScrollSpeed { get; init; }

    public int? TalkHearingDistance { get; init; }
}

public sealed record RoomCopyModeration
{
    public int? Mute { get; init; }

    public int? Kick { get; init; }

    public int? Ban { get; init; }
}

public sealed record RoomCopyModel
{
    /// <summary>The floor plan's rows joined by <c>\r</c>, one character a tile (<c>x</c> closed, <c>0-9a-z</c> a height).</summary>
    public string? Heightmap { get; init; }

    public int DoorX { get; init; }

    public int DoorY { get; init; }

    public int DoorDir { get; init; }
}

public sealed record RoomCopyStuff
{
    /// <summary><c>legacy</c> (a string), <c>map</c> (string to string) or <c>number</c> (whole numbers).</summary>
    public string? Type { get; init; }

    public JsonElement Value { get; init; }
}

public sealed record RoomCopyFloorItem
{
    [JsonConverter(typeof(RefConverter))]
    public string? Ref { get; init; }

    public int Sprite { get; init; }

    public string? Classname { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public double Z { get; init; }

    public int Dir { get; init; }

    public RoomCopyStuff? Stuff { get; init; }
}

public sealed record RoomCopyWallItem
{
    [JsonConverter(typeof(RefConverter))]
    public string? Ref { get; init; }

    public int Sprite { get; init; }

    public string? Classname { get; init; }

    public string? WallPosition { get; init; }

    public RoomCopyStuff? Stuff { get; init; }
}

public sealed record RoomCopyWired
{
    [JsonConverter(typeof(RefConverter))]
    public string? Ref { get; init; }

    /// <summary><c>trigger</c>, <c>action</c>, <c>condition</c>, <c>selector</c>, <c>addon</c> or <c>variable</c>.</summary>
    public string? Kind { get; init; }

    public List<int> IntParams { get; init; } = [];

    public string? StringParam { get; init; }

    [JsonConverter(typeof(RefListConverter))]
    public List<string> StuffIds { get; init; } = [];

    [JsonConverter(typeof(RefListConverter))]
    public List<string> StuffIds2 { get; init; } = [];

    public List<int> FurniSourceTypes { get; init; } = [];

    public List<int> UserSourceTypes { get; init; } = [];

    [JsonConverter(typeof(RefListConverter))]
    public List<string> VariableIds { get; init; } = [];

    public int DelayInPulses { get; init; }

    public int QuantifierCode { get; init; }

    public bool IsFilter { get; init; }

    public bool IsInvert { get; init; }
}

/// <summary>A ref or an id written as a string or a number: read as its text.</summary>
internal sealed class RefConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        System.Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.ValueSpan),
            JsonTokenType.String => reader.GetString(),
            _ => throw new JsonException("A ref is a string or a number."),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

internal sealed class RefListConverter : JsonConverter<List<string>>
{
    private static readonly RefConverter ITEM = new();

    public override List<string> Read(
        ref Utf8JsonReader reader,
        System.Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("A list of refs is an array.");

        var refs = new List<string>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            if (ITEM.Read(ref reader, typeof(string), options) is { } value)
                refs.Add(value);

        return refs;
    }

    public override void Write(
        Utf8JsonWriter writer,
        List<string> value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartArray();

        foreach (var item in value)
            writer.WriteStringValue(item);

        writer.WriteEndArray();
    }
}
