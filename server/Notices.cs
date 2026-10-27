using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Turbo.Primitives.Plugins.Events;
using TurboRoomCopy.Database;

namespace TurboRoomCopy;

/// <summary>
/// Tells other plugins how each import ended with a <see cref="PluginNoticeEvent"/> on the host's
/// event system (turbo-cloud's <c>docs/plugin-notices.md</c>): the Discord plugin posts them as
/// <c>plugin:roomcopy:import.done</c> and <c>plugin:roomcopy:import.failed</c>. Nothing is
/// published until the plugin's start sets <see cref="Publisher"/>, and a publish that throws is
/// logged: the import stands either way. When the host has an admin panel URL
/// (<c>Turbo:Admin:PanelUrl</c>, <paramref name="panelUrl"/>) a notice links to the import's page.
/// </summary>
public sealed class Notices(ILogger<Notices> logger, string? panelUrl = null)
{
    public const string SOURCE = "roomcopy";
    public const string IMPORT_DONE = "import.done";
    public const string IMPORT_FAILED = "import.failed";

    /// <summary>The host's <c>IEventPublisher.PublishAsync</c> while the plugin runs; null: nothing is published.</summary>
    public Func<PluginNoticeEvent, CancellationToken, Task>? Publisher { get; set; }

    /// <summary>An import that made its room, with how many furni it made and wired boxes it saved.</summary>
    public Task DoneAsync(
        RoomImportEntity import,
        int furniMade,
        int wiredSaved,
        int wiredBoxes,
        CancellationToken ct
    ) =>
        PublishAsync(
            Import(
                import,
                IMPORT_DONE,
                $"Room copy imported: {import.RoomName}",
                $"Room {import.RoomId?.ToString(CultureInfo.InvariantCulture) ?? "-"} for {import.OwnerName}.",
                furniMade.ToString(CultureInfo.InvariantCulture),
                $"{wiredSaved}/{wiredBoxes}",
                PluginNoticeLevel.Success
            ),
            ct
        );

    /// <summary>An import that made nothing, with why.</summary>
    public Task FailedAsync(RoomImportEntity import, CancellationToken ct) =>
        PublishAsync(
            Import(
                import,
                IMPORT_FAILED,
                $"Room copy import failed: {import.RoomName}",
                import.Error,
                "0",
                "0",
                PluginNoticeLevel.Error
            ),
            ct
        );

    /// <summary>Publishes <paramref name="notice"/> cut to the event's limits; never throws.</summary>
    public async Task PublishAsync(PluginNoticeEvent notice, CancellationToken ct)
    {
        if (Publisher is not { } publish)
            return;

        try
        {
            await publish(Fit(notice), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Plugin notice {Topic} {Key} could not be published",
                notice.Topic,
                notice.Key
            );
        }
    }

    private PluginNoticeEvent Import(
        RoomImportEntity import,
        string topic,
        string title,
        string? text,
        string furniMade,
        string wiredSaved,
        PluginNoticeLevel level
    )
    {
        var id = import.Id.ToString(CultureInfo.InvariantCulture);
        var panel = panelUrl?.TrimEnd('/');
        var source = string.Join(
            ' ',
            new[] { import.SourceHotel, import.SourceRoomId }.Where(x => !string.IsNullOrEmpty(x))
        );

        return new PluginNoticeEvent
        {
            Source = SOURCE,
            Topic = topic,
            Title = title,
            Text = text,
            Url = string.IsNullOrEmpty(panel) ? null : $"{panel}/plugin/roomcopy/detail/{id}",
            Fields =
            [
                new("Source room", source.Length > 0 ? source : "-"),
                new("Owner", import.OwnerName),
                new("Furni made", furniMade),
                new("Wired saved", wiredSaved),
            ],
            Level = level,
            Key = id,
        };
    }

    /// <summary>The notice within <see cref="PluginNoticeEvent"/>'s limits.</summary>
    internal static PluginNoticeEvent Fit(PluginNoticeEvent notice) =>
        notice with
        {
            Topic = Cut(notice.Topic, PluginNoticeEvent.MAX_TOPIC_LENGTH),
            Title = Cut(notice.Title, PluginNoticeEvent.MAX_TITLE_LENGTH),
            Text = notice.Text is null ? null : Cut(notice.Text, PluginNoticeEvent.MAX_TEXT_LENGTH),
            Fields = notice.Fields.IsDefaultOrEmpty
                ? []
                : [.. notice.Fields.Take(PluginNoticeEvent.MAX_FIELDS)],
        };

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
