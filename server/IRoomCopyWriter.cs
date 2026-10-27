using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Turbo.Primitives.Messages.Incoming.Userdefinedroomevents;

namespace TurboRoomCopy;

/// <summary>
/// The server's side of an import (<see cref="TurboRoomCopyWriter"/>): finding the owner, making
/// the room with its model and furni in one write, and saving a wired box through the room.
/// </summary>
public interface IRoomCopyWriter
{
    Task<(int Id, string Name)?> FindPlayerAsync(string name, CancellationToken ct);

    /// <summary>
    /// Makes the floor plan, the room and its furni in one transaction, or nothing; then has the
    /// running server pick up the new floor plan and list the room. The furni's new ids by ref.
    /// </summary>
    Task<CreatedRoom> CreateAsync(
        RoomCopyPlan plan,
        int ownerId,
        int actorId,
        int importId,
        CancellationToken ct
    );

    /// <summary>Saves a box as its editor's save does (the room's own checks), on behalf of <paramref name="actorId"/>.</summary>
    Task<WiredSaveOutcome> SaveWiredAsync(
        int roomId,
        int actorId,
        UpdateWiredMessage message,
        CancellationToken ct
    );
}

public sealed record CreatedRoom(int RoomId, int ModelId, IReadOnlyDictionary<string, int> ItemIds);

public sealed record WiredSaveOutcome(bool Saved, string? ErrorKey, string? Parameters);
