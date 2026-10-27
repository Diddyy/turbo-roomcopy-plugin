using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Turbo.Primitives.Messages.Incoming.Userdefinedroomevents;
using Turbo.Primitives.Rooms.Enums.Wired;
using Turbo.Primitives.Rooms.Wired.Variable;

namespace TurboRoomCopy;

/// <summary>
/// A copied wired box as the editor's save would send it for the new room: the furni it picks
/// and the variables it names turned from the copy's refs into the new ids.
///
/// A variable a box makes is named after its box (<see cref="WiredVariableId.OfBox(int)"/>,
/// the same hash Habbo uses), so a variable of a box in the copy is found by hashing the copy's
/// refs and is renamed after the new box. An internal variable (a furni's or a user's own) is the
/// same in every hotel and is kept. Any other is reported and passed on as it was: the save
/// decides what it makes of it, and none is made up here.
/// </summary>
internal static class WiredCopy
{
    /// <summary>The derived variables a box may have (a leaderboard's columns, say), looked for by index.</summary>
    private const int SUB_VARIABLES = 32;

    public sealed record Built(UpdateWiredMessage Message, List<string> Problems);

    /// <summary>Copied variable ids by the new ones, for every variable box of the copy that was placed.</summary>
    public static Dictionary<ulong, ulong> VariableIds(
        IEnumerable<PlannedWired> boxes,
        IReadOnlyDictionary<string, int> newIds
    )
    {
        var map = new Dictionary<ulong, ulong>();

        foreach (var box in boxes)
        {
            if (
                !int.TryParse(
                    box.Ref,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var oldId
                ) || !newIds.TryGetValue(box.Ref, out var newId)
            )
                continue;

            map[WiredVariableId.OfBox(oldId).Value] = WiredVariableId.OfBox(newId).Value;

            for (var i = 0; i < SUB_VARIABLES; i++)
                map[WiredVariableId.OfBox(oldId, i).Value] = WiredVariableId.OfBox(newId, i).Value;
        }

        return map;
    }

    public static Built Build(
        PlannedWired box,
        IReadOnlyDictionary<string, int> newIds,
        IReadOnlyDictionary<ulong, ulong> variables
    )
    {
        var source = box.Source;
        var problems = new List<string>();

        List<int> Pick(List<string> refs, string what)
        {
            var ids = new List<int>();

            foreach (var item in refs)
                if (newIds.TryGetValue(item, out var id))
                    ids.Add(id);
                else
                    problems.Add($"{what} {item} is not in the room");

            return ids;
        }

        var variableIds = new List<string>();

        foreach (var text in source.VariableIds)
        {
            if (!WiredVariableId.TryParse(text, out var id))
            {
                problems.Add($"variable '{text}' is not a variable id");
                variableIds.Add(text);

                continue;
            }

            if (variables.TryGetValue(id.Value, out var renamed))
            {
                variableIds.Add(renamed.ToString(CultureInfo.InvariantCulture));

                continue;
            }

            // An id made from a box (the database source) names a box of the source room that is
            // not in the copy; an internal one is the hotel's own and means the same here.
            if (((id.Value >> 48) & 0b1_1111) == (ulong)WiredVariableIdSourceType.Database)
                problems.Add($"variable {text} is unknown (its box is not in the copy)");

            variableIds.Add(text);
        }

        // Made as each kind's parser makes it (UpdateWiredDataParser): the kind's own message
        // type, its fields set after.
        var type = box.Kind switch
        {
            RoomCopyPlanner.KIND_TRIGGER => typeof(UpdateTriggerMessage),
            RoomCopyPlanner.KIND_ACTION => typeof(UpdateActionMessage),
            RoomCopyPlanner.KIND_CONDITION => typeof(UpdateConditionMessage),
            RoomCopyPlanner.KIND_SELECTOR => typeof(UpdateSelectorMessage),
            RoomCopyPlanner.KIND_ADDON => typeof(UpdateAddonMessage),
            _ => typeof(UpdateVariableMessage),
        };

        var message = (UpdateWiredMessage)Activator.CreateInstance(type)! with
        {
            Id = newIds[box.Ref],
            IntParams = [.. source.IntParams],
            StringParam = source.StringParam ?? string.Empty,
            StuffIds = Pick(source.StuffIds, "furni"),
            StuffIds2 = Pick(source.StuffIds2, "second furni"),
            // What each kind's parser reads after the furni: an action's delay, a condition's
            // quantifier, a selector's filter and invert flags.
            DefinitionSpecifics = box.Kind switch
            {
                RoomCopyPlanner.KIND_ACTION => [source.DelayInPulses],
                RoomCopyPlanner.KIND_CONDITION => [source.QuantifierCode],
                RoomCopyPlanner.KIND_SELECTOR => [source.IsFilter, source.IsInvert],
                _ => [],
            },
            FurniSources =
            [
                .. source.FurniSourceTypes.Select(x =>
                    new[] { WiredFurniSourceTypeExtensions.FromProtocolId((WiredSourceType)x) }
                ),
            ],
            PlayerSources =
            [
                .. source.UserSourceTypes.Select(x =>
                    new[] { WiredPlayerSourceTypeExtensions.FromProtocolId((WiredSourceType)x) }
                ),
            ],
            VariableIds = variableIds,
            TypeSpecifics = [],
        };

        return new Built(message, problems);
    }
}
