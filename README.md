# Turbo Room Copy plugin

**Room import** for a [Turbo](https://github.com/nitrodevco/turbo-cloud) hotel's admin panel: paste
a copy of a Habbo room, choose the player who will own it, preview what it would make, then import
it as a new room with its floor plan, settings, furni and wired.

- **Preview** reads the copy and plans the room without writing one: the floor plan, the
  settings, which furni the hotel has (matched by sprite and type, then by class name) and which it
  does not, the stuff data kinds and the wired boxes, with a warning for everything not copied as
  it was. Open the preview to read it.
- **Import** makes the room: the floor plan, the room row and every furni in **one transaction**,
  so a failed import leaves nothing behind. Then each wired box is saved through the room's own
  save (the check the wired editor's save gets), and the report says which saved and why any was
  refused.

Previews and imports are kept in `roomcopy_imports`, with the copy itself, so an import runs on
what was previewed. The report of each can be downloaded as JSON from its page.

How each import ended is told to other plugins as a plugin notice: with the
[Turbo Discord plugin](https://github.com/Diddyy/turbo-discord-plugin) loaded it is posted to
Discord like any other hotel event (Notices, below).

## How a room is made

The importer writes rooms the way the server does:

- **Floor plan**: a custom model row (`room_models.custom = 1`) named `room_<room id>`, the name
  the floor plan editor gives a room's own plan, so a later edit in the editor changes that row.
  The plan keeps to the editor's rules (`x` or a height `0-9a-z` a tile, no side past
  `MaxAxisLength`, the door on an open tile) but not to its area limit: a hotel's public rooms are
  larger than a player may draw. Once committed, the running server is told through
  `IRoomModelProvider.ReloadModelAsync`, as the editor's save does: no restart.
- **Room**: every setting of the copy (below), on the navigator's defaults for the rest. The
  navigator's listings for the owner and search are refreshed as when a room is made.
- **Furni**: through the server's furni custody, with a movement per furni (cause
  `plugin.roomcopy`, the import's id as its ref). Places are kept exactly: `x`, `y`, `z` (the
  server keeps 3 decimals), the direction, and a wall item's wall position. Stuff data is written in
  the server's extra data shape, `{"stuff":{"Data":...,"UniqueNumber":0,"UniqueSeries":0}}`: a
  string (legacy, an empty one too), a string map, or a list of whole numbers. A furni's definition
  decides how its data is read: map data shows only on a definition whose logic keeps map data
  (Turbo's `room_branding` for `ads_*`, `background_toner` for toners).
- **Wired**: after the commit, each box is saved through the room grain (`ApplyWiredUpdateAsync`)
  as the staff member importing, the way their own save in the room would be checked. Boxes are
  saved variables first, then add-ons, selectors, triggers, conditions and actions. The furni a box
  picks are turned from the copy's refs into the new ids; a ref that is not in the room is dropped
  and reported. A variable a box of the copy makes is renamed after the new box (Habbo names it
  after its box, `WiredVariableIdBuilder.CreateFromBoxId`); an internal variable is the same in
  every hotel and is kept; any other variable is reported as unknown and passed on as it was,
  never made up. A box the room refuses is reported with its error; the room stays.

## Room copy v1

A room copy is one JSON document:

```jsonc
{
  "version": 1,
  "source": { "hotel": "habbo.com", "roomId": "78236752", "build": "WIN63-...", "capturedAt": "2026-10-21T08:37:21.592Z" },
  "room": {
    "name": "Ambassador InfoBus Park", "description": "", "maxUsers": 50,
    "doorMode": 0,          // 0 open, 1 locked (doorbell), 2 password (made locked: a copy has no password), 3 invisible
    "tradeMode": 0,         // 0 off, 1 owner and rights, 2 everyone
    "allowPets": false, "allowPetsEat": false, "hideOwner": true, "hideWalls": true,
    "wallThickness": -2, "floorThickness": -2,   // -2..1, as RoomVisualizationSettings sends them
    "wallHeight": -1,
    "paint": { "wall": null, "floor": null, "landscape": "0.0" },
    "chat": { "flood": 1 },  // 0 strict, 1 normal, 2 loose; flow, bubbleWidth, scrollSpeed and talkHearingDistance are read and not kept
    "moderation": { "mute": 0, "kick": 0, "ban": 0 },   // 0 owner, 1 rights, 2 all, 4 group rights, 5 rights or group
    "tags": []
  },
  "model": { "heightmap": "xxxx\rx00x\rx00x", "doorX": 1, "doorY": 2, "doorDir": 0 },   // FloorHeightMap rows joined by "\r"
  "floorItems": [
    { "ref": "770018431", "sprite": 8088, "classname": "room_info15_div", "x": 8, "y": 16, "z": 0, "dir": 2,
      "stuff": { "type": "legacy", "value": "" } },
    { "ref": "770018467", "sprite": 3996, "classname": "ads_background", "x": 1, "y": 9, "z": 0, "dir": 1,
      "stuff": { "type": "map", "value": { "state": "0", "offsetX": "319", "offsetY": "695", "offsetZ": "8700", "imageUrl": "https://..." } } },
    { "ref": "770018472", "sprite": 8020, "classname": "room_info15_bg", "x": 1, "y": 9, "z": 0, "dir": 2,
      "stuff": { "type": "number", "value": [1, 134, 206, 159] } }
  ],
  "wallItems": [
    { "ref": "770019000", "sprite": 4001, "classname": "poster", "wallPosition": ":w=3,2 l=10,40 r", "stuff": { "type": "legacy", "value": "12" } }
  ],
  "wired": [
    { "ref": "770018452", "kind": "action",   // trigger, action, condition, selector, addon or variable
      "intParams": [1, 1, 1, 0], "stringParam": "",
      "stuffIds": ["770018432"], "stuffIds2": [],         // refs of floorItems
      "furniSourceTypes": [], "userSourceTypes": [],      // protocol source ids, as the editor sends them
      "variableIds": [],                                  // Habbo's variable ids (decimal strings)
      "delayInPulses": 0,                                 // action
      "quantifierCode": 0,                                // condition
      "isFilter": false, "isInvert": false }              // selector
  ]
}
```

- `ref` is the item's id in the source room; it only has to be unique within the copy. Refs and
  ids may be strings or numbers.
- `dir` is 0-7. `z` is the height as the client receives it.
- A furni is matched by `sprite` with its type (floor or wall) first, then by `classname`; when the
  sprite is a different furni in this hotel and the class name is one, the class name wins (and
  the preview says so). Furni the hotel has neither for are left out and counted by kind.
- `stuff` may be left out: the furni starts as a new one.

## Settings

Under `Turbo:RoomCopy` in the server's configuration (`Turbo__RoomCopy__MaxItems=...`):

| Setting | Default | |
| --- | --- | --- |
| `MaxAxisLength` | 64 | The longest side of a floor plan (the editor's `Turbo:Rooms:FloorPlanMaxAxisLength`). |
| `NameMaxLength` | 60 | A longer room name is cut (the navigator's `RoomNameMaxLength`). |
| `DescriptionMaxLength` | 255 | A longer description is cut. |
| `MaxItems` | 5000 | The most furni one import makes. |
| `MaxUsersLimit` | 200 | The most users a room is made for; the copy's own number up to it. |

The panel sends a plugin at most `Turbo:Admin:PluginMaxBodyBytes` (1 MB) and waits
`Turbo:Admin:PluginTimeoutSeconds` (30 s): a copy of a few hundred furni is about 50 KB and imports
in under a second.

## Notices (Discord)

From version 1.1.0 the plugin publishes a `PluginNoticeEvent` (turbo-cloud's
`docs/plugin-notices.md`, turbo-cloud #296 or later) on the hotel's event system when an import
ends, keyed by the import id:

| Topic | When | Level |
| --- | --- | --- |
| `import.done` | The room is made (whatever became of its wired boxes). | Success |
| `import.failed` | The import failed and made nothing; the text says why. | Error |

Each carries the fields Source room (the copy's hotel and room id), Owner, Furni made and Wired
saved (saved/boxes), and a link to the import's page (`/plugin/roomcopy/detail/<id>`) when the
server has `Turbo:Admin:PanelUrl`. The Discord plugin lists `plugin:roomcopy:import.done` and
`plugin:roomcopy:import.failed` in its routing table, off until the hotel turns them on and gives
them a webhook. With no relay loaded a notice goes nowhere, and a notice that cannot be published
is logged: the import stands either way.

## Permission

`plugin.roomcopy.import`: the Room import page, previewing, importing and discarding. No one holds
it until a hotel grants it. Every call but a GET is in the command log as
`plugin call roomcopy <method> <path> <status>`, refusals included.

## Build and install

The plugin builds against a built turbo-cloud checkout (its `Turbo.*` assemblies are the host's and
are never copied into the plugin's folder). Besides the plugin contracts it uses the server's furni
custody (`Turbo.Furniture`), wired variable ids (`Turbo.Rooms`) and the event system its notices are
published on (`Turbo.Events`, with `PluginNoticeEvent`: turbo-cloud #296 or later):

```sh
TURBO_CLOUD=/path/to/turbo-cloud ./scripts/package.sh
```

That leaves `dist/roomcopy/` with `TurboRoomCopy.dll` and `manifest.json`. Copy that folder into
the server's plugin folder (`Turbo:Plugin:PluginFolderPath`, by default `plugins/` beside
`Turbo.Main`), as `plugins/roomcopy/`, and restart the server (or let hot reload pick it up). On
load the plugin creates its table (`roomcopy_imports`) through the server's own migrator.

### Releases

A tag `v1.2.3` (pushed, or named in a manual run of Release, which makes it on that commit) runs
`.github/workflows/release.yml`: it builds and tests the plugin against turbo-cloud at the ref
pinned there (`TURBO_CLOUD_REF`), stamps the manifest's `Version` and
`BuiltAgainst`, and attaches `roomcopy-v1.2.3.zip` to the tag's release, which a server linked to
this repository in the admin panel installs. `ApiVersion` in the manifest is the turbo-cloud plugin
API it is built for; move `TURBO_CLOUD_REF` and it together.

## Tests

```sh
dotnet test TurboRoomCopy.Tests -p:TurboCloudPath=/path/to/turbo-cloud
dotnet tool restore && dotnet csharpier check .
```

`.github/workflows/ci.yml` runs both on every push and pull request against turbo-cloud's `main`.

## Migrations

```sh
TurboCloudPath=/path/to/turbo-cloud DesignTime=true dotnet ef migrations add <Name> \
  --project TurboRoomCopy/TurboRoomCopy.csproj --context RoomCopyDbContext --output-dir Database/Migrations
```
