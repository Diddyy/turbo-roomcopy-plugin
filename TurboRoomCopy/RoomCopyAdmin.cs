using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Turbo.Contracts.Plugins.Admin;
using TurboRoomCopy.Database;

namespace TurboRoomCopy;

/// <summary>
/// The Room import pages of the admin panel: paste a room copy, name its owner (and a new name if
/// wanted) and get its dry run; open it to see what it would make and leave out, then import it
/// for a new room. Both need <c>plugin.roomcopy.import</c>, which no one holds until a hotel grants
/// it. The host logs every call but a GET to the command log.
/// </summary>
public sealed class RoomCopyAdmin(IServiceScopeFactory scopes, RoomCopyService service)
    : IPluginAdmin
{
    public const int PAGE_SIZE = 25;

    private static readonly JsonSerializerOptions JSON = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public void Configure(IPluginAdminBuilder admin)
    {
        var import = admin.Node("import", "Import copied rooms from the admin panel");

        admin.Map("GET", "imports", import, ListAsync);
        admin.Map("POST", "imports", import, PreviewAsync);
        admin.Map("GET", "imports/{id}", import, (r, ct) => WithImportAsync(r, ct, Detail));
        admin.Map("POST", "imports/{id}/run", import, RunAsync);
        admin.Map("DELETE", "imports/{id}", import, DiscardAsync);
        admin.Map(
            "GET",
            "imports/{id}/report",
            import,
            (r, ct) =>
                WithImportAsync(
                    r,
                    ct,
                    x =>
                        PluginAdminResult.FileOf(
                            Encoding.UTF8.GetBytes(ReportJson(x)),
                            "application/json",
                            $"roomcopy-import-{x.Id}.json"
                        )
                )
        );

        admin.Page(
            new PluginAdminPage(
                "import",
                "Room import",
                import,
                [
                    new PluginAdminBlock(
                        "form",
                        Title: "Preview a room copy",
                        Fields:
                        [
                            new PluginAdminField(
                                "document",
                                "Room copy",
                                "textarea",
                                Required: true,
                                Hint: "The room copy v1 JSON (see the plugin's README)."
                            ),
                            new PluginAdminField(
                                "owner",
                                "Owner",
                                Required: true,
                                Hint: "The player who will own the room, by name."
                            ),
                            new PluginAdminField(
                                "name",
                                "Room name",
                                Hint: "Empty: the copied room's own name."
                            ),
                        ],
                        Submit: new PluginAdminAction("Preview", "POST", "imports")
                    ),
                    new PluginAdminBlock(
                        "table",
                        Title: "Imports",
                        Endpoint: "imports",
                        Columns:
                        [
                            new PluginAdminColumn("id", "#", "number"),
                            new PluginAdminColumn("name", "Room"),
                            new PluginAdminColumn("source", "Copied from"),
                            new PluginAdminColumn("owner", "Owner"),
                            new PluginAdminColumn("furni", "Furni made", "number"),
                            new PluginAdminColumn("leftOut", "Left out", "number"),
                            new PluginAdminColumn("wired", "Wired"),
                            new PluginAdminColumn("status", "Status", "badge"),
                            new PluginAdminColumn("roomId", "Room id", "number"),
                            new PluginAdminColumn("created", "Previewed", "time"),
                        ],
                        Filters:
                        [
                            new PluginAdminField(
                                "status",
                                "Status",
                                "select",
                                Options:
                                [
                                    "",
                                    RoomImportEntity.PREVIEW,
                                    RoomImportEntity.IMPORTED,
                                    RoomImportEntity.FAILED,
                                ]
                            ),
                        ],
                        ItemPage: "detail"
                    ),
                    new PluginAdminBlock(
                        "text",
                        Title: "How an import works",
                        Text: "**Preview** reads the copy and plans the room without making it: the floor plan, the settings, which furni this hotel has (matched by sprite, then by class name) and which it does not, and the wired boxes. Open the preview to read it.\n\n"
                            + "**Import** makes the room for its owner: the floor plan, the room and every furni it has in one write, so a failed import leaves nothing behind. Then each wired box is saved the way its editor saves it, and the report says which saved and why any was refused. Variables named by a box that is not in the copy are reported, never made up."
                    ),
                ],
                "Make a room from a copy of a Habbo room.",
                Icon: "upload"
            )
        );
        admin.Page(
            new PluginAdminPage(
                "detail",
                "Room import",
                import,
                [
                    new PluginAdminBlock(
                        "detail",
                        Endpoint: "imports/{id}",
                        Columns:
                        [
                            new PluginAdminColumn("status", "Status", "badge"),
                            new PluginAdminColumn("name", "Room name"),
                            new PluginAdminColumn("owner", "Owner"),
                            new PluginAdminColumn("source", "Copied from"),
                            new PluginAdminColumn("floorPlan", "Floor plan"),
                            new PluginAdminColumn(
                                "floorItems",
                                "Floor furni in the copy",
                                "number"
                            ),
                            new PluginAdminColumn("wallItems", "Wall furni in the copy", "number"),
                            new PluginAdminColumn("furniMade", "Furni made", "number"),
                            new PluginAdminColumn("furniLeftOut", "Furni left out", "number"),
                            new PluginAdminColumn("unmapped", "Not in this hotel"),
                            new PluginAdminColumn("stuff", "Stuff data"),
                            new PluginAdminColumn("wiredBoxes", "Wired boxes", "number"),
                            new PluginAdminColumn("wiredSaved", "Wired saved"),
                            new PluginAdminColumn("wiredRefused", "Wired refused"),
                            new PluginAdminColumn("warnings", "Warnings"),
                            new PluginAdminColumn("roomId", "Room id", "number"),
                            new PluginAdminColumn("link", "Room"),
                            new PluginAdminColumn("error", "Error"),
                            new PluginAdminColumn("created", "Previewed", "time"),
                            new PluginAdminColumn("importedAt", "Imported", "time"),
                        ],
                        Downloads:
                        [
                            new PluginAdminAction("Report (JSON)", "GET", "imports/{id}/report"),
                        ]
                    ),
                    new PluginAdminBlock(
                        "actions",
                        Actions:
                        [
                            new PluginAdminAction(
                                "Import",
                                "POST",
                                "imports/{id}/run",
                                "Make this room for its owner now?",
                                When: $"status={RoomImportEntity.PREVIEW}"
                            ),
                            new PluginAdminAction(
                                "Discard",
                                "DELETE",
                                "imports/{id}",
                                "Discard this preview?",
                                Danger: true,
                                Then: "back",
                                When: $"status={RoomImportEntity.PREVIEW}"
                            ),
                        ]
                    ),
                ],
                Nav: false
            )
        );
    }

    private async Task<PluginAdminResult> ListAsync(
        PluginAdminRequest request,
        CancellationToken ct
    )
    {
        var page = Math.Max(
            0,
            int.TryParse(request.Query.GetValueOrDefault("page"), out var p) ? p : 0
        );
        var status = request.Query.GetValueOrDefault("status");

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RoomCopyDbContext>();
        var query = db.Imports.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(x => x.Id)
            .Skip(page * PAGE_SIZE)
            .Take(PAGE_SIZE)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return PluginAdminResult.Ok(
            new
            {
                items = rows.Select(Row).ToList(),
                total,
                pageSize = PAGE_SIZE,
            }
        );
    }

    private async Task<PluginAdminResult> PreviewAsync(
        PluginAdminRequest request,
        CancellationToken ct
    )
    {
        if (request.Body is not { ValueKind: JsonValueKind.Object } body)
            return PluginAdminResult.BadRequest("Send the room copy, its owner and a name.");

        var outcome = await service
            .PreviewAsync(
                Text(body, "document"),
                Text(body, "owner"),
                Text(body, "name"),
                request.PlayerId,
                ct
            )
            .ConfigureAwait(false);

        return outcome.Error is { } error
            ? PluginAdminResult.BadRequest(error)
            : PluginAdminResult.Ok(Detail(outcome.Import!));
    }

    private async Task<PluginAdminResult> RunAsync(PluginAdminRequest request, CancellationToken ct)
    {
        if (!TryId(request, out var id))
            return PluginAdminResult.NotFound("There is no such import.");

        var outcome = await service.ImportAsync(id, request.PlayerId, ct).ConfigureAwait(false);

        if (outcome.Import is null)
            return PluginAdminResult.NotFound(outcome.Error!);

        return outcome.Error is { } error
            ? new PluginAdminResult(
                outcome.Import.Status == RoomImportEntity.FAILED ? 500 : 409,
                Error: error
            )
            : PluginAdminResult.Ok(Detail(outcome.Import));
    }

    private async Task<PluginAdminResult> DiscardAsync(
        PluginAdminRequest request,
        CancellationToken ct
    )
    {
        if (!TryId(request, out var id))
            return PluginAdminResult.NotFound("There is no such import.");

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RoomCopyDbContext>();
        var deleted = await db
            .Imports.Where(x => x.Id == id && x.Status == RoomImportEntity.PREVIEW)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        return deleted == 0
            ? PluginAdminResult.NotFound($"There is no preview {id} to discard.")
            : PluginAdminResult.NoContent();
    }

    private async Task<PluginAdminResult> WithImportAsync(
        PluginAdminRequest request,
        CancellationToken ct,
        Func<RoomImportEntity, object> answer
    )
    {
        if (!TryId(request, out var id))
            return PluginAdminResult.NotFound("There is no such import.");

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RoomCopyDbContext>();
        var import = await db
            .Imports.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);

        if (import is null)
            return PluginAdminResult.NotFound($"There is no import {id}.");

        var value = answer(import);

        return value as PluginAdminResult ?? PluginAdminResult.Ok(value);
    }

    private static bool TryId(PluginAdminRequest request, out int id) =>
        int.TryParse(
            request.Route.GetValueOrDefault("id"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out id
        );

    private static string? Text(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Source(RoomImportEntity x) =>
        x.SourceRoomId is null ? x.SourceHotel ?? "" : $"{x.SourceHotel} room {x.SourceRoomId}";

    private static JsonElement PreviewOf(RoomImportEntity x) =>
        JsonDocument.Parse(x.Preview).RootElement.Clone();

    private static JsonElement? ReportOf(RoomImportEntity x) =>
        x.Report is null ? null : JsonDocument.Parse(x.Report).RootElement.Clone();

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var n) ? n : 0;

    internal static object Row(RoomImportEntity x)
    {
        var preview = PreviewOf(x);

        return new
        {
            id = x.Id,
            name = x.RoomName,
            source = Source(x),
            owner = x.OwnerName,
            furni = Number(preview, "furniMade"),
            leftOut = Number(preview, "furniLeftOut"),
            wired = Wired(x, preview),
            status = x.Status,
            roomId = x.RoomId,
            created = x.CreatedAt,
        };
    }

    /// <summary>Saved of saved-or-refused once imported; before, how many will be saved.</summary>
    private static string Wired(RoomImportEntity x, JsonElement preview)
    {
        if (ReportOf(x) is { } report && report.TryGetProperty("wired", out var wired))
        {
            var boxes = wired.EnumerateArray().ToList();

            return $"{boxes.Count(b => b.GetProperty("saved").GetBoolean())}/{boxes.Count} saved";
        }

        return $"{Number(preview, "wiredToSave")} to save";
    }

    internal static object Detail(RoomImportEntity x)
    {
        var preview = PreviewOf(x);
        var report = ReportOf(x);
        var wired =
            report is { } r && r.TryGetProperty("wired", out var w)
                ? w.EnumerateArray().ToList()
                : [];

        static string Lines(JsonElement preview, string name) =>
            preview.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
                ? string.Join(" | ", list.EnumerateArray().Select(x => x.GetString()))
                : "";

        return new
        {
            id = x.Id,
            status = x.Status,
            name = x.RoomName,
            owner = x.OwnerName,
            source = Source(x),
            floorPlan = preview.TryGetProperty("floorPlan", out var plan) ? plan.GetString() : null,
            floorItems = Number(preview, "floorItems"),
            wallItems = Number(preview, "wallItems"),
            furniMade = Number(preview, "furniMade"),
            furniLeftOut = Number(preview, "furniLeftOut"),
            unmapped = Lines(preview, "unmapped"),
            stuff = preview.TryGetProperty("stuff", out var stuff)
                ? string.Join(", ", stuff.EnumerateObject().Select(s => $"{s.Name} {s.Value}"))
                : "",
            wiredBoxes = Number(preview, "wiredBoxes"),
            wiredSaved = report is null
                ? $"{Number(preview, "wiredToSave")} to save"
                : string.Join(
                    ", ",
                    wired
                        .Where(b => b.GetProperty("saved").GetBoolean())
                        .Select(b =>
                            $"{b.GetProperty("kind").GetString()} {b.GetProperty("ref").GetString()}"
                        )
                ),
            wiredRefused = string.Join(
                " | ",
                wired
                    .Where(b =>
                        !b.GetProperty("saved").GetBoolean()
                        || b.GetProperty("problems").GetArrayLength() > 0
                    )
                    .Select(b =>
                        $"{b.GetProperty("kind").GetString()} {b.GetProperty("ref").GetString()}: "
                        + string.Join(
                            "; ",
                            new[] { b.GetProperty("error").GetString() }
                                .Concat(
                                    b.GetProperty("problems")
                                        .EnumerateArray()
                                        .Select(p => p.GetString())
                                )
                                .Where(s => !string.IsNullOrEmpty(s))
                        )
                    )
            ),
            warnings = Lines(preview, "warnings"),
            roomId = x.RoomId,
            link = x.RoomId is { } roomId
                ? $"/rooms/{roomId} (admin), :goto {roomId} in the hotel"
                : null,
            error = x.Error,
            created = x.CreatedAt,
            importedAt = x.ImportedAt,
        };
    }

    private static string ReportJson(RoomImportEntity x) =>
        JsonSerializer.Serialize(
            new
            {
                id = x.Id,
                status = x.Status,
                owner = new { id = x.OwnerId, name = x.OwnerName },
                roomId = x.RoomId,
                preview = PreviewOf(x),
                report = ReportOf(x),
                error = x.Error,
            },
            JSON
        );
}
