using Microsoft.EntityFrameworkCore;
using Turbo.Database.Context;
using Turbo.Database.Delegates;

namespace TurboRoomCopy.Database;

public class RoomCopyDbContext(
    DbContextOptions<RoomCopyDbContext> options,
    TablePrefixProvider prefix
) : PluginDbContextBase<RoomCopyDbContext>(options, prefix)
{
    public DbSet<RoomImportEntity> Imports => Set<RoomImportEntity>();
}
