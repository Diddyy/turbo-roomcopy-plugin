using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Turbo.Database.Context;
using Turbo.Database.Entities.Room;
using Turbo.Database.Furniture;
using Turbo.Primitives.Action;
using Turbo.Primitives.Messages.Incoming.Userdefinedroomevents;
using Turbo.Primitives.Movements;
using Turbo.Primitives.Navigator;
using Turbo.Primitives.Orleans;
using Turbo.Primitives.Players;
using Turbo.Primitives.Rooms;
using Turbo.Primitives.Rooms.Providers;

namespace TurboRoomCopy;

/// <summary>
/// <see cref="IRoomCopyWriter"/> over the server's own services, so an imported room is written
/// as the server writes one: the floor plan as the floor plan editor saves it (a custom model row
/// named <c>room_&lt;id&gt;</c>, so a later edit in the editor changes that row), the room row as
/// the navigator makes one, the furni through the hotel's furni writer (with their movements, cause
/// <see cref="CAUSE"/>), and each wired box through the room grain's save, which checks it as the
/// editor's save is checked.
/// </summary>
public sealed class TurboRoomCopyWriter(
    IDbContextFactory<TurboDbContext> dbCtxFactory,
    IRoomFurniWriter furniWriter,
    IRoomModelProvider models,
    IGrainFactory grainFactory,
    ILogger<TurboRoomCopyWriter> logger
) : IRoomCopyWriter
{
    /// <summary>The movement cause of furni an import made; its ref is the import's id.</summary>
    public const string CAUSE = "plugin.roomcopy";

    public async Task<(int Id, string Name)?> FindPlayerAsync(string name, CancellationToken ct)
    {
        var db = await dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);

        var player = await db
            .Players.AsNoTracking()
            .Where(x => x.Name == name.Trim())
            .Select(x => new { x.Id, x.Name })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return player is null ? null : (player.Id, player.Name);
    }

    public async Task<CreatedRoom> CreateAsync(
        RoomCopyPlan plan,
        int ownerId,
        int actorId,
        int importId,
        CancellationToken ct
    )
    {
        var db = await dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);

        var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var txScope = tx.ConfigureAwait(false);

        var owner = await db.Players.SingleAsync(x => x.Id == ownerId, ct).ConfigureAwait(false);

        // Named after the room once it has an id; the name is unique, so a placeholder until then.
        var model = new RoomModelEntity
        {
            Name = $"roomcopy_{Guid.NewGuid():N}",
            Model = plan.Model.Heightmap,
            DoorX = plan.Model.DoorX,
            DoorY = plan.Model.DoorY,
            DoorRotation = plan.Model.DoorRotation,
            Enabled = true,
            Custom = true,
        };

        var settings = plan.Room;
        var room = new RoomEntity
        {
            Name = settings.Name,
            Description = settings.Description,
            PlayerEntityId = ownerId,
            PlayerEntity = owner,
            DoorMode = settings.DoorMode,
            RoomModelEntityId = 0,
            RoomModelEntity = model,
            UsersNow = 0,
            PlayersMax = settings.PlayersMax,
            PaintWall = settings.PaintWall,
            PaintFloor = settings.PaintFloor,
            PaintLandscape = settings.PaintLandscape,
            WallHeight = settings.WallHeight,
            HideWalls = settings.HideWalls,
            ThicknessWall = settings.ThicknessWall,
            ThicknessFloor = settings.ThicknessFloor,
            AllowBlocking = false,
            AllowPets = settings.AllowPets,
            AllowPetsEat = settings.AllowPetsEat,
            TradeType = settings.TradeType,
            MuteType = settings.MuteType,
            KickType = settings.KickType,
            BanType = settings.BanType,
            ChatFloodType = settings.ChatFlood,
            HideOwner = settings.HideOwner,
            Tags = settings.Tags,
        };

        db.RoomModels.Add(model);
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        model.Name = $"room_{room.Id}";
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var made = await furniWriter
            .CreateInRoomAsync(
                db,
                room.Id,
                ownerId,
                [
                    .. plan.Furni.Select(x => new NewRoomFurni(
                        x.DefinitionId,
                        x.ExtraDataJson,
                        x.Position
                    )),
                ],
                MovementCause.New(CAUSE, actorId, importId),
                ct
            )
            .ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        // The running server learns the floor plan the way the editor's save tells it; the rows
        // are committed, so a room grain that activates now finds everything. The room exists
        // now, so these finish whatever happens to the call.
        if (
            await models.ReloadModelAsync(model.Id, CancellationToken.None).ConfigureAwait(false)
            is null
        )
            logger.LogWarning(
                "Room copy import {ImportId}: room {RoomId}'s floor plan {ModelId} did not load",
                importId,
                room.Id,
                model.Id
            );

        // As the navigator does after it makes a room, so no listing is cached without it.
        await grainFactory
            .GetRoomDirectoryGrain()
            .PublishListingChangesAsync(
                [
                    NavigatorListingKeys.Owner(ownerId),
                    NavigatorListingKeys.OwnerRoomCount(ownerId),
                    NavigatorListingKeys.SEARCH,
                ],
                CancellationToken.None
            )
            .ConfigureAwait(false);

        var ids = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < made.Count; i++)
            ids[plan.Furni[i].Ref] = made[i];

        return new CreatedRoom(room.Id, model.Id, ids);
    }

    public async Task<WiredSaveOutcome> SaveWiredAsync(
        int roomId,
        int actorId,
        UpdateWiredMessage message,
        CancellationToken ct
    )
    {
        var room = grainFactory.GetRoomGrain(roomId);

        // A room nobody has entered has not loaded its furni: the save would find no box. Loading
        // is done once; later calls find it loaded.
        await room.EnsureRoomActiveAsync(ct).ConfigureAwait(false);

        var result = await room.ApplyWiredUpdateAsync(
                ActionContext.CreateForPlayer(actorId, roomId),
                message.Id,
                message,
                ct
            )
            .ConfigureAwait(false);

        return new WiredSaveOutcome(
            result.IsSaved,
            result.ErrorKey,
            result.Parameters.Count == 0
                ? null
                : string.Join(", ", result.Parameters.Select(x => $"{x.Key}={x.Value}"))
        );
    }
}
