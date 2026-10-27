namespace TurboRoomCopy;

/// <summary>Under <c>Turbo:RoomCopy</c> in the server's configuration.</summary>
public sealed class RoomCopyConfig
{
    public const string SECTION_NAME = "Turbo:RoomCopy";

    /// <summary>The longest side a floor plan may have (the floor plan editor's own limit, <c>Turbo:Rooms:FloorPlanMaxAxisLength</c>).</summary>
    public int MaxAxisLength { get; init; } = 64;

    /// <summary>The longest room name kept; a longer one is cut (the navigator's <c>RoomNameMaxLength</c>).</summary>
    public int NameMaxLength { get; init; } = 60;

    /// <summary>The longest description kept; a longer one is cut.</summary>
    public int DescriptionMaxLength { get; init; } = 255;

    /// <summary>The most furni one import makes.</summary>
    public int MaxItems { get; init; } = 5000;

    /// <summary>The most users a room is made for (the source's own number up to this).</summary>
    public int MaxUsersLimit { get; init; } = 200;
}
