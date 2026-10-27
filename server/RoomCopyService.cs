using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboRoomCopy.Database;
using TurboRoomCopy.Format;

namespace TurboRoomCopy;

/// <summary>
/// The two steps of an import. <see cref="PreviewAsync"/> reads a pasted room copy, finds its
/// owner and plans it (<see cref="RoomCopyPlanner"/>) without writing a room, and keeps the copy
/// and its dry run. <see cref="ImportAsync"/> plans that copy again against the furni as they are
/// then, makes the room, its floor plan and furni in one write (all or nothing), and saves each
/// wired box through the room, reporting each as saved or refused. How each import ended is told to
/// other plugins (<see cref="Notices"/>).
/// </summary>
public sealed class RoomCopyService(
    IServiceScopeFactory scopes,
    IRoomCopyWriter writer,
    RoomCopyPlanner planner,
    TimeProvider time,
    Notices notices,
    ILogger<RoomCopyService> logger
)
{
    private static readonly JsonSerializerOptions REPORT_JSON = new(JsonSerializerDefaults.Web);

    public sealed record Outcome(RoomImportEntity? Import, string? Error);

    public async Task<Outcome> PreviewAsync(
        string? document,
        string? ownerName,
        string? nameOverride,
        int staffId,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(document))
            return new(null, "Paste a room copy (room copy v1 JSON).");

        if (string.IsNullOrWhiteSpace(ownerName))
            return new(null, "Name the player who will own the room.");

        if (Read(document, out var error) is not { } copy)
            return new(null, error);

        if (await writer.FindPlayerAsync(ownerName, ct).ConfigureAwait(false) is not { } owner)
            return new(null, $"There is no player called {ownerName.Trim()}.");

        nameOverride = string.IsNullOrWhiteSpace(nameOverride) ? null : nameOverride.Trim();

        var plan = planner.Plan(copy, nameOverride);

        if (!plan.CanImport)
            return new(null, "This copy can't be imported: " + string.Join(" ", plan.Errors));

        var import = new RoomImportEntity
        {
            StaffId = staffId,
            OwnerId = owner.Id,
            OwnerName = owner.Name,
            NameOverride = nameOverride,
            RoomName = plan.Room.Name,
            SourceHotel = Cut(copy.Source?.Hotel, 64),
            SourceRoomId = Cut(copy.Source?.RoomId, 32),
            Status = RoomImportEntity.PREVIEW,
            Document = document,
            Preview = JsonSerializer.Serialize(Summary(copy, plan), REPORT_JSON),
        };

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RoomCopyDbContext>();

        db.Imports.Add(import);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new(import, null);
    }

    public async Task<Outcome> ImportAsync(int importId, int staffId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RoomCopyDbContext>();

        // Taken from preview to running in one statement, so two clicks make one room.
        var taken = await db
            .Imports.Where(x => x.Id == importId && x.Status == RoomImportEntity.PREVIEW)
            .ExecuteUpdateAsync(up => up.SetProperty(x => x.Status, RoomImportEntity.RUNNING), ct)
            .ConfigureAwait(false);

        var import = await db
            .Imports.SingleOrDefaultAsync(x => x.Id == importId, ct)
            .ConfigureAwait(false);

        if (import is null)
            return new(null, $"There is no import {importId}.");

        if (taken == 0)
            return new(
                import,
                $"Import {importId} is {import.Status}: only a preview can be imported."
            );

        RoomCopyDocument copy;
        RoomCopyPlan plan;
        CreatedRoom created;

        try
        {
            copy =
                Read(import.Document, out var error) ?? throw new InvalidOperationException(error);
            plan = planner.Plan(copy, import.NameOverride);

            if (!plan.CanImport)
                throw new InvalidOperationException(string.Join(" ", plan.Errors));

            created = await writer
                .CreateAsync(plan, import.OwnerId, staffId, import.Id, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Nothing was committed (a cancelled or timed out call too): the import is over.
            logger.LogError(ex, "Room copy import {ImportId} failed", importId);

            import.Status = RoomImportEntity.FAILED;
            import.Error = Cut(ex.GetBaseException().Message, 1000);
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            await notices.FailedAsync(import, CancellationToken.None).ConfigureAwait(false);

            return new(
                import,
                $"The import failed and made nothing: {ex.GetBaseException().Message}"
            );
        }

        // The room stands from here on: a box the room refuses is reported, not undone, and the
        // import is recorded whatever happens to the call.
        var wired = await SaveWiredAsync(plan, created, staffId, ct).ConfigureAwait(false);

        import.Status = RoomImportEntity.IMPORTED;
        import.RoomId = created.RoomId;
        import.RoomName = plan.Room.Name;
        import.ImportedBy = staffId;
        import.ImportedAt = time.GetUtcNow().UtcDateTime;
        import.Preview = JsonSerializer.Serialize(Summary(copy, plan), REPORT_JSON);
        import.Report = JsonSerializer.Serialize(
            new
            {
                roomId = created.RoomId,
                modelId = created.ModelId,
                furniMade = created.ItemIds.Count,
                items = created.ItemIds,
                wired,
            },
            REPORT_JSON
        );

        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        logger.LogInformation(
            "Room copy import {ImportId} by staff {StaffId} made room {RoomId} for player {OwnerId}: {Furni} furni, {Saved}/{Boxes} wired boxes saved",
            import.Id,
            staffId,
            created.RoomId,
            import.OwnerId,
            created.ItemIds.Count,
            wired.Count(x => x.Saved),
            wired.Count
        );

        await notices
            .DoneAsync(
                import,
                created.ItemIds.Count,
                wired.Count(x => x.Saved),
                wired.Count,
                CancellationToken.None
            )
            .ConfigureAwait(false);

        return new(import, null);
    }

    public sealed record WiredResult(
        string Ref,
        int ItemId,
        string Kind,
        bool Saved,
        string? Error,
        List<string> Problems
    );

    private async Task<List<WiredResult>> SaveWiredAsync(
        RoomCopyPlan plan,
        CreatedRoom created,
        int staffId,
        CancellationToken ct
    )
    {
        var variables = WiredCopy.VariableIds(
            plan.Wired.Where(x => x.Kind == RoomCopyPlanner.KIND_VARIABLE),
            created.ItemIds
        );
        var results = new List<WiredResult>();

        foreach (var box in plan.Wired)
        {
            var built = WiredCopy.Build(box, created.ItemIds, variables);
            WiredSaveOutcome outcome;

            try
            {
                outcome = await writer
                    .SaveWiredAsync(created.RoomId, staffId, built.Message, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                outcome = new(false, "not saved: the call ran out of time", null);
            }
            catch (Exception ex)
            {
                outcome = new(false, ex.GetBaseException().Message, null);
            }

            results.Add(
                new WiredResult(
                    box.Ref,
                    built.Message.Id,
                    box.Kind,
                    outcome.Saved,
                    outcome.Saved
                        ? null
                        : (outcome.ErrorKey ?? "refused")
                            + (outcome.Parameters is null ? "" : $" ({outcome.Parameters})"),
                    built.Problems
                )
            );
        }

        return results;
    }

    internal static RoomCopyDocument? Read(string document, out string? error)
    {
        error = null;

        try
        {
            var copy = JsonSerializer.Deserialize<RoomCopyDocument>(
                document,
                RoomCopyDocument.JSON
            );

            if (copy is null)
                error = "The room copy is empty.";

            return copy;
        }
        catch (JsonException ex)
        {
            error = $"The room copy is not room copy v1 JSON: {ex.Message}";

            return null;
        }
    }

    /// <summary>The dry run as the panel shows it.</summary>
    internal static object Summary(RoomCopyDocument copy, RoomCopyPlan plan) =>
        new
        {
            source = copy.Source,
            name = plan.Room.Name,
            floorPlan = $"{plan.Model.Width}x{plan.Model.Height}, door {plan.Model.DoorX},{plan.Model.DoorY} facing {(int)plan.Model.DoorRotation}",
            floorItems = copy.FloorItems.Count,
            wallItems = copy.WallItems.Count,
            furniMade = plan.Furni.Count,
            furniLeftOut = plan.Unmapped.Sum(x => x.Count),
            unmapped = plan.Unmapped.Select(RoomCopyPlanner.Describe).ToList(),
            stuff = plan
                .Furni.Where(x => x.ExtraDataJson is not null)
                .GroupBy(x => StuffKind(x.ExtraDataJson!))
                .ToDictionary(x => x.Key, x => x.Count()),
            wiredBoxes = copy.Wired.Count,
            wiredToSave = plan.Wired.Count,
            wiredWithoutConfig = plan.UnconfiguredWired.Count,
            warnings = plan.Warnings,
        };

    private static string StuffKind(string extraData)
    {
        using var json = JsonDocument.Parse(extraData);

        return json.RootElement.GetProperty("stuff").GetProperty("Data").ValueKind switch
        {
            JsonValueKind.Object => "map",
            JsonValueKind.Array => "number",
            _ => "legacy",
        };
    }

    private static string? Cut(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
