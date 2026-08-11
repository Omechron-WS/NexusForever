using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NexusForever.Game;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable;
using NexusForever.GameTable.Configuration.Model;
using NexusForever.GameTable.Model;
using NexusForever.Shared;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Handler;

namespace NexusForever.WorldServer.Tests.Command
{
    [Collection(QuestCommandServiceProviderCollection.Name)]
    public sealed class QuestCommandCategoryTests : IDisposable
    {
        private const uint ValidCreatureId = 100u;
        private const uint ZeroDifficultyCreatureId = 101u;
        private const uint MissingDifficultyCreatureId = 102u;

        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public QuestCommandCategoryTests()
        {
            previousProvider = LegacyServiceProvider.Provider;

            var gameTableManager = new GameTableManager(Options.Create(new GameTableConfig()));
            SetGameTable(
                gameTableManager,
                nameof(GameTableManager.Creature2),
                CreateGameTable(
                    new Creature2Entry
                    {
                        Id                    = ValidCreatureId,
                        Creature2DifficultyId = 7u
                    },
                    new Creature2Entry
                    {
                        Id                    = ZeroDifficultyCreatureId,
                        Creature2DifficultyId = 0u
                    },
                    new Creature2Entry
                    {
                        Id                    = MissingDifficultyCreatureId,
                        Creature2DifficultyId = 8u
                    }));
            SetGameTable(
                gameTableManager,
                nameof(GameTableManager.Creature2Difficulty),
                CreateGameTable(new Creature2DifficultyEntry { Id = 7u }));

            var assetManager = new AssetManager();
            SetPrivateField(
                assetManager,
                "creatureAssociatedTargetGroups",
                ImmutableDictionary<uint, ImmutableList<uint>>.Empty);

            serviceProvider = new ServiceCollection()
                .AddSingleton(gameTableManager)
                .AddSingleton(assetManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        [Fact]
        public void HandleQuestKill_TranslatesCreatureDifficultyAndPreservesQuantity()
        {
            CommandFixture fixture = CreateFixture();

            new QuestCommandCategory().HandleQuestKill(
                fixture.Context.Object,
                ValidCreatureId,
                3u);

            fixture.QuestManager.Verify(
                manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.KillCreature,
                    ValidCreatureId,
                    3u),
                Times.Once);
            fixture.QuestManager.Verify(
                manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.KillCreature2,
                    7u,
                    3u),
                Times.Once);
            fixture.QuestManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Exactly(2));
            fixture.Context.Verify(
                context => context.SendMessage(
                    $"Success! You've killed 3 of Creature ID: {ValidCreatureId}"),
                Times.Once);
        }

        [Theory]
        [InlineData(ZeroDifficultyCreatureId)]
        [InlineData(MissingDifficultyCreatureId)]
        [InlineData(999u)]
        public void HandleQuestKill_InvalidDifficultyFailsClosedAndPreservesCreatureCredit(
            uint creatureId)
        {
            CommandFixture fixture = CreateFixture();

            new QuestCommandCategory().HandleQuestKill(
                fixture.Context.Object,
                creatureId,
                null);

            fixture.QuestManager.Verify(
                manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.KillCreature,
                    creatureId,
                    1u),
                Times.Once);
            fixture.QuestManager.Verify(
                manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.KillCreature2,
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
            fixture.QuestManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Once);
        }

        private static CommandFixture CreateFixture()
        {
            var questManager = new Mock<IQuestManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);

            var context = new Mock<ICommandContext>();
            context
                .Setup(value => value.GetTargetOrInvoker<IPlayer>())
                .Returns(player.Object);
            return new CommandFixture(context, questManager);
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
                ?.SetValue(table, new GameTableHeader
                {
                    MaxId = maximumId + 1ul
                });
            return table;
        }

        private static void SetPrivateField<T>(object owner, string fieldName, T value)
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(owner, value);
        }

        private sealed record CommandFixture(
            Mock<ICommandContext> Context,
            Mock<IQuestManager> QuestManager);
    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class QuestCommandServiceProviderCollection
    {
        public const string Name = "Quest command service provider";
    }
}
