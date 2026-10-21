using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Turbo.Primitives.Furniture.Enums;
using Turbo.Primitives.Furniture.Providers;
using Turbo.Primitives.Furniture.Snapshots;
using Turbo.Primitives.Movements;
using Turbo.Primitives.Navigator.Enums;
using Turbo.Primitives.Rooms;
using Turbo.Primitives.Rooms.Enums;
using TurboRoomCopy.Format;

namespace TurboRoomCopy;

/// <summary>
/// Turns a room copy into a <see cref="RoomCopyPlan"/> against this hotel's furni: nothing is
/// written here, so the dry run and the import read the same plan.
///
/// A furni is matched by its sprite and type first, as the client knows it, then by its class
/// name; when the sprite is a different furni here and the class name is one, the class name
/// wins (the hotel's sprite ids drifted). The floor plan keeps to the floor plan editor's rules
/// (<c>RoomMapModule.FloorPlan</c>: <c>x</c> or a height <c>0-9a-z</c> a tile, no side past the
/// limit, an open tile under the door), but not to its area limit: a hotel's public rooms are
/// larger than a player may draw.
/// </summary>
public sealed class RoomCopyPlanner(IFurnitureDefinitionProvider definitions, RoomCopyConfig config)
{
    public const string KIND_TRIGGER = "trigger";
    public const string KIND_ACTION = "action";
    public const string KIND_CONDITION = "condition";
    public const string KIND_SELECTOR = "selector";
    public const string KIND_ADDON = "addon";
    public const string KIND_VARIABLE = "variable";

    /// <summary>
    /// The order boxes are saved in: the variables first, so the boxes that name them find them,
    /// then what the others lean on.
    /// </summary>
    public static readonly string[] KINDS =
    [
        KIND_VARIABLE,
        KIND_ADDON,
        KIND_SELECTOR,
        KIND_TRIGGER,
        KIND_CONDITION,
        KIND_ACTION,
    ];

    private const string HEIGHT_CHARACTERS = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const char CLOSED_TILE = 'x';

    public RoomCopyPlan Plan(RoomCopyDocument copy, string? nameOverride)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (copy.Version != RoomCopyDocument.VERSION)
            errors.Add(
                $"This is room copy version {copy.Version}; this plugin reads version {RoomCopyDocument.VERSION}."
            );

        var model = PlanModel(copy.Model, errors);
        var room = PlanRoom(copy.Room ?? new RoomCopyRoom(), nameOverride, errors, warnings);
        var unmapped = new Dictionary<(string, int, bool), int>();
        var furni = new List<PlannedFurni>();
        var refs = new HashSet<string>(StringComparer.Ordinal);

        if (copy.FloorItems.Count + copy.WallItems.Count > config.MaxItems)
            errors.Add(
                $"The copy has {copy.FloorItems.Count + copy.WallItems.Count} furni; one import makes at most {config.MaxItems}."
            );

        foreach (var item in copy.FloorItems)
        {
            if (!TakeRef(item.Ref, refs, warnings))
                continue;

            var definition = Resolve(ProductType.Floor, item.Sprite, item.Classname, warnings);

            if (definition is null)
            {
                Count(unmapped, item.Classname, item.Sprite, false);

                continue;
            }

            if (model is not null && !IsOnPlan(model, item.X, item.Y))
                warnings.Add(
                    $"{definition.Name} ({item.Ref}) stands at {item.X},{item.Y}, off the floor plan: kept there."
                );

            furni.Add(
                new PlannedFurni(
                    item.Ref!,
                    definition.Id,
                    definition.Name,
                    false,
                    new RoomPosition(item.X, item.Y, item.Z, ReadRotation(item.Dir), null),
                    ExtraData(item.Stuff, item.Ref!, definition, warnings)
                )
            );
        }

        foreach (var item in copy.WallItems)
        {
            if (!TakeRef(item.Ref, refs, warnings))
                continue;

            if (!WallPosition.TryParse(item.WallPosition, out var position))
            {
                warnings.Add(
                    $"{item.Classname} ({item.Ref}): the wall position '{item.WallPosition}' does not read; left out."
                );

                continue;
            }

            var definition = Resolve(ProductType.Wall, item.Sprite, item.Classname, warnings);

            if (definition is null)
            {
                Count(unmapped, item.Classname, item.Sprite, true);

                continue;
            }

            furni.Add(
                new PlannedFurni(
                    item.Ref!,
                    definition.Id,
                    definition.Name,
                    true,
                    new RoomPosition(
                        position.X,
                        position.Y,
                        position.Z,
                        position.Rotation,
                        position.WallOffset
                    ),
                    ExtraData(item.Stuff, item.Ref!, definition, warnings)
                )
            );
        }

        var wired = PlanWired(copy.Wired, furni, warnings);

        if (furni.Count == 0 && copy.FloorItems.Count + copy.WallItems.Count > 0)
            warnings.Add("None of the copy's furni is known to this hotel.");

        return new RoomCopyPlan
        {
            Model = model ?? new PlannedModel(string.Empty, 0, 0, 0, 0, Rotation.North),
            Room = room,
            Furni = furni,
            Wired = wired,
            Unmapped =
            [
                .. unmapped
                    .Select(x => new UnmappedFurni(x.Key.Item1, x.Key.Item2, x.Key.Item3, x.Value))
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Classname, StringComparer.Ordinal),
            ],
            Warnings = warnings,
            Errors = errors,
        };
    }

    private PlannedModel? PlanModel(RoomCopyModel? source, List<string> errors)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Heightmap))
        {
            errors.Add("The copy has no floor plan (model.heightmap).");

            return null;
        }

        List<string> rows =
        [
            .. source
                .Heightmap.Replace("\r\n", "\r")
                .Replace("\n", "\r")
                .ToLowerInvariant()
                .Split('\r')
                .Select(x => x.TrimEnd()),
        ];

        while (rows.Count > 0 && rows[^1].Length == 0)
            rows.RemoveAt(rows.Count - 1);

        if (rows.Count == 0)
        {
            errors.Add("The floor plan has no rows.");

            return null;
        }

        var width = rows.Max(x => x.Length);
        var height = rows.Count;

        if (width > config.MaxAxisLength || height > config.MaxAxisLength)
            errors.Add(
                $"The floor plan is {width}x{height}; no side may pass {config.MaxAxisLength}."
            );

        foreach (var row in rows)
        foreach (var tile in row)
            if (tile != CLOSED_TILE && !HEIGHT_CHARACTERS.Contains(tile))
            {
                errors.Add($"The floor plan has '{tile}', which is not a tile height.");

                return null;
            }

        if (!rows.Any(row => row.Any(tile => tile != CLOSED_TILE)))
            errors.Add("Every tile of the floor plan is closed.");

        var doorRow = source.DoorY >= 0 && source.DoorY < rows.Count ? rows[source.DoorY] : "";

        if (
            source.DoorX < 0
            || source.DoorX >= doorRow.Length
            || doorRow[source.DoorX] == CLOSED_TILE
        )
            errors.Add($"The door ({source.DoorX},{source.DoorY}) is not on an open tile.");

        return new PlannedModel(
            string.Join('\r', rows),
            width,
            height,
            source.DoorX,
            source.DoorY,
            ReadRotation(source.DoorDir)
        );
    }

    private PlannedRoom PlanRoom(
        RoomCopyRoom source,
        string? nameOverride,
        List<string> errors,
        List<string> warnings
    )
    {
        var name = (
            string.IsNullOrWhiteSpace(nameOverride) ? source.Name ?? string.Empty : nameOverride
        ).Trim();

        if (name.Length == 0)
            errors.Add("The room needs a name: the copy has none and none was given.");

        if (name.Length > config.NameMaxLength)
        {
            warnings.Add($"The name is cut to {config.NameMaxLength} characters.");
            name = name[..config.NameMaxLength];
        }

        var description = (source.Description ?? string.Empty).Trim();

        if (description.Length > config.DescriptionMaxLength)
        {
            warnings.Add($"The description is cut to {config.DescriptionMaxLength} characters.");
            description = description[..config.DescriptionMaxLength];
        }

        var maxUsers = source.MaxUsers ?? 25;

        if (maxUsers < 1 || maxUsers > config.MaxUsersLimit)
        {
            var kept = Math.Clamp(maxUsers, 1, config.MaxUsersLimit);

            warnings.Add($"Max users {maxUsers} is made {kept}.");
            maxUsers = kept;
        }

        var doorMode = Read(source.DoorMode, RoomDoorModeType.Open, "door mode", warnings);

        // A copy carries no password: a password room is made locked (doorbell) instead.
        if (doorMode == RoomDoorModeType.Password)
        {
            warnings.Add(
                "The room is password protected; the copy has no password, so it is made locked (doorbell)."
            );
            doorMode = RoomDoorModeType.Locked;
        }

        var chat = source.Chat;

        if (
            chat is not null
            && (
                (chat.Flow is { } flow && flow != 0)
                || (chat.BubbleWidth is { } width && width != 1)
                || (chat.ScrollSpeed is { } speed && speed != 1)
                || (chat.TalkHearingDistance is { } distance && distance != 14)
            )
        )
            warnings.Add(
                "The room's chat mode, bubble width, scroll speed and hearing distance are not kept: this server keeps the flood setting only."
            );

        var tags = source
            .Tags.Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var joined = string.Join(',', tags);

        if (joined.Length > 128)
        {
            warnings.Add("The tags are cut to 128 characters.");
            joined = joined[..128].TrimEnd(',');
        }

        return new PlannedRoom
        {
            Name = name,
            Description = description,
            PlayersMax = maxUsers,
            DoorMode = doorMode,
            TradeType = Read(source.TradeMode, RoomTradeModeType.Disabled, "trade mode", warnings),
            AllowPets = source.AllowPets ?? false,
            AllowPetsEat = source.AllowPetsEat ?? false,
            HideOwner = source.HideOwner ?? false,
            HideWalls = source.HideWalls ?? false,
            ThicknessWall = Read(
                source.WallThickness,
                RoomThicknessType.Normal,
                "wall thickness",
                warnings
            ),
            ThicknessFloor = Read(
                source.FloorThickness,
                RoomThicknessType.Normal,
                "floor thickness",
                warnings
            ),
            WallHeight = source.WallHeight ?? -1,
            PaintWall = Paint(source.Paint?.Wall),
            PaintFloor = Paint(source.Paint?.Floor),
            PaintLandscape = Paint(source.Paint?.Landscape),
            ChatFlood = Read(
                chat?.Flood,
                ChatFloodSensitivityType.Normal,
                "chat flood setting",
                warnings
            ),
            MuteType = Read(
                source.Moderation?.Mute,
                ModSettingType.Owner,
                "who may mute",
                warnings
            ),
            KickType = Read(
                source.Moderation?.Kick,
                ModSettingType.Owner,
                "who may kick",
                warnings
            ),
            BanType = Read(source.Moderation?.Ban, ModSettingType.Owner, "who may ban", warnings),
            Tags = joined.Length == 0 ? null : joined,
        };
    }

    private static string? Paint(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static T Read<T>(int? value, T fallback, string what, List<string> warnings)
        where T : struct, Enum
    {
        if (value is null)
            return fallback;

        var read = (T)Enum.ToObject(typeof(T), value.Value);

        if (Enum.IsDefined(read))
            return read;

        warnings.Add($"The {what} {value} is not one this server knows: {fallback} is used.");

        return fallback;
    }

    private static Rotation ReadRotation(int value) =>
        value is >= 0 and <= 7 ? (Rotation)value : Rotation.North;

    private static bool IsOnPlan(PlannedModel model, int x, int y)
    {
        var rows = model.Heightmap.Split('\r');

        return y >= 0
            && y < rows.Length
            && x >= 0
            && x < rows[y].Length
            && rows[y][x] != CLOSED_TILE;
    }

    private static bool TakeRef(string? itemRef, HashSet<string> refs, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(itemRef))
        {
            warnings.Add("A furni without a ref is left out.");

            return false;
        }

        if (!refs.Add(itemRef))
        {
            warnings.Add($"The ref {itemRef} is used twice: the second is left out.");

            return false;
        }

        return true;
    }

    private static void Count(
        Dictionary<(string, int, bool), int> unmapped,
        string? classname,
        int sprite,
        bool onWall
    )
    {
        var key = (classname ?? "?", sprite, onWall);

        unmapped[key] = unmapped.GetValueOrDefault(key) + 1;
    }

    /// <summary>By sprite and type first, then by class name; the class name wins when the sprite is another furni here.</summary>
    private FurnitureDefinitionSnapshot? Resolve(
        ProductType type,
        int sprite,
        string? classname,
        List<string> warnings
    )
    {
        var bySprite = sprite > 0 ? definitions.TryGetDefinitionBySprite(type, sprite) : null;

        if (string.IsNullOrWhiteSpace(classname))
            return bySprite;

        if (
            bySprite is not null
            && string.Equals(bySprite.Name, classname, StringComparison.OrdinalIgnoreCase)
        )
            return bySprite;

        var byName = definitions.TryGetDefinitionByName(classname);

        if (byName is not null && byName.ProductType != type)
            byName = null;

        if (byName is not null)
        {
            if (bySprite is not null)
                warnings.Add(
                    $"{classname}: sprite {sprite} is {bySprite.Name} here; {byName.Name} is used, by its name."
                );

            return byName;
        }

        if (bySprite is not null)
            warnings.Add(
                $"{classname}: no furni of that name here; sprite {sprite} is {bySprite.Name}, which is used."
            );

        return bySprite;
    }

    /// <summary>
    /// The server's extra data for the item (<c>{"stuff":{"Data":...,"UniqueNumber":0,"UniqueSeries":0}}</c>,
    /// read by <c>StuffDataFactory</c>): a legacy string, a string map or a number list. Map data
    /// is read only by a definition whose logic keeps map data; it is stored either way.
    /// </summary>
    internal static string? ExtraData(
        RoomCopyStuff? stuff,
        string itemRef,
        FurnitureDefinitionSnapshot definition,
        List<string> warnings
    )
    {
        if (stuff is null || stuff.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;

        object? data;

        switch (stuff.Type?.ToLowerInvariant())
        {
            case "legacy":
            case null:
                var text = stuff.Value.ValueKind switch
                {
                    JsonValueKind.String => stuff.Value.GetString(),
                    JsonValueKind.Number => stuff.Value.GetRawText(),
                    _ => null,
                };

                // Kept as Habbo sends it, an empty state too: with no stuff data at all the server
                // would show "0" where Habbo shows "" (48 of 246 furni of the InfoBus park).
                if (text is null)
                    return null;

                data = text;
                break;
            case "map":
                if (stuff.Value.ValueKind != JsonValueKind.Object)
                {
                    warnings.Add(
                        $"{definition.Name} ({itemRef}): its map data is not an object; left out."
                    );

                    return null;
                }

                data = stuff
                    .Value.EnumerateObject()
                    .ToDictionary(
                        x => x.Name,
                        x =>
                            x.Value.ValueKind == JsonValueKind.String
                                ? x.Value.GetString() ?? string.Empty
                                : x.Value.GetRawText(),
                        StringComparer.Ordinal
                    );
                break;
            case "number":
                if (
                    stuff.Value.ValueKind != JsonValueKind.Array
                    || stuff.Value.EnumerateArray().Any(x => !x.TryGetInt32(out _))
                )
                {
                    warnings.Add(
                        $"{definition.Name} ({itemRef}): its number data is not whole numbers; left out."
                    );

                    return null;
                }

                data = stuff.Value.EnumerateArray().Select(x => x.GetInt32()).ToList();
                break;
            default:
                warnings.Add(
                    $"{definition.Name} ({itemRef}): stuff data of type '{stuff.Type}' is not one room copy v1 has; left out."
                );

                return null;
        }

        return JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                [ExtraDataSectionType.STUFF] = new Dictionary<string, object?>
                {
                    ["Data"] = data,
                    ["UniqueNumber"] = 0,
                    ["UniqueSeries"] = 0,
                },
            }
        );
    }

    private static List<PlannedWired> PlanWired(
        List<RoomCopyWired> boxes,
        List<PlannedFurni> furni,
        List<string> warnings
    )
    {
        var placed = furni
            .Where(x => !x.OnWall)
            .Select(x => x.Ref)
            .ToHashSet(StringComparer.Ordinal);
        var planned = new List<PlannedWired>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var box in boxes)
        {
            var kind = box.Kind?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(box.Ref) || !seen.Add(box.Ref))
            {
                warnings.Add(
                    $"A wired box without a ref, or named twice ({box.Ref}), is left out."
                );

                continue;
            }

            if (!KINDS.Contains(kind))
            {
                warnings.Add($"Wired {box.Ref}: '{box.Kind}' is not a kind of box; not saved.");

                continue;
            }

            if (!placed.Contains(box.Ref))
            {
                warnings.Add($"Wired {box.Ref} ({kind}): its furni is not in the room; not saved.");

                continue;
            }

            planned.Add(new PlannedWired(box.Ref, kind, box));
        }

        return [.. planned.OrderBy(x => Array.IndexOf(KINDS, x.Kind))];
    }

    internal static string Describe(UnmappedFurni x) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{x.Classname} (sprite {x.Sprite}{(x.OnWall ? ", wall" : "")}) x{x.Count}"
        );
}
