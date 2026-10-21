using System;
using System.Threading;
using System.Threading.Tasks;
using Turbo.Contracts.Plugins;
using Turbo.Database.Migrations;

namespace TurboRoomCopy.Database;

public class RoomCopyDbModule : IPluginDbModule
{
    public Task MigrateAsync(IServiceProvider sp, CancellationToken ct) =>
        MigrationHelper.MigrateAsync<RoomCopyDbContext>(sp, ct);

    public Task UninstallAsync(IServiceProvider sp, CancellationToken ct) =>
        MigrationHelper.UninstallAsync<RoomCopyDbContext>(sp, ct);
}
