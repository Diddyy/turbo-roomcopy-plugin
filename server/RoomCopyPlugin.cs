using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Turbo.Contracts.Plugins;
using Turbo.Contracts.Plugins.Admin;
using Turbo.Contracts.Plugins.Exports;
using Turbo.Database.Context;
using Turbo.Database.Extensions;
using Turbo.Database.Furniture;
using Turbo.Primitives.Events;
using Turbo.Primitives.Furniture.Providers;
using Turbo.Primitives.Rooms.Providers;
using TurboRoomCopy.Database;

namespace TurboRoomCopy;

/// <summary>
/// Room import: a staff member pastes a copy of a Habbo room ("room copy v1", README) into the
/// admin panel, previews it and imports it as a new room for a player, with its floor plan,
/// settings, furni and wired. Previews and imports are kept in <c>roomcopy_imports</c>.
/// </summary>
public sealed class RoomCopyPlugin : ITurboPlugin
{
    private Notices? _notices;

    public string Key => "roomcopy";

    public string Version => "1.1.0";

    public void ConfigureServices(IServiceCollection services, PluginManifest manifest)
    {
        services.AddPluginDatabaseContext<RoomCopyDbContext, RoomCopyDbModule>();
        services.AddSingleton(sp =>
            sp.GetRequiredService<IHostServices>()
                .GetRequiredService<IConfiguration>()
                .GetSection(RoomCopyConfig.SECTION_NAME)
                .Get<RoomCopyConfig>()
            ?? new RoomCopyConfig()
        );
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new RoomCopyPlanner(
            Host<IFurnitureDefinitionProvider>(sp),
            sp.GetRequiredService<RoomCopyConfig>()
        ));
        services.AddSingleton<IRoomCopyWriter>(sp => new TurboRoomCopyWriter(
            Host<IDbContextFactory<TurboDbContext>>(sp),
            Host<IRoomFurniWriter>(sp),
            Host<IRoomModelProvider>(sp),
            Host<IGrainFactory>(sp),
            sp.GetRequiredService<ILogger<TurboRoomCopyWriter>>()
        ));
        services.AddSingleton(sp => new Notices(
            sp.GetRequiredService<ILogger<Notices>>(),
            Host<IConfiguration>(sp)["Turbo:Admin:PanelUrl"]
        ));
        services.AddSingleton<RoomCopyService>();
        services.AddSingleton<IPluginAdmin, RoomCopyAdmin>();
    }

    private static T Host<T>(IServiceProvider sp)
        where T : notnull => sp.GetRequiredService<IHostServices>().GetRequiredService<T>();

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        _notices = services.GetRequiredService<Notices>();
        _notices.Publisher = Host<IEventPublisher>(services).PublishAsync;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        if (_notices is not null)
            _notices.Publisher = null;

        _notices = null;

        return Task.CompletedTask;
    }

    public Task BindExportsAsync(IExportBinder binder, IServiceProvider services) =>
        Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
