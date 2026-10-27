using System.Globalization;
using System.Text.Json.Nodes;
using Turbo.Primitives.Furniture.Enums;
using Turbo.Primitives.Rooms.Wired.Variable;

namespace TurboRoomCopy.Tests;

/// <summary>A small room copy in the shape of the InfoBus park copy (habbo.com room 78236752), and the furni a hotel has for it.</summary>
internal static class Samples
{
    public const string VARIABLE_BOX = "30";
    public const string ACTION_BOX = "31";
    public const string TRIGGER_BOX = "32";

    /// <summary>A box the copy has no config for, as every box of a room Habbo won't let us read.</summary>
    public const string UNCONFIGURED_BOX = "33";

    /// <summary>The id Habbo gives the variable a box makes, as its copy names it.</summary>
    public static string VariableOf(string boxRef) =>
        WiredVariableId
            .OfBox(int.Parse(boxRef, CultureInfo.InvariantCulture))
            .Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A database (box) variable of no box in the copy.</summary>
    public const string UNKNOWN_VARIABLE = "562949953421313";

    public static Definitions Definitions() =>
        new Definitions()
            .Add(1, "room_info15_div", 8088)
            .Add(2, "garden_flo1", 3418)
            .Add(3, "room_info15_roller", 8154)
            .Add(4, "ads_background", 3996)
            .Add(5, "room_info15_bg", 8020)
            .Add(6, "table_norja", 777)
            .Add(7, "chair_norja", 778)
            .Add(8, "poster", 4001, ProductType.Wall)
            .Add(9, "wf_var_furni", 5000, logic: "wf_var_furni")
            .Add(10, "wf_act_match_to_sshot", 3700, logic: "wf_act_match_to_sshot")
            .Add(11, "wf_trg_says_something", 3675, logic: "wf_trg_says_something");

    public static JsonObject Copy() =>
        JsonNode
            .Parse(
                $$"""
                {
                  "version": 1,
                  "source": { "hotel": "habbo.com", "roomId": "78236752", "build": "WIN63-202610201032-107964540", "capturedAt": "2026-10-21T08:37:21.592Z" },
                  "room": {
                    "name": "Ambassador InfoBus Park", "description": "", "maxUsers": 50, "doorMode": 0, "tradeMode": 0,
                    "allowPets": false, "allowPetsEat": false, "hideOwner": true, "hideWalls": true,
                    "wallThickness": -2, "floorThickness": -2, "wallHeight": -1,
                    "paint": { "wall": null, "floor": null, "landscape": "0.0" },
                    "chat": { "flow": 0, "bubbleWidth": 1, "scrollSpeed": 1, "talkHearingDistance": 14, "flood": 1 },
                    "moderation": { "mute": 0, "kick": 0, "ban": 0 },
                    "tags": []
                  },
                  "model": { "heightmap": "xxxxx\rx000x\rx000x\rxx0xx", "doorX": 2, "doorY": 3, "doorDir": 0 },
                  "floorItems": [
                    { "ref": "10", "sprite": 8088, "classname": "room_info15_div", "x": 1, "y": 1, "z": 0, "dir": 2, "stuff": { "type": "legacy", "value": "" } },
                    { "ref": "11", "sprite": 3418, "classname": "garden_flo1", "x": 2, "y": 1, "z": 0.5, "dir": 0, "stuff": { "type": "legacy", "value": "0" } },
                    { "ref": "12", "sprite": 3996, "classname": "ads_background", "x": 1, "y": 2, "z": 0.000002, "dir": 1, "stuff": { "type": "map", "value": { "state": "0", "offsetX": "319", "imageUrl": "https://images.habbo.com/c_images/album3831/infoa15_d.png" } } },
                    { "ref": "13", "sprite": 8020, "classname": "room_info15_bg", "x": 1, "y": 2, "z": 0, "dir": 2, "stuff": { "type": "number", "value": [1, 134, 206, 159] } },
                    { "ref": "14", "sprite": 99999, "classname": "mystery_furni", "x": 3, "y": 1, "z": 0, "dir": 0, "stuff": { "type": "legacy", "value": "" } },
                    { "ref": "15", "sprite": 99999, "classname": "mystery_furni", "x": 3, "y": 2, "z": 0, "dir": 0 },
                    { "ref": "16", "sprite": 777, "classname": "chair_norja", "x": 2, "y": 2, "z": 1.25, "dir": 4 },
                    { "ref": "{{VARIABLE_BOX}}", "sprite": 5000, "classname": "wf_var_furni", "x": 3, "y": 2, "z": 0, "dir": 0 },
                    { "ref": "{{ACTION_BOX}}", "sprite": 3700, "classname": "wf_act_match_to_sshot", "x": 3, "y": 2, "z": 0.65, "dir": 0 },
                    { "ref": "{{TRIGGER_BOX}}", "sprite": 3675, "classname": "wf_trg_says_something", "x": 3, "y": 2, "z": 1.3, "dir": 0 },
                    { "ref": "{{UNCONFIGURED_BOX}}", "sprite": 3675, "classname": "wf_trg_says_something", "x": 1, "y": 1, "z": 0, "dir": 0, "stuff": { "type": "legacy", "value": "0" } }
                  ],
                  "wallItems": [
                    { "ref": "20", "sprite": 4001, "classname": "poster", "wallPosition": ":w=3,2 l=10,40 r", "stuff": { "type": "legacy", "value": "12" } }
                  ],
                  "wired": [
                    { "ref": "{{ACTION_BOX}}", "kind": "action", "intParams": [1, 1, 1, 0], "stringParam": "", "stuffIds": ["10", "11", "404"], "stuffIds2": [],
                      "furniSourceTypes": [], "userSourceTypes": [], "variableIds": ["{{VariableOf(
                    VARIABLE_BOX
                )}}", "{{UNKNOWN_VARIABLE}}"], "delayInPulses": 3 },
                    { "ref": "{{TRIGGER_BOX}}", "kind": "trigger", "intParams": [0, 0, 1], "stringParam": "bus" },
                    { "ref": "{{VARIABLE_BOX}}", "kind": "variable", "intParams": [], "stringParam": "score" }
                  ]
                }
                """
            )!
            .AsObject();
}
