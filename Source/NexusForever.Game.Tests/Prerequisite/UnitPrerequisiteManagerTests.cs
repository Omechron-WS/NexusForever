using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Prerequisite.Check;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Prerequisite;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Prerequisite
{
    public class UnitPrerequisiteManagerTests
    {
        [Fact]
        public void UnitSafeAndOrRows_EvaluateCompletePredicate()
        {
            PrerequisiteEntry andEntry = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u),
                (PrerequisiteType.Vital, PrerequisiteComparison.GreaterThan, 5u, (uint)Vital.Resource1),
                (PrerequisiteType.BaseFaction, PrerequisiteComparison.Equal, (uint)Faction.Exile, 0u));
            PrerequisiteEntry orEntry = CreateEntry(
                2u,
                EvaluationMode.EvaluateOR,
                (PrerequisiteType.Level, PrerequisiteComparison.Equal, 99u, 0u),
                (PrerequisiteType.BaseFaction, PrerequisiteComparison.Equal, (uint)Faction.Exile, 0u));
            using var context = new ManagerContext(andEntry, orEntry);
            Mock<IUnitEntity> unit = CreateUnit(level: 10u, faction: Faction.Exile, resource1: 8f);

            Assert.True(context.Manager.CanEvaluateForUnit(andEntry.Id));
            Assert.True(context.Manager.TryMeets(unit.Object, andEntry.Id, out bool meetsAnd));
            Assert.True(meetsAnd);

            Assert.True(context.Manager.CanEvaluateForUnit(orEntry.Id));
            Assert.True(context.Manager.TryMeets(unit.Object, orEntry.Id, out bool meetsOr));
            Assert.True(meetsOr);
        }

        [Fact]
        public void ValidFalsePredicate_IsDistinguishedFromEvaluationFailure()
        {
            PrerequisiteEntry entry = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThan, 10u, 0u));
            using var context = new ManagerContext(entry);
            Mock<IUnitEntity> unit = CreateUnit(level: 10u);

            Assert.True(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
            Assert.False(meets);
        }

        [Fact]
        public void InCombatRow_ReevaluatesStateWhileInvalidShapesAndUnderSpellRemainGated()
        {
            PrerequisiteEntry inCombatEntry = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            PrerequisiteEntry underSpellEntry = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.UnderSpell, PrerequisiteComparison.Equal, 123u, 0u));
            PrerequisiteEntry invalidValueEntry = CreateEntry(
                3u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 1u, 0u));
            PrerequisiteEntry invalidObjectEntry = CreateEntry(
                4u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 1u));
            using var context = new ManagerContext(
                inCombatEntry,
                underSpellEntry,
                invalidValueEntry,
                invalidObjectEntry);
            bool inCombat = false;
            Mock<IUnitEntity> unit = CreateUnit(inCombat: () => inCombat);

            Assert.True(context.Manager.CanEvaluateForUnit(inCombatEntry.Id));
            Assert.True(context.Manager.TryMeets(unit.Object, inCombatEntry.Id, out bool meets));
            Assert.False(meets);

            inCombat = true;

            Assert.True(context.Manager.TryMeets(unit.Object, inCombatEntry.Id, out meets));
            Assert.True(meets);
            foreach (uint prerequisiteId in new[]
            {
                underSpellEntry.Id,
                invalidValueEntry.Id,
                invalidObjectEntry.Id
            })
            {
                Assert.False(context.Manager.CanEvaluateForUnit(prerequisiteId));
                Assert.False(context.Manager.TryMeets(unit.Object, prerequisiteId, out meets));
                Assert.False(meets);
            }
        }

        [Fact]
        public void DeadStateRows_ReevaluateWhileInvalidAndMixedShapesRemainGated()
        {
            PrerequisiteEntry aliveAndOutOfCombat = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 0u, 0u),
                (PrerequisiteType.InCombat, PrerequisiteComparison.NotEqual, 0u, 0u));
            PrerequisiteEntry deadOrInCombat = CreateEntry(
                2u,
                EvaluationMode.EvaluateOR,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 1u, 0u),
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            PrerequisiteEntry invalidValue = CreateEntry(
                3u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 2u, 0u));
            PrerequisiteEntry invalidObject = CreateEntry(
                4u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 0u, 1u));
            PrerequisiteEntry mixedPlayerOnly = CreateEntry(
                5u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 0u, 0u),
                (PrerequisiteType.Race, PrerequisiteComparison.Equal, 1u, 0u));
            PrerequisiteEntry malformedInactive = CreateEntry(
                6u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 0u, 0u));
            malformedInactive.Value[1] = 1u;
            using var context = new ManagerContext(
                aliveAndOutOfCombat,
                deadOrInCombat,
                invalidValue,
                invalidObject,
                mixedPlayerOnly,
                malformedInactive);
            bool isAlive = true;
            Mock<IUnitEntity> unit = CreateUnit(
                alive: () => isAlive,
                inCombat: () => false);

            Assert.True(context.Manager.CanEvaluateForUnit(aliveAndOutOfCombat.Id));
            Assert.True(context.Manager.TryMeets(
                unit.Object,
                aliveAndOutOfCombat.Id,
                out bool meets));
            Assert.True(meets);
            Assert.True(context.Manager.CanEvaluateForUnit(deadOrInCombat.Id));
            Assert.True(context.Manager.TryMeets(unit.Object, deadOrInCombat.Id, out meets));
            Assert.False(meets);

            isAlive = false;

            Assert.True(context.Manager.TryMeets(
                unit.Object,
                aliveAndOutOfCombat.Id,
                out meets));
            Assert.False(meets);
            Assert.True(context.Manager.TryMeets(unit.Object, deadOrInCombat.Id, out meets));
            Assert.True(meets);

            foreach (uint prerequisiteId in new[]
            {
                invalidValue.Id,
                invalidObject.Id,
                mixedPlayerOnly.Id,
                malformedInactive.Id
            })
            {
                Assert.False(context.Manager.CanEvaluateForUnit(prerequisiteId));
                Assert.False(context.Manager.TryMeets(unit.Object, prerequisiteId, out meets));
                Assert.False(meets);
            }
        }

        [Fact]
        public void TableBackedFactionOutsideLocalEnum_RemainsUnitSafe()
        {
            const uint factionId = 170u;
            PrerequisiteEntry entry = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.BaseFaction, PrerequisiteComparison.Equal, factionId, 0u));
            using var context = new ManagerContext(entry);
            Mock<IUnitEntity> unit = CreateUnit(faction: (Faction)factionId);

            Assert.True(context.Manager.CanEvaluateForUnit(entry.Id));
            Assert.True(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
            Assert.True(meets);
        }

        [Fact]
        public void MixedPlayerOnlyRow_FailsClosedWithoutPlayerFallback()
        {
            PrerequisiteEntry entry = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u),
                (PrerequisiteType.Race, PrerequisiteComparison.Equal, 1u, 0u));
            using var context = new ManagerContext(entry);
            var player = new Mock<IPlayer>(MockBehavior.Strict);

            Assert.False(context.Manager.CanEvaluateForUnit(entry.Id));
            Assert.False(context.Manager.TryMeets(player.Object, entry.Id, out bool meets));
            Assert.False(meets);
            player.VerifyNoOtherCalls();
        }

        [Fact]
        public void OrRow_DynamicUnsupportedComponentRejectsWholePredicate()
        {
            PrerequisiteEntry entry = CreateEntry(
                1u,
                EvaluationMode.EvaluateOR,
                (PrerequisiteType.Level, PrerequisiteComparison.Equal, 10u, 0u),
                (PrerequisiteType.Vital, PrerequisiteComparison.Equal, 0u, (uint)Vital.Resource1));
            using var context = new ManagerContext(entry);
            Mock<IUnitEntity> unit = CreateUnit(level: 10u);

            Assert.True(context.Manager.CanEvaluateForUnit(entry.Id));
            Assert.False(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
            Assert.False(meets);
            unit.Verify(entity => entity.TryGetVitalValue(
                Vital.Resource1,
                out It.Ref<float>.IsAny), Times.Once);
        }

        [Fact]
        public void UnitReadException_IsContainedAsEvaluationFailure()
        {
            PrerequisiteEntry entry = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.Equal, 10u, 0u));
            using var context = new ManagerContext(entry);
            var unit = new Mock<IUnitEntity>();
            unit.SetupGet(entity => entity.Level)
                .Throws(new InvalidOperationException("Test unit read failure."));

            Exception exception = Record.Exception(() =>
            {
                Assert.False(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
                Assert.False(meets);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void DeadStateReadException_IsContainedAsEvaluationFailure()
        {
            PrerequisiteEntry entry = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new ManagerContext(entry);
            var unit = new Mock<IUnitEntity>();
            unit.SetupGet(entity => entity.IsAlive)
                .Throws(new InvalidOperationException("Test unit death-state read failure."));

            Exception exception = Record.Exception(() =>
            {
                Assert.True(context.Manager.CanEvaluateForUnit(entry.Id));
                Assert.False(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
                Assert.False(meets);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void MissingMalformedOrInvalidRows_FailClosed()
        {
            PrerequisiteEntry invalidMode = CreateEntry(
                1u,
                (EvaluationMode)99,
                (PrerequisiteType.Level, PrerequisiteComparison.Equal, 10u, 0u));
            PrerequisiteEntry invalidComparison = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, (PrerequisiteComparison)99, 10u, 0u));
            PrerequisiteEntry invalidVital = CreateEntry(
                3u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Vital, PrerequisiteComparison.Equal, 10u, uint.MaxValue));
            PrerequisiteEntry empty = CreateEntry(4u, EvaluationMode.EvaluateAND);
            PrerequisiteEntry malformed = CreateEntry(
                5u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.Equal, 10u, 0u));
            malformed.Value = [10u, 0u];
            PrerequisiteEntry invalidFaction = CreateEntry(
                6u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.BaseFaction, PrerequisiteComparison.NotEqual, uint.MaxValue, 0u));
            PrerequisiteEntry invalidLevelObject = CreateEntry(
                7u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.Equal, 10u, 1u));
            PrerequisiteEntry invalidEmptyComponentData = CreateEntry(
                8u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.Equal, 10u, 0u));
            invalidEmptyComponentData.Value[1] = 1u;
            PrerequisiteEntry unsupportedBreath = CreateEntry(
                9u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Vital, PrerequisiteComparison.Equal, 10u, (uint)Vital.Breath));
            PrerequisiteEntry inexactVitalValue = CreateEntry(
                10u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Vital, PrerequisiteComparison.Equal, 16_777_217u, (uint)Vital.Resource1));
            using var context = new ManagerContext(
                invalidMode,
                invalidComparison,
                invalidVital,
                empty,
                malformed,
                invalidFaction,
                invalidLevelObject,
                invalidEmptyComponentData,
                unsupportedBreath,
                inexactVitalValue);
            Mock<IUnitEntity> unit = CreateUnit(level: 10u);

            foreach (uint prerequisiteId in new[] { 1u, 2u, 3u, 4u, 5u, 6u, 7u, 8u, 9u, 10u, 99u })
            {
                Assert.False(context.Manager.CanEvaluateForUnit(prerequisiteId));
                Assert.False(context.Manager.TryMeets(unit.Object, prerequisiteId, out bool meets));
                Assert.False(meets);
            }
        }

        private static Mock<IUnitEntity> CreateUnit(
            uint level = 1u,
            Faction faction = Faction.Dominion,
            float? resource1 = null,
            Func<bool> alive = null,
            Func<bool> inCombat = null)
        {
            var unit = new Mock<IUnitEntity>();
            unit.SetupGet(entity => entity.Level).Returns(level);
            unit.SetupGet(entity => entity.Faction1).Returns(faction);
            unit.SetupGet(entity => entity.IsAlive).Returns(() => alive?.Invoke() ?? true);
            unit.SetupGet(entity => entity.InCombat).Returns(() => inCombat?.Invoke() ?? false);
            unit.Setup(entity => entity.TryGetVitalValue(
                    It.IsAny<Vital>(),
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital vital, out float value) =>
                {
                    value = resource1 ?? 0f;
                    return vital == Vital.Resource1 && resource1.HasValue;
                }));
            return unit;
        }

        private static PrerequisiteEntry CreateEntry(
            uint id,
            EvaluationMode mode,
            params (PrerequisiteType Type, PrerequisiteComparison Comparison, uint Value, uint ObjectId)[] components)
        {
            var entry = new PrerequisiteEntry
            {
                Id                       = id,
                Flags                    = mode,
                PrerequisiteTypeId       = new PrerequisiteType[3],
                PrerequisiteComparisonId = new PrerequisiteComparison[3],
                Value                    = new uint[3],
                ObjectId                 = new uint[3]
            };

            for (int i = 0; i < components.Length; i++)
            {
                entry.PrerequisiteTypeId[i] = components[i].Type;
                entry.PrerequisiteComparisonId[i] = components[i].Comparison;
                entry.Value[i] = components[i].Value;
                entry.ObjectId[i] = components[i].ObjectId;
            }

            return entry;
        }

        private static GameTable<PrerequisiteEntry> CreateGameTable(params PrerequisiteEntry[] entries)
        {
            var table = (GameTable<PrerequisiteEntry>)RuntimeHelpers.GetUninitializedObject(
                typeof(GameTable<PrerequisiteEntry>));
            typeof(GameTable<PrerequisiteEntry>)
                .GetProperty(nameof(GameTable<PrerequisiteEntry>.Entries))
                ?.SetValue(table, entries);

            int lookupLength = checked((int)entries.Max(entry => entry.Id) + 1);
            int[] lookup = Enumerable.Repeat(-1, lookupLength).ToArray();
            for (int i = 0; i < entries.Length; i++)
                lookup[entries[i].Id] = i;

            typeof(GameTable<PrerequisiteEntry>)
                .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, lookup);
            typeof(GameTable<PrerequisiteEntry>)
                .GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, new GameTableHeader { MaxId = (ulong)lookupLength });
            return table;
        }

        private static GameTable<Faction2Entry> CreateFactionTable()
        {
            Faction2Entry[] entries =
            [
                new Faction2Entry { Id = 166u },
                new Faction2Entry { Id = 167u },
                new Faction2Entry { Id = 170u },
                new Faction2Entry { Id = 171u },
                new Faction2Entry { Id = 390u },
                new Faction2Entry { Id = 391u },
                new Faction2Entry { Id = 392u }
            ];
            var table = (GameTable<Faction2Entry>)RuntimeHelpers.GetUninitializedObject(
                typeof(GameTable<Faction2Entry>));
            typeof(GameTable<Faction2Entry>)
                .GetProperty(nameof(GameTable<Faction2Entry>.Entries))
                ?.SetValue(table, entries);

            int[] lookup = Enumerable.Repeat(-1, 393).ToArray();
            for (int i = 0; i < entries.Length; i++)
                lookup[entries[i].Id] = i;

            typeof(GameTable<Faction2Entry>)
                .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, lookup);
            typeof(GameTable<Faction2Entry>)
                .GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, new GameTableHeader { MaxId = (ulong)lookup.Length });
            return table;
        }

        private sealed class ManagerContext : IDisposable
        {
            public PrerequisiteManager Manager { get; }

            private readonly ServiceProvider serviceProvider;

            public ManagerContext(params PrerequisiteEntry[] entries)
            {
                var gameTableManager = new Mock<IGameTableManager>();
                gameTableManager.SetupGet(manager => manager.Prerequisite)
                    .Returns(CreateGameTable(entries));
                gameTableManager.SetupGet(manager => manager.Faction2)
                    .Returns(CreateFactionTable());
                serviceProvider = new ServiceCollection()
                    .AddLogging()
                    .AddSingleton<IGameTableManager>(gameTableManager.Object)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckLevel>(PrerequisiteType.Level)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckVital>(PrerequisiteType.Vital)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckBaseFaction>(PrerequisiteType.BaseFaction)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckInCombat>(PrerequisiteType.InCombat)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckDeadState>(PrerequisiteType.DeadState)
                    .BuildServiceProvider();

                Manager = new PrerequisiteManager(
                    Mock.Of<ILogger<PrerequisiteManager>>(),
                    serviceProvider,
                    gameTableManager.Object,
                    Mock.Of<IFactory<IPrerequisiteParameters>>());
            }

            public void Dispose()
            {
                serviceProvider.Dispose();
            }
        }

        private delegate bool TryGetVitalValue(Vital vital, out float value);
    }
}
