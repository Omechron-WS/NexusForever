using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Map.Search;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Configuration.Model;
using NexusForever.GameTable.Model;
using NexusForever.IO.Map;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Model;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.Entity
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class WorldEntityZoneLifecycleCollection
    {
        public const string Name = "World entity zone lifecycle";
    }

    [Collection(WorldEntityZoneLifecycleCollection.Name)]
    public sealed class WorldEntityZoneLifecycleTests : IDisposable
    {
        private static readonly Vector3 ZoneAPosition = Vector3.Zero;
        private static readonly Vector3 ZoneBPosition = new(32f, 0f, 0f);

        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public WorldEntityZoneLifecycleTests()
        {
            previousProvider = LegacyServiceProvider.Provider;

            var gameTableManager = new GameTableManager(Options.Create(new GameTableConfig()));
            SetGameTable(
                gameTableManager,
                nameof(GameTableManager.WorldZone),
                CreateGameTable(
                    new WorldZoneEntry { Id = 42u },
                    new WorldZoneEntry { Id = 43u }));

            serviceProvider = new ServiceCollection()
                .AddSingleton(gameTableManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        [Fact]
        public void Relocate_SameMapAndZoneDoesNotPublishDuplicateTransition()
        {
            TestWorldEntity entity = CreateEntity(out List<uint> scriptZones);
            Mock<IBaseMap> map = CreateMap(1u, CreateMapFile((0u, 42u), (1u, 42u)));

            entity.OnAddToMap(map.Object, 1u, ZoneAPosition);
            entity.OnRelocate(ZoneBPosition);

            Assert.Equal([42u], entity.HookZones);
            Assert.Equal([42u], scriptZones);
            Assert.Equal(42u, entity.Zone.Id);
        }

        [Fact]
        public void Relocate_MissingWorldZoneRowPreservesCurrentTransition()
        {
            TestWorldEntity entity = CreateEntity(out List<uint> scriptZones);
            Mock<IBaseMap> map = CreateMap(1u, CreateMapFile((0u, 42u), (1u, 99u)));

            entity.OnAddToMap(map.Object, 1u, ZoneAPosition);
            WorldZoneEntry committedZone = entity.Zone;
            entity.OnRelocate(ZoneBPosition);

            Assert.Same(committedZone, entity.Zone);
            Assert.Equal([42u], entity.HookZones);
            Assert.Equal([42u], scriptZones);
        }

        [Fact]
        public void AddToMap_DifferentMapWithSameZonePublishesNewTransition()
        {
            TestWorldEntity entity = CreateEntity(out List<uint> scriptZones);
            Mock<IBaseMap> firstMap = CreateMap(1u, CreateMapFile((0u, 42u)));
            Mock<IBaseMap> secondMap = CreateMap(2u, CreateMapFile((0u, 42u)));

            entity.OnAddToMap(firstMap.Object, 1u, ZoneAPosition);
            entity.OnRemoveFromMap();
            entity.OnAddToMap(secondMap.Object, 2u, ZoneAPosition);

            Assert.Equal([42u, 42u], entity.HookZones);
            Assert.Equal([42u, 42u], scriptZones);
            Assert.Same(secondMap.Object, entity.Map);
            Assert.Equal(42u, entity.Zone.Id);
        }

        [Fact]
        public void RemoveFromMap_ClearsCommittedZone()
        {
            TestWorldEntity entity = CreateEntity(out _);
            Mock<IBaseMap> map = CreateMap(1u, CreateMapFile((0u, 42u)));

            entity.OnAddToMap(map.Object, 1u, ZoneAPosition);
            entity.OnRemoveFromMap();

            Assert.Null(entity.Zone);
            Assert.False(entity.InWorld);
        }

        [Fact]
        public void AddToMap_ThrowingZoneHookDoesNotSuppressScriptNotification()
        {
            TestWorldEntity entity = CreateEntity(out List<uint> scriptZones);
            entity.ThrowDuringZoneHook = true;
            Mock<IBaseMap> map = CreateMap(1u, CreateMapFile((0u, 42u)));

            entity.OnAddToMap(map.Object, 1u, ZoneAPosition);

            Assert.Equal([42u], entity.HookZones);
            Assert.Equal([42u], scriptZones);
            Assert.Equal(42u, entity.Zone.Id);
        }

        [Fact]
        public void ScriptReentrancy_ZoneAToBToASuppressesStaleOuterObservers()
        {
            var firstScriptZones = new List<uint>();
            var secondScriptZones = new List<uint>();
            var firstScript = new Mock<IWorldEntityScript>();
            var secondScript = new Mock<IWorldEntityScript>();
            var scripts = new Mock<IScriptCollection>();
            TestWorldEntity entity = CreateEntity(scripts.Object);
            Mock<IBaseMap> map = CreateMap(1u, CreateMapFile((0u, 42u), (1u, 43u)));
            bool reentered = false;

            firstScript
                .Setup(script => script.OnEnterZone(entity, It.IsAny<uint>()))
                .Callback((IWorldEntity _, uint zoneId) =>
                {
                    firstScriptZones.Add(zoneId);
                    if (reentered || zoneId != 42u)
                        return;

                    reentered = true;
                    entity.OnRelocate(ZoneBPosition);
                    entity.OnRelocate(ZoneAPosition);
                });
            secondScript
                .Setup(script => script.OnEnterZone(entity, It.IsAny<uint>()))
                .Callback((IWorldEntity _, uint zoneId) => secondScriptZones.Add(zoneId));
            scripts
                .Setup(collection => collection.Invoke<IWorldEntityScript>(
                    It.IsAny<Action<IWorldEntityScript>>()))
                .Callback((Action<IWorldEntityScript> callback) =>
                {
                    callback(firstScript.Object);
                    callback(secondScript.Object);
                });

            entity.OnAddToMap(map.Object, 1u, ZoneAPosition);

            Assert.Equal([42u, 43u, 42u], entity.HookZones);
            Assert.Equal([42u, 43u, 42u], firstScriptZones);
            Assert.Equal([43u, 42u], secondScriptZones);
            Assert.Equal(42u, entity.Zone.Id);
        }

        private static TestWorldEntity CreateEntity(out List<uint> scriptZones)
        {
            scriptZones = [];
            List<uint> capturedZones = scriptZones;
            var script = new Mock<IWorldEntityScript>();
            script
                .Setup(value => value.OnEnterZone(It.IsAny<IWorldEntity>(), It.IsAny<uint>()))
                .Callback((IWorldEntity _, uint zoneId) => capturedZones.Add(zoneId));

            var scripts = new Mock<IScriptCollection>();
            scripts
                .Setup(collection => collection.Invoke<IWorldEntityScript>(
                    It.IsAny<Action<IWorldEntityScript>>()))
                .Callback((Action<IWorldEntityScript> callback) => callback(script.Object));
            return CreateEntity(scripts.Object);
        }

        private static TestWorldEntity CreateEntity(IScriptCollection scripts)
        {
            var entity = new TestWorldEntity(new Mock<IMovementManager>().Object);
            entity.SetScriptCollection(scripts);
            return entity;
        }

        private static Mock<IBaseMap> CreateMap(uint worldId, MapFile file)
        {
            var map = new Mock<IBaseMap>();
            map.SetupGet(value => value.Entry).Returns(new WorldEntry { Id = worldId });
            map.SetupGet(value => value.File).Returns(file);
            map.SetupGet(value => value.VisionRange).Returns(100f);
            map
                .Setup(value => value.Search<IGridEntity>(
                    It.IsAny<Vector3>(),
                    It.IsAny<float?>(),
                    It.IsAny<ISearchCheck<IGridEntity>>()))
                .Returns([]);

            List<IMapGrid> grids = [];
            map.Setup(value => value.GridSearch(
                It.IsAny<Vector3>(),
                It.IsAny<float?>(),
                out grids));
            return map;
        }

        private static MapFile CreateMapFile(params (uint CellX, uint ZoneId)[] cells)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(0x504D464Eu);
                writer.Write(4u);
                writer.Write(16_042u);
                writer.Write("zone-lifecycle-test");
                writer.Write(1u);
                writer.Write(64u);
                writer.Write(64u);
                writer.Write((uint)cells.Length);

                foreach ((uint cellX, uint zoneId) in cells)
                {
                    writer.Write(cellX);
                    writer.Write(0u);
                    writer.Write((uint)MapFileCell.Flags.Zone);
                    writer.Write(zoneId);
                    writer.Write(zoneId);
                    writer.Write(zoneId);
                    writer.Write(zoneId);
                }
            }

            stream.Position = 0;
            var file = new MapFile();
            using var reader = new BinaryReader(stream);
            file.Read(reader);
            return file;
        }

        private static void SetGameTable<T>(
            GameTableManager manager,
            string propertyName,
            GameTable<T> table)
            where T : class, new()
        {
            typeof(GameTableManager).GetProperty(propertyName)?.SetValue(manager, table);
        }

        private static GameTable<T> CreateGameTable<T>(params T[] entries) where T : class, new()
        {
            var table = (GameTable<T>)RuntimeHelpers.GetUninitializedObject(typeof(GameTable<T>));
            typeof(GameTable<T>).GetProperty(nameof(GameTable<T>.Entries))?.SetValue(table, entries);

            FieldInfo idField = typeof(T).GetFields().First();
            uint maximumId = entries.Select(entry => (uint)idField.GetValue(entry)).DefaultIfEmpty().Max();
            int[] lookup = Enumerable.Repeat(-1, checked((int)maximumId + 1)).ToArray();
            for (int index = 0; index < entries.Length; index++)
                lookup[(uint)idField.GetValue(entries[index])] = index;

            typeof(GameTable<T>)
                .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, lookup);
            typeof(GameTable<T>)
                .GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, new GameTableHeader { MaxId = maximumId + 1ul });
            return table;
        }

        private sealed class TestWorldEntity : WorldEntity
        {
            public override EntityType Type => EntityType.NonPlayer;

            public List<uint> HookZones { get; } = [];
            public bool ThrowDuringZoneHook { get; set; }

            public TestWorldEntity(IMovementManager movementManager)
                : base(movementManager)
            {
            }

            public void SetScriptCollection(IScriptCollection value)
            {
                scriptCollection = value;
            }

            protected override void OnZoneUpdate()
            {
                HookZones.Add(Zone.Id);
                if (ThrowDuringZoneHook)
                    throw new InvalidOperationException("injected zone hook failure");
            }

            protected override IEntityModel BuildEntityModel()
            {
                return new NonPlayerEntityModel();
            }
        }
    }
}
