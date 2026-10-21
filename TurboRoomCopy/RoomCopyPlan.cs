using System.Collections.Generic;
using Turbo.Primitives.Movements;
using Turbo.Primitives.Navigator.Enums;
using Turbo.Primitives.Rooms.Enums;
using TurboRoomCopy.Format;

namespace TurboRoomCopy;

/// <summary>
/// What an import makes from a room copy, worked out before anything is written: the dry run
/// shows it and the import writes it. Furni the hotel has no definition for are left out and
/// counted (<see cref="Unmapped"/>); everything else that is not copied as it was is a
/// <see cref="Warnings"/> line.
/// </summary>
public sealed record RoomCopyPlan
{
    public required PlannedModel Model { get; init; }

    public required PlannedRoom Room { get; init; }

    public required List<PlannedFurni> Furni { get; init; }

    public required List<PlannedWired> Wired { get; init; }

    /// <summary>
    /// The refs of wired boxes the copy has no config for (Habbo lets few read a room's wired):
    /// placed as a box is when it is first put down, with nothing set.
    /// </summary>
    public required List<string> UnconfiguredWired { get; init; }

    public required List<UnmappedFurni> Unmapped { get; init; }

    public required List<string> Warnings { get; init; }

    /// <summary>Why the copy can't be imported at all; empty when it can.</summary>
    public required List<string> Errors { get; init; }

    public bool CanImport => Errors.Count == 0;
}

/// <summary>The floor plan, cleaned the way the floor plan editor's save cleans one.</summary>
public sealed record PlannedModel(
    string Heightmap,
    int Width,
    int Height,
    int DoorX,
    int DoorY,
    Rotation DoorRotation
);

/// <summary>The room row's settings.</summary>
public sealed record PlannedRoom
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required int PlayersMax { get; init; }

    public required RoomDoorModeType DoorMode { get; init; }

    public required RoomTradeModeType TradeType { get; init; }

    public required bool AllowPets { get; init; }

    public required bool AllowPetsEat { get; init; }

    public required bool HideOwner { get; init; }

    public required bool HideWalls { get; init; }

    public required RoomThicknessType ThicknessWall { get; init; }

    public required RoomThicknessType ThicknessFloor { get; init; }

    public required int WallHeight { get; init; }

    public string? PaintWall { get; init; }

    public string? PaintFloor { get; init; }

    public string? PaintLandscape { get; init; }

    public required ChatFloodSensitivityType ChatFlood { get; init; }

    public required ModSettingType MuteType { get; init; }

    public required ModSettingType KickType { get; init; }

    public required ModSettingType BanType { get; init; }

    public string? Tags { get; init; }
}

/// <summary>
/// One furni to make: the source's ref, the definition it maps to, where it stands and its extra
/// data; <see cref="WiredBox"/> when its definition is a wired box (a trigger, action, condition,
/// selector, add-on or variable).
/// </summary>
public sealed record PlannedFurni(
    string Ref,
    int DefinitionId,
    string DefinitionName,
    bool OnWall,
    RoomPosition Position,
    string? ExtraDataJson,
    bool WiredBox = false
);

/// <summary>One wired box to save once its furni exist; its refs are turned into the new ids then.</summary>
public sealed record PlannedWired(string Ref, string Kind, RoomCopyWired Source);

/// <summary>Furni of one kind the hotel has no definition for.</summary>
public sealed record UnmappedFurni(string Classname, int Sprite, bool OnWall, int Count);
