using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Turbo.Database.Delegates;
using Turbo.Primitives.Furniture.Enums;
using Turbo.Primitives.Furniture.Providers;
using Turbo.Primitives.Furniture.Snapshots;
using Turbo.Primitives.Messages.Incoming.Userdefinedroomevents;
using TurboRoomCopy.Database;

namespace TurboRoomCopy.Tests;

/// <summary>A hotel's furni definitions, as the server's provider answers for them.</summary>
internal sealed class Definitions : IFurnitureDefinitionProvider
{
    private readonly List<FurnitureDefinitionSnapshot> _all = [];

    public Definitions Add(int id, string name, int sprite, ProductType type = ProductType.Floor)
    {
        _all.Add(
            new FurnitureDefinitionSnapshot
            {
                Id = id,
                SpriteId = sprite,
                Name = name,
                ProductType = type,
                FurniCategory = default,
                LogicName = "default_floor",
                TotalStates = 2,
                Width = 1,
                Length = 1,
                StackHeight = 1,
                CanStack = true,
                CanWalk = false,
                CanSit = false,
                CanLay = false,
                CanRecycle = false,
                CanTrade = true,
                CanGroup = false,
                CanSell = false,
                UsagePolicy = default,
                ExtraData = null,
            }
        );

        return this;
    }

    public FurnitureDefinitionSnapshot? TryGetDefinition(int id) => _all.Find(x => x.Id == id);

    public FurnitureDefinitionSnapshot? TryGetDefinitionByName(string name) =>
        _all.Find(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    public FurnitureDefinitionSnapshot? TryGetDefinitionBySprite(
        ProductType productType,
        int spriteId
    ) => _all.Find(x => x.ProductType == productType && x.SpriteId == spriteId);

    public IReadOnlyList<string> FindNames(string prefix, int limit) =>
        [
            .. _all.Select(x => x.Name)
                .Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Take(limit),
        ];

    public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// The server's side of an import, kept in memory: what it was asked to make, ids from 1000 up,
/// and a save that refuses what <see cref="Refuse"/> says.
/// </summary>
internal sealed class FakeWriter : IRoomCopyWriter
{
    public Dictionary<string, int> Players { get; } =
        new(StringComparer.OrdinalIgnoreCase) { ["alice"] = 7 };

    public List<RoomCopyPlan> Created { get; } = [];

    public List<UpdateWiredMessage> Saved { get; } = [];

    public Func<UpdateWiredMessage, string?> Refuse { get; set; } = _ => null;

    public Exception? FailCreate { get; set; }

    public Task<(int Id, string Name)?> FindPlayerAsync(string name, CancellationToken ct) =>
        Task.FromResult<(int, string)?>(
            Players.TryGetValue(name.Trim(), out var id) ? (id, name.Trim()) : null
        );

    public Task<CreatedRoom> CreateAsync(
        RoomCopyPlan plan,
        int ownerId,
        int actorId,
        int importId,
        CancellationToken ct
    )
    {
        if (FailCreate is not null)
            throw FailCreate;

        Created.Add(plan);

        var ids = plan
            .Furni.Select((x, i) => (x.Ref, Id: 1000 + i))
            .ToDictionary(x => x.Ref, x => x.Id);

        return Task.FromResult(new CreatedRoom(500, 900, ids));
    }

    public Task<WiredSaveOutcome> SaveWiredAsync(
        int roomId,
        int actorId,
        UpdateWiredMessage message,
        CancellationToken ct
    )
    {
        Saved.Add(message);

        var error = Refuse(message);

        return Task.FromResult(new WiredSaveOutcome(error is null, error, null));
    }
}

/// <summary>
/// <c>created_at</c> is the database's to fill, as MySQL does (<c>CURRENT_TIMESTAMP</c>); SQLite
/// is told the same.
/// </summary>
internal sealed class CreatedAtByDatabase(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder
            .Entity<RoomImportEntity>()
            .Property(x => x.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}

/// <summary>The plugin's services over an in-memory SQLite database (real SQL: transactions, bulk updates).</summary>
internal sealed class TestHost : IDisposable
{
    public static readonly CancellationToken CT = CancellationToken.None;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestHost(Definitions? definitions = null)
    {
        _connection.Open();

        var services = new ServiceCollection();

        services.AddSingleton<TablePrefixProvider>(() => "roomcopy_");
        services.AddDbContext<RoomCopyDbContext>(o =>
            o.UseSqlite(_connection).ReplaceService<IModelCustomizer, CreatedAtByDatabase>()
        );
        Services = services.BuildServiceProvider();

        using (var scope = Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<RoomCopyDbContext>().Database.EnsureCreated();

        Planner = new RoomCopyPlanner(definitions ?? Samples.Definitions(), new RoomCopyConfig());
        Service = new RoomCopyService(
            Services.GetRequiredService<IServiceScopeFactory>(),
            Writer,
            Planner,
            TimeProvider.System,
            NullLogger<RoomCopyService>.Instance
        );
        Admin = new RoomCopyAdmin(Services.GetRequiredService<IServiceScopeFactory>(), Service);
    }

    public ServiceProvider Services { get; }

    public FakeWriter Writer { get; } = new();

    public RoomCopyPlanner Planner { get; }

    public RoomCopyService Service { get; }

    public RoomCopyAdmin Admin { get; }

    public T Query<T>(Func<RoomCopyDbContext, T> query)
    {
        using var scope = Services.CreateScope();

        return query(scope.ServiceProvider.GetRequiredService<RoomCopyDbContext>());
    }

    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }
}
