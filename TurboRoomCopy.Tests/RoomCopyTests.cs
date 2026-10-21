using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Turbo.Contracts.Plugins.Admin;
using Turbo.Primitives.Messages.Incoming.Userdefinedroomevents;
using Turbo.Primitives.Navigator.Enums;
using Turbo.Primitives.Rooms.Enums;
using Turbo.Primitives.Rooms.Enums.Wired;
using Turbo.Rooms.Wired.Variables;
using TurboRoomCopy.Database;
using TurboRoomCopy.Format;
using Xunit;

namespace TurboRoomCopy.Tests;

public class PlannerTests
{
    private static RoomCopyPlan Plan(JsonObject copy, string? name = null) =>
        new RoomCopyPlanner(Samples.Definitions(), new RoomCopyConfig()).Plan(
            RoomCopyService.Read(copy.ToJsonString(), out _)!,
            name
        );

    [Fact]
    public void Furni_map_by_sprite_then_by_class_name_and_the_rest_are_counted()
    {
        var plan = Plan(Samples.Copy());
        var byRef = plan.Furni.ToDictionary(x => x.Ref);

        Assert.True(plan.CanImport);
        Assert.Equal(9, plan.Furni.Count);
        Assert.Equal("room_info15_div", byRef["10"].DefinitionName);
        // Sprite 777 is another furni here: the class name wins, and says so.
        Assert.Equal("chair_norja", byRef["16"].DefinitionName);
        Assert.Contains(plan.Warnings, x => x.Contains("sprite 777 is table_norja here"));
        var unmapped = Assert.Single(plan.Unmapped);
        Assert.Equal(
            ("mystery_furni", 99999, 2),
            (unmapped.Classname, unmapped.Sprite, unmapped.Count)
        );
    }

    [Fact]
    public void Places_are_kept_exactly_and_a_wall_item_reads_its_wall_position()
    {
        var plan = Plan(Samples.Copy());
        var byRef = plan.Furni.ToDictionary(x => x.Ref);

        Assert.Equal(
            (1, 2, 0.000002, Rotation.NorthEast),
            (
                byRef["12"].Position.X,
                byRef["12"].Position.Y,
                byRef["12"].Position.Z,
                byRef["12"].Position.Rotation
            )
        );
        Assert.Equal(
            (2, 2, 1.25, Rotation.South),
            (
                byRef["16"].Position.X,
                byRef["16"].Position.Y,
                byRef["16"].Position.Z,
                byRef["16"].Position.Rotation
            )
        );

        var poster = byRef["20"];

        Assert.True(poster.OnWall);
        // "r" is the right wall, which faces west (WallPosition).
        Assert.Equal(
            (3, 2, 10, Rotation.West),
            (
                poster.Position.X,
                poster.Position.Y,
                poster.Position.WallOffset,
                poster.Position.Rotation
            )
        );
        Assert.Equal(40, poster.Position.Z);
    }

    [Fact]
    public void Stuff_data_is_written_in_the_servers_extra_data_shape()
    {
        var byRef = Plan(Samples.Copy()).Furni.ToDictionary(x => x.Ref);

        // An empty legacy state is kept as Habbo sends it ("" and not the server's "0").
        Assert.Equal(
            """{"stuff":{"Data":"","UniqueNumber":0,"UniqueSeries":0}}""",
            byRef["10"].ExtraDataJson
        );
        Assert.Equal(
            """{"stuff":{"Data":"0","UniqueNumber":0,"UniqueSeries":0}}""",
            byRef["11"].ExtraDataJson
        );
        Assert.Equal(
            """{"stuff":{"Data":{"state":"0","offsetX":"319","imageUrl":"https://images.habbo.com/c_images/album3831/infoa15_d.png"},"UniqueNumber":0,"UniqueSeries":0}}""",
            byRef["12"].ExtraDataJson
        );
        Assert.Equal(
            """{"stuff":{"Data":[1,134,206,159],"UniqueNumber":0,"UniqueSeries":0}}""",
            byRef["13"].ExtraDataJson
        );
        // No stuff in the copy: none written, the furni starts as new.
        Assert.Null(byRef["16"].ExtraDataJson);
    }

    [Fact]
    public void The_room_keeps_every_setting_of_the_copy()
    {
        var copy = Samples.Copy();

        copy["room"]!["moderation"] = new JsonObject
        {
            ["mute"] = 1,
            ["kick"] = 2,
            ["ban"] = 0,
        };
        copy["room"]!["tags"] = new JsonArray("info", "bus", "Info");
        copy["room"]!["tradeMode"] = 2;

        var plan = Plan(copy);
        var room = plan.Room;

        Assert.Equal("Ambassador InfoBus Park", room.Name);
        Assert.Equal(50, room.PlayersMax);
        Assert.True(room.HideWalls);
        Assert.True(room.HideOwner);
        Assert.Equal(RoomThicknessType.Thinnest, room.ThicknessWall);
        Assert.Equal(RoomThicknessType.Thinnest, room.ThicknessFloor);
        Assert.Equal("0.0", room.PaintLandscape);
        Assert.Null(room.PaintWall);
        Assert.Equal(ChatFloodSensitivityType.Normal, room.ChatFlood);
        Assert.Equal(
            (ModSettingType.Rights, ModSettingType.All, ModSettingType.Owner),
            (room.MuteType, room.KickType, room.BanType)
        );
        Assert.Equal(RoomTradeModeType.Everyone, room.TradeType);
        Assert.Equal("info,bus", room.Tags);
        Assert.DoesNotContain(plan.Warnings, x => x.Contains("chat"));
        Assert.Equal("My park", Plan(copy, "  My park ").Room.Name);
    }

    [Fact]
    public void What_the_server_cannot_keep_is_a_warning()
    {
        var copy = Samples.Copy();

        copy["room"]!["doorMode"] = 2;
        copy["room"]!["chat"]!["bubbleWidth"] = 0;
        copy["room"]!["name"] = new string('n', 80);

        var plan = Plan(copy);

        Assert.Equal(RoomDoorModeType.Locked, plan.Room.DoorMode);
        Assert.Equal(60, plan.Room.Name.Length);
        Assert.Contains(plan.Warnings, x => x.Contains("password"));
        Assert.Contains(plan.Warnings, x => x.Contains("bubble width"));
        Assert.Contains(plan.Warnings, x => x.Contains("cut to 60"));
    }

    [Fact]
    public void The_floor_plan_keeps_to_the_floor_plan_editors_rules()
    {
        var plan = Plan(Samples.Copy());

        Assert.Equal("xxxxx\rx000x\rx000x\rxx0xx", plan.Model.Heightmap);
        Assert.Equal(
            (5, 4, 2, 3, Rotation.North),
            (
                plan.Model.Width,
                plan.Model.Height,
                plan.Model.DoorX,
                plan.Model.DoorY,
                plan.Model.DoorRotation
            )
        );

        var copy = Samples.Copy();

        copy["model"]!["doorX"] = 0;
        Assert.Contains("not on an open tile", string.Join(" ", Plan(copy).Errors));

        copy = Samples.Copy();
        copy["model"]!["heightmap"] = "xx\rx?";
        Assert.Contains("'?'", string.Join(" ", Plan(copy).Errors));

        copy = Samples.Copy();
        copy["model"]!["heightmap"] = string.Join("\r", Enumerable.Repeat(new string('0', 65), 3));
        copy["model"]!["doorX"] = 1;
        copy["model"]!["doorY"] = 1;
        Assert.Contains("no side may pass 64", string.Join(" ", Plan(copy).Errors));

        copy = Samples.Copy();
        copy["version"] = 2;
        Assert.False(Plan(copy).CanImport);
    }

    [Fact]
    public void Wired_is_saved_variables_first_and_only_for_boxes_in_the_room()
    {
        var copy = Samples.Copy();

        ((JsonArray)copy["wired"]!).Add(new JsonObject { ["ref"] = "14", ["kind"] = "action" });
        ((JsonArray)copy["wired"]!).Add(new JsonObject { ["ref"] = "10", ["kind"] = "gizmo" });

        var plan = Plan(copy);

        Assert.Equal(["variable", "trigger", "action"], plan.Wired.Select(x => x.Kind));
        Assert.Contains(
            plan.Warnings,
            x => x.Contains("Wired 14") && x.Contains("not in the room")
        );
        Assert.Contains(plan.Warnings, x => x.Contains("'gizmo'"));
    }
}

public class WiredCopyTests
{
    [Fact]
    public void A_box_picks_the_new_ids_and_names_the_new_variables()
    {
        var plan = new RoomCopyPlanner(Samples.Definitions(), new RoomCopyConfig()).Plan(
            RoomCopyService.Read(Samples.Copy().ToJsonString(), out _)!,
            null
        );
        var ids = plan
            .Furni.Select((x, i) => (x.Ref, Id: 2000 + i))
            .ToDictionary(x => x.Ref, x => x.Id);
        var variables = WiredCopy.VariableIds(plan.Wired.Where(x => x.Kind == "variable"), ids);
        var action = WiredCopy.Build(plan.Wired.Single(x => x.Kind == "action"), ids, variables);
        var message = Assert.IsType<UpdateActionMessage>(action.Message);

        Assert.Equal(ids[Samples.ACTION_BOX], message.Id);
        Assert.Equal([ids["10"], ids["11"]], message.StuffIds);
        Assert.Equal([3], message.DefinitionSpecifics);
        Assert.Equal([1, 1, 1, 0], message.IntParams);
        // The copied box's variable is renamed after the new box; the unknown one is passed on and reported.
        Assert.Equal(
            [
                WiredVariableIdBuilder.CreateFromBoxId(ids[Samples.VARIABLE_BOX]).Value.ToString(),
                Samples.UNKNOWN_VARIABLE,
            ],
            message.VariableIds
        );
        Assert.Contains(action.Problems, x => x.Contains("404"));
        Assert.Contains(
            action.Problems,
            x => x.Contains(Samples.UNKNOWN_VARIABLE) && x.Contains("unknown")
        );
        Assert.Equal(2, action.Problems.Count);
    }

    [Fact]
    public void Each_kind_is_its_own_message_with_its_own_specifics()
    {
        var ids = new System.Collections.Generic.Dictionary<string, int> { ["1"] = 11 };
        var empty = new System.Collections.Generic.Dictionary<ulong, ulong>();

        UpdateWiredMessage Build(string kind, RoomCopyWired source) =>
            WiredCopy
                .Build(new PlannedWired("1", kind, source with { Ref = "1" }), ids, empty)
                .Message;

        Assert.IsType<UpdateTriggerMessage>(Build("trigger", new RoomCopyWired()));
        Assert.Equal(
            [2],
            Assert
                .IsType<UpdateConditionMessage>(
                    Build("condition", new RoomCopyWired { QuantifierCode = 2 })
                )
                .DefinitionSpecifics
        );
        Assert.Equal(
            [true, false],
            Assert
                .IsType<UpdateSelectorMessage>(
                    Build("selector", new RoomCopyWired { IsFilter = true })
                )
                .DefinitionSpecifics
        );
        Assert.IsType<UpdateAddonMessage>(Build("addon", new RoomCopyWired()));
        Assert.IsType<UpdateVariableMessage>(Build("variable", new RoomCopyWired()));

        var sources = Build(
            "action",
            new RoomCopyWired { FurniSourceTypes = [0], UserSourceTypes = [0] }
        );

        Assert.Equal(
            WiredFurniSourceTypeExtensions.FromProtocolId(0),
            Assert.Single(Assert.Single(sources.FurniSources))
        );
        Assert.Single(Assert.Single(sources.PlayerSources));
    }

    [Fact]
    public void An_internal_variable_is_kept_without_a_word()
    {
        var internalId = WiredVariableIdBuilder
            .CreateInternalOrdered(WiredVariableTargetType.User, "@index", default, 1)
            .Value.ToString();
        var built = WiredCopy.Build(
            new PlannedWired(
                "1",
                "action",
                new RoomCopyWired { Ref = "1", VariableIds = [internalId] }
            ),
            new System.Collections.Generic.Dictionary<string, int> { ["1"] = 5 },
            new System.Collections.Generic.Dictionary<ulong, ulong>()
        );

        Assert.Equal([internalId], built.Message.VariableIds);
        Assert.Empty(built.Problems);
    }
}

public class ServiceTests
{
    private static string Json(JsonObject copy) => copy.ToJsonString();

    [Fact]
    public async Task A_preview_writes_no_room_and_keeps_its_dry_run()
    {
        using var host = new TestHost();
        var outcome = await host.Service.PreviewAsync(
            Json(Samples.Copy()),
            " alice ",
            null,
            1,
            TestHost.CT
        );

        Assert.Null(outcome.Error);
        Assert.Empty(host.Writer.Created);

        var row = host.Query(db => db.Imports.AsNoTracking().Single());

        Assert.Equal(
            (RoomImportEntity.PREVIEW, 7, "Ambassador InfoBus Park", "78236752"),
            (row.Status, row.OwnerId, row.RoomName, row.SourceRoomId)
        );

        using var preview = JsonDocument.Parse(row.Preview);

        Assert.Equal(9, preview.RootElement.GetProperty("furniMade").GetInt32());
        Assert.Equal(2, preview.RootElement.GetProperty("furniLeftOut").GetInt32());
        Assert.Equal(
            "mystery_furni (sprite 99999) x2",
            preview.RootElement.GetProperty("unmapped")[0].GetString()
        );
        Assert.Equal(3, preview.RootElement.GetProperty("wiredToSave").GetInt32());
    }

    [Theory]
    [InlineData("not json", "alice", "not room copy v1 JSON")]
    [InlineData("{\"version\":1}", "alice", "no floor plan")]
    [InlineData("VALID", "nobody", "no player called nobody")]
    [InlineData("VALID", "", "Name the player")]
    [InlineData("", "alice", "Paste a room copy")]
    public async Task A_copy_that_cannot_be_imported_is_refused_with_why(
        string document,
        string owner,
        string why
    )
    {
        using var host = new TestHost();
        var outcome = await host.Service.PreviewAsync(
            document == "VALID" ? Json(Samples.Copy()) : document,
            owner,
            null,
            1,
            TestHost.CT
        );

        Assert.Contains(why, outcome.Error);
        Assert.Equal(0, host.Query(db => db.Imports.Count()));
    }

    [Fact]
    public async Task An_import_makes_the_room_once_and_reports_each_wired_box()
    {
        using var host = new TestHost();
        var preview = await host.Service.PreviewAsync(
            Json(Samples.Copy()),
            "alice",
            "Copied park",
            1,
            TestHost.CT
        );

        host.Writer.Refuse = m =>
            m is UpdateTriggerMessage ? "wiredfurni.error.require_keyword" : null;

        var outcome = await host.Service.ImportAsync(preview.Import!.Id, 2, TestHost.CT);

        Assert.Null(outcome.Error);
        Assert.Equal("Copied park", Assert.Single(host.Writer.Created).Room.Name);
        // Variables before the boxes that name them.
        Assert.Equal(
            ["UpdateVariableMessage", "UpdateTriggerMessage", "UpdateActionMessage"],
            host.Writer.Saved.Select(x => x.GetType().Name)
        );

        var row = host.Query(db => db.Imports.AsNoTracking().Single());

        Assert.Equal((RoomImportEntity.IMPORTED, 500, 2), (row.Status, row.RoomId, row.ImportedBy));

        using var report = JsonDocument.Parse(row.Report!);
        var wired = report.RootElement.GetProperty("wired").EnumerateArray().ToList();

        Assert.Equal(9, report.RootElement.GetProperty("furniMade").GetInt32());
        Assert.Equal([true, false, true], wired.Select(x => x.GetProperty("saved").GetBoolean()));
        Assert.Equal("wiredfurni.error.require_keyword", wired[1].GetProperty("error").GetString());

        var again = await host.Service.ImportAsync(preview.Import.Id, 2, TestHost.CT);

        Assert.Contains("only a preview can be imported", again.Error);
        Assert.Single(host.Writer.Created);
    }

    [Fact]
    public async Task A_failed_import_is_marked_failed_and_saves_no_wired()
    {
        using var host = new TestHost();
        var preview = await host.Service.PreviewAsync(
            Json(Samples.Copy()),
            "alice",
            null,
            1,
            TestHost.CT
        );

        host.Writer.FailCreate = new InvalidOperationException(
            "Duplicate entry 'room_x' for key 'name'"
        );

        var outcome = await host.Service.ImportAsync(preview.Import!.Id, 2, TestHost.CT);

        Assert.Contains("made nothing", outcome.Error);
        Assert.Empty(host.Writer.Saved);

        var row = host.Query(db => db.Imports.AsNoTracking().Single());

        Assert.Equal((RoomImportEntity.FAILED, null), (row.Status, row.RoomId));
        Assert.Contains("Duplicate entry", row.Error);
    }
}

public class AdminTests
{
    private sealed class Builder : IPluginAdminBuilder
    {
        public System.Collections.Generic.List<string> Nodes { get; } = [];

        public System.Collections.Generic.List<PluginAdminEndpoint> Endpoints { get; } = [];

        public System.Collections.Generic.List<PluginAdminPage> Pages { get; } = [];

        public string Node(string name, string description)
        {
            Nodes.Add($"plugin.roomcopy.{name}");

            return Nodes[^1];
        }

        public void Map(
            string method,
            string pattern,
            string node,
            Func<
                PluginAdminRequest,
                System.Threading.CancellationToken,
                Task<PluginAdminResult>
            > handler
        ) => Endpoints.Add(new(method, pattern, node, handler));

        public void Page(PluginAdminPage page) => Pages.Add(page);
    }

    private static PluginAdminRequest Request(
        string method,
        string path,
        string? id = null,
        object? body = null
    ) =>
        new(
            method,
            path,
            id is null
                ? new System.Collections.Generic.Dictionary<string, string>()
                : new() { ["id"] = id },
            new System.Collections.Generic.Dictionary<string, string>(),
            body is null ? null : JsonSerializer.SerializeToElement(body),
            1
        );

    [Fact]
    public async Task The_page_previews_lists_shows_and_imports_behind_one_node()
    {
        using var host = new TestHost();
        var admin = new Builder();

        host.Admin.Configure(admin);

        Assert.Equal(["plugin.roomcopy.import"], admin.Nodes);
        Assert.All(admin.Endpoints, x => Assert.Equal("plugin.roomcopy.import", x.Node));
        Assert.Equal(["import", "detail"], admin.Pages.Select(x => x.Id));

        Task<PluginAdminResult> Call(string method, string pattern, PluginAdminRequest request) =>
            admin
                .Endpoints.Single(x => x.Method == method && x.Pattern == pattern)
                .Handler(request, TestHost.CT);

        var previewed = await Call(
            "POST",
            "imports",
            Request(
                "POST",
                "imports",
                body: new
                {
                    document = Samples.Copy().ToJsonString(),
                    owner = "alice",
                    name = "",
                }
            )
        );

        Assert.Equal(200, previewed.Status);

        var id = host.Query(db => db.Imports.Single().Id).ToString();
        var list = JsonSerializer.SerializeToElement(
            (await Call("GET", "imports", Request("GET", "imports"))).Value
        );

        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(
            "9 furni",
            $"{list.GetProperty("items")[0].GetProperty("furni").GetInt32()} furni"
        );
        Assert.Equal("3 to save", list.GetProperty("items")[0].GetProperty("wired").GetString());

        var refused = await Call(
            "POST",
            "imports",
            Request("POST", "imports", body: new { document = "{", owner = "alice" })
        );

        Assert.Equal(400, refused.Status);

        var run = await Call("POST", "imports/{id}/run", Request("POST", $"imports/{id}/run", id));
        var detail = JsonSerializer.SerializeToElement(run.Value);

        Assert.Equal(200, run.Status);
        Assert.Equal("imported", detail.GetProperty("status").GetString());
        Assert.Equal(500, detail.GetProperty("roomId").GetInt32());
        Assert.Contains("variable 30", detail.GetProperty("wiredSaved").GetString());
        Assert.Contains("404 is not in the room", detail.GetProperty("wiredRefused").GetString());

        Assert.Equal(
            409,
            (
                await Call("POST", "imports/{id}/run", Request("POST", $"imports/{id}/run", id))
            ).Status
        );
        Assert.Equal(
            404,
            (await Call("DELETE", "imports/{id}", Request("DELETE", $"imports/{id}", id))).Status
        );

        var report = await Call(
            "GET",
            "imports/{id}/report",
            Request("GET", $"imports/{id}/report", id)
        );

        Assert.Equal("application/json", report.ContentType);
        Assert.Equal(
            500,
            JsonDocument.Parse(report.File!).RootElement.GetProperty("roomId").GetInt32()
        );
    }
}
