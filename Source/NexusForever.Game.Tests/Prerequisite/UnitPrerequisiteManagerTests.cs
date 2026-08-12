using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Prerequisite.Check;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Prerequisite;
using NexusForever.Game.Static.Reputation;
using NexusForever.Game.Static.Setting;
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
        public void DifficultyBuildRows_ReevaluateMapAuthorityForPureAndMixedPredicates()
        {
            PrerequisiteEntry normal = CreateEntry(
                14409u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Normal, 0u));
            PrerequisiteEntry veteran = CreateEntry(
                14893u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Veteran, 0u));
            PrerequisiteEntry normalPlayer = CreateEntry(
                29501u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u),
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Normal, 0u));
            PrerequisiteEntry veteranPlayer = CreateEntry(
                29502u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u),
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Veteran, 0u));
            PrerequisiteEntry normalExile = CreateEntry(
                30026u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.BaseFaction, PrerequisiteComparison.Equal, (uint)Faction.Exile, 0u),
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Normal, 0u));
            PrerequisiteEntry veteranExile = CreateEntry(
                30027u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.BaseFaction, PrerequisiteComparison.Equal, (uint)Faction.Exile, 0u),
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Veteran, 0u));
            PrerequisiteEntry[] entries =
            [
                normal,
                veteran,
                normalPlayer,
                veteranPlayer,
                normalExile,
                veteranExile
            ];
            using var context = new ManagerContext(entries);
            WorldDifficulty difficulty = WorldDifficulty.Normal;
            var map = new Mock<IBaseMap>();
            map.SetupGet(value => value.Difficulty).Returns(() => difficulty);
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Map).Returns(map.Object);
            player.SetupGet(value => value.Faction1).Returns(Faction.Exile);

            bool[] expectedNormal = [true, false, true, false, true, false];
            for (int i = 0; i < entries.Length; i++)
            {
                Assert.True(context.Manager.CanEvaluateForUnit(entries[i].Id));
                Assert.True(context.Manager.TryMeets(player.Object, entries[i].Id, out bool meets));
                Assert.Equal(expectedNormal[i], meets);
            }

            difficulty = WorldDifficulty.Veteran;

            for (int i = 0; i < entries.Length; i++)
            {
                Assert.True(context.Manager.TryMeets(player.Object, entries[i].Id, out bool meets));
                Assert.Equal(!expectedNormal[i], meets);
            }
        }

        [Fact]
        public void UnsupportedDifficultyShapesRemainWhollyGated()
        {
            PrerequisiteEntry invalidComparison = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Difficulty, PrerequisiteComparison.NotEqual, (uint)WorldDifficulty.Normal, 0u));
            PrerequisiteEntry invalidValue = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Count, 0u));
            PrerequisiteEntry invalidObject = CreateEntry(
                3u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Normal, 2149u));
            PrerequisiteEntry mixedUnsupported = CreateEntry(
                4u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Normal, 0u),
                (PrerequisiteType.Race, PrerequisiteComparison.Equal, 1u, 0u));
            PrerequisiteEntry[] entries =
            [
                invalidComparison,
                invalidValue,
                invalidObject,
                mixedUnsupported
            ];
            using var context = new ManagerContext(entries);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);

            foreach (PrerequisiteEntry entry in entries)
            {
                Assert.False(context.Manager.CanEvaluateForUnit(entry.Id));
                Assert.False(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
                Assert.False(meets);
            }

            unit.VerifyNoOtherCalls();
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
        public void IsPlayerRows_EvaluateRuntimeIdentityForCompletePredicates()
        {
            PrerequisiteEntry isPlayer = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u));
            PrerequisiteEntry isNotPlayer = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.NotEqual, 0u, 0u));
            using var context = new ManagerContext(isPlayer, isNotPlayer);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var nonPlayer = new Mock<IUnitEntity>(MockBehavior.Strict);

            Assert.True(context.Manager.CanEvaluateForUnit(isPlayer.Id));
            Assert.True(context.Manager.TryMeets(
                player.Object,
                isPlayer.Id,
                out bool playerMeets));
            Assert.True(playerMeets);
            Assert.True(context.Manager.TryMeets(
                nonPlayer.Object,
                isPlayer.Id,
                out bool nonPlayerMeets));
            Assert.False(nonPlayerMeets);

            Assert.True(context.Manager.CanEvaluateForUnit(isNotPlayer.Id));
            Assert.True(context.Manager.TryMeets(
                player.Object,
                isNotPlayer.Id,
                out playerMeets));
            Assert.False(playerMeets);
            Assert.True(context.Manager.TryMeets(
                nonPlayer.Object,
                isNotPlayer.Id,
                out nonPlayerMeets));
            Assert.True(nonPlayerMeets);
            player.VerifyNoOtherCalls();
            nonPlayer.VerifyNoOtherCalls();
        }

        [Fact]
        public void IsPlayerInvalidMixedAndMalformedRowsRemainGated()
        {
            PrerequisiteEntry invalidComparison = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.GreaterThan, 0u, 0u));
            PrerequisiteEntry invalidValue = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 1u, 0u));
            PrerequisiteEntry invalidObject = CreateEntry(
                3u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 1u));
            PrerequisiteEntry mixedUnsupported = CreateEntry(
                4u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u),
                (PrerequisiteType.Race, PrerequisiteComparison.Equal, 1u, 0u));
            PrerequisiteEntry malformedInactive = CreateEntry(
                5u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u));
            malformedInactive.ObjectId[1] = 1u;
            PrerequisiteEntry invalidMode = CreateEntry(
                6u,
                (EvaluationMode)99,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new ManagerContext(
                invalidComparison,
                invalidValue,
                invalidObject,
                mixedUnsupported,
                malformedInactive,
                invalidMode);
            var unit = new Mock<IPlayer>(MockBehavior.Strict);

            foreach (uint prerequisiteId in new[] { 1u, 2u, 3u, 4u, 5u, 6u })
            {
                Assert.False(context.Manager.CanEvaluateForUnit(prerequisiteId));
                Assert.False(context.Manager.TryMeets(
                    unit.Object,
                    prerequisiteId,
                    out bool meets));
                Assert.False(meets);
            }

            Assert.False(context.Manager.TryMeets(
                null,
                invalidComparison.Id,
                out bool nullMeets));
            Assert.False(nullMeets);
            unit.VerifyNoOtherCalls();
        }

        [Fact]
        public void IsCreatureBuildRowsEvaluateExactIdZeroAndIgnoredObjectDynamically()
        {
            PrerequisiteEntry equal = CreateEntry(
                345u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 5_991u, 0u));
            PrerequisiteEntry notEqual = CreateEntry(
                905u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.NotEqual, 7_471u, 0u));
            PrerequisiteEntry ignoredObject = CreateEntry(
                590u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 6_540u, 2_446u));
            PrerequisiteEntry missingCreatureEntry = CreateEntry(
                3208u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 0u, 0u));
            PrerequisiteEntry anyOfThree = CreateEntry(
                363u,
                EvaluationMode.EvaluateOR,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 6_204u, 0u),
                (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 6_205u, 0u),
                (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 6_206u, 0u));
            PrerequisiteEntry[] entries =
            [
                equal,
                notEqual,
                ignoredObject,
                missingCreatureEntry,
                anyOfThree
            ];
            using var context = new ManagerContext(entries);
            uint creatureId = 5_991u;
            Mock<IUnitEntity> unit = CreateUnit(creatureId: () => creatureId);

            foreach (PrerequisiteEntry entry in entries)
                Assert.True(context.Manager.CanEvaluateForUnit(entry.Id));

            Assert.True(context.Manager.TryMeets(unit.Object, equal.Id, out bool meets));
            Assert.True(meets);
            Assert.True(context.Manager.TryMeets(unit.Object, notEqual.Id, out meets));
            Assert.True(meets);

            creatureId = 7_471u;
            Assert.True(context.Manager.TryMeets(unit.Object, notEqual.Id, out meets));
            Assert.False(meets);

            creatureId = 6_540u;
            Assert.True(context.Manager.TryMeets(unit.Object, ignoredObject.Id, out meets));
            Assert.True(meets);

            creatureId = 0u;
            Assert.True(context.Manager.TryMeets(
                unit.Object,
                missingCreatureEntry.Id,
                out meets));
            Assert.True(meets);

            creatureId = 6_205u;
            Assert.True(context.Manager.TryMeets(unit.Object, anyOfThree.Id, out meets));
            Assert.True(meets);

            creatureId = 6_207u;
            Assert.True(context.Manager.TryMeets(unit.Object, anyOfThree.Id, out meets));
            Assert.False(meets);
        }

        [Fact]
        public void MixedIsPlayerAndIsCreatureBuildRowEvaluatesEveryComponent()
        {
            PrerequisiteEntry entry = CreateEntry(
                6216u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.NotEqual, 0u, 0u),
                (PrerequisiteType.IsCreature, PrerequisiteComparison.NotEqual, 12_664u, 0u));
            using var context = new ManagerContext(entry);
            uint creatureId = 5_991u;
            Mock<IUnitEntity> creature = CreateUnit(creatureId: () => creatureId);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(unit => unit.CreatureId).Returns(0u);

            Assert.True(context.Manager.CanEvaluateForUnit(entry.Id));
            Assert.True(context.Manager.TryMeets(
                creature.Object,
                entry.Id,
                out bool creatureMeets));
            Assert.True(creatureMeets);

            creatureId = 12_664u;
            Assert.True(context.Manager.TryMeets(
                creature.Object,
                entry.Id,
                out creatureMeets));
            Assert.False(creatureMeets);

            Assert.True(context.Manager.TryMeets(
                player.Object,
                entry.Id,
                out bool playerMeets));
            Assert.False(playerMeets);
            player.VerifyGet(unit => unit.CreatureId, Times.Once);
            player.VerifyNoOtherCalls();
        }

        [Fact]
        public void IsCreatureInvalidMalformedAndMixedUnsupportedRowsRemainWhollyGated()
        {
            PrerequisiteEntry invalidComparison = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.GreaterThan, 5_991u, 0u));
            PrerequisiteEntry mixedUnsupported = CreateEntry(
                3659u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Unknown47, PrerequisiteComparison.Equal, 0u, 0u),
                (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 0u, 0u));
            PrerequisiteEntry malformedInactive = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 5_991u, 0u));
            malformedInactive.Value[1] = 1u;
            using var context = new ManagerContext(
                invalidComparison,
                mixedUnsupported,
                malformedInactive);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);

            foreach (PrerequisiteEntry entry in new[]
            {
                invalidComparison,
                mixedUnsupported,
                malformedInactive
            })
            {
                Assert.False(context.Manager.CanEvaluateForUnit(entry.Id));
                Assert.False(context.Manager.TryMeets(
                    unit.Object,
                    entry.Id,
                    out bool meets));
                Assert.False(meets);
            }

            unit.VerifyNoOtherCalls();
        }

        [Fact]
        public void IsCreatureReadExceptionIsContainedAsEvaluationFailure()
        {
            PrerequisiteEntry entry = CreateEntry(
                345u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 5_991u, 0u));
            using var context = new ManagerContext(entry);
            var unit = new Mock<IUnitEntity>();
            unit.SetupGet(entity => entity.CreatureId)
                .Throws(new InvalidOperationException("Test creature-id read failure."));

            Exception exception = Record.Exception(() =>
            {
                Assert.True(context.Manager.CanEvaluateForUnit(entry.Id));
                Assert.False(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
                Assert.False(meets);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void ShieldBuildRowsReevaluatePercentageAbsoluteAndIgnoredObjects()
        {
            PrerequisiteEntry highShield = CreateEntry(
                35982u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Shield215, PrerequisiteComparison.GreaterThanOrEqual, 25u, 3u));
            PrerequisiteEntry lowOrEmptyShield = CreateEntry(
                40239u,
                EvaluationMode.EvaluateOR,
                (PrerequisiteType.Shield215, PrerequisiteComparison.LessThan, 25u, 3u),
                (PrerequisiteType.Shield216, PrerequisiteComparison.LessThanOrEqual, 0u, 0u));
            PrerequisiteEntry positiveLowShield = CreateEntry(
                38497u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Shield215, PrerequisiteComparison.LessThan, 25u, 0u),
                (PrerequisiteType.Shield216, PrerequisiteComparison.GreaterThan, 0u, 0u));
            PrerequisiteEntry middleShieldBand = CreateEntry(
                38498u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Shield215, PrerequisiteComparison.LessThan, 50u, 0u),
                (PrerequisiteType.Shield215, PrerequisiteComparison.GreaterThanOrEqual, 25u, 0u));
            PrerequisiteEntry creatureAtCriticalShield = CreateEntry(
                36015u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 67_575u, 0u),
                (PrerequisiteType.Shield215, PrerequisiteComparison.LessThanOrEqual, 5u, 0u));
            PrerequisiteEntry[] entries =
            [
                highShield,
                lowOrEmptyShield,
                positiveLowShield,
                middleShieldBand,
                creatureAtCriticalShield
            ];
            using var context = new ManagerContext(entries);
            uint shield = 25u;
            uint maximumShield = 100u;
            uint creatureId = 67_575u;
            Mock<IUnitEntity> unit = CreateUnit(
                creatureId: () => creatureId,
                shield: () => shield,
                maximumShield: () => maximumShield);

            foreach (PrerequisiteEntry entry in entries)
                Assert.True(context.Manager.CanEvaluateForUnit(entry.Id));

            Assert.True(context.Manager.TryMeets(unit.Object, highShield.Id, out bool meets));
            Assert.True(meets);
            Assert.True(context.Manager.TryMeets(unit.Object, lowOrEmptyShield.Id, out meets));
            Assert.False(meets);
            Assert.True(context.Manager.TryMeets(unit.Object, middleShieldBand.Id, out meets));
            Assert.True(meets);

            shield = 24u;

            Assert.True(context.Manager.TryMeets(unit.Object, highShield.Id, out meets));
            Assert.False(meets);
            Assert.True(context.Manager.TryMeets(unit.Object, lowOrEmptyShield.Id, out meets));
            Assert.True(meets);
            Assert.True(context.Manager.TryMeets(unit.Object, positiveLowShield.Id, out meets));
            Assert.True(meets);
            Assert.True(context.Manager.TryMeets(unit.Object, middleShieldBand.Id, out meets));
            Assert.False(meets);

            shield = 5u;

            Assert.True(context.Manager.TryMeets(unit.Object, creatureAtCriticalShield.Id, out meets));
            Assert.True(meets);

            creatureId = 67_576u;

            Assert.True(context.Manager.TryMeets(unit.Object, creatureAtCriticalShield.Id, out meets));
            Assert.False(meets);

            shield = 0u;

            Assert.True(context.Manager.TryMeets(unit.Object, positiveLowShield.Id, out meets));
            Assert.False(meets);
            Assert.True(context.Manager.TryMeets(unit.Object, lowOrEmptyShield.Id, out meets));
            Assert.True(meets);
            unit.VerifyGet(entity => entity.Shield, Times.AtLeastOnce);
            unit.VerifyGet(entity => entity.MaxShieldCapacity, Times.AtLeastOnce);
        }

        [Fact]
        public void UnsupportedAndMalformedShieldRowsRemainWhollyGated()
        {
            PrerequisiteEntry underSpell = CreateEntry(
                36634u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.UnderSpell, PrerequisiteComparison.Equal, 78_221u, 0u),
                (PrerequisiteType.Shield215, PrerequisiteComparison.LessThan, 50u, 0u));
            PrerequisiteEntry unknownAbsoluteCompanion = CreateEntry(
                24572u,
                EvaluationMode.EvaluateOR,
                (PrerequisiteType.Unknown71, PrerequisiteComparison.LessThanOrEqual, 30u, 3u),
                (PrerequisiteType.Shield216, PrerequisiteComparison.Equal, 0u, 0u));
            PrerequisiteEntry malformedInactive = CreateEntry(
                38500u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Shield215, PrerequisiteComparison.GreaterThanOrEqual, 75u, 0u));
            malformedInactive.PrerequisiteComparisonId[1] = PrerequisiteComparison.Equal;
            PrerequisiteEntry invalidComparison = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Shield216, (PrerequisiteComparison)999, 0u, uint.MaxValue));
            PrerequisiteEntry[] entries =
            [
                underSpell,
                unknownAbsoluteCompanion,
                malformedInactive,
                invalidComparison
            ];
            using var context = new ManagerContext(entries);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);

            foreach (PrerequisiteEntry entry in entries)
            {
                Assert.False(context.Manager.CanEvaluateForUnit(entry.Id));
                Assert.False(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
                Assert.False(meets);
            }

            unit.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(PrerequisiteType.Shield215)]
        [InlineData(PrerequisiteType.Shield216)]
        public void ShieldReadExceptionIsContainedAsEvaluationFailure(PrerequisiteType type)
        {
            PrerequisiteEntry entry = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (type, PrerequisiteComparison.GreaterThanOrEqual, 1u, uint.MaxValue));
            using var context = new ManagerContext(entry);
            var unit = new Mock<IUnitEntity>();
            unit.SetupGet(entity => entity.Shield).Returns(50u);
            if (type == PrerequisiteType.Shield215)
            {
                unit.SetupGet(entity => entity.MaxShieldCapacity)
                    .Throws(new InvalidOperationException("Test max-shield read failure."));
            }
            else
            {
                unit.SetupGet(entity => entity.Shield)
                    .Throws(new InvalidOperationException("Test current-shield read failure."));
            }

            Exception exception = Record.Exception(() =>
            {
                Assert.True(context.Manager.CanEvaluateForUnit(entry.Id));
                Assert.False(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
                Assert.False(meets);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void HealthRequirementBuildRows_EvaluateCurrentAbsoluteHealthDynamically()
        {
            PrerequisiteEntry greaterThanOne = CreateEntry(
                7958u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.GreaterThan, 1u, 0u));
            PrerequisiteEntry equalOne = CreateEntry(
                10754u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.Equal, 1u, 0u));
            PrerequisiteEntry duplicateGreaterThanOne = CreateEntry(
                10790u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.GreaterThan, 1u, 0u));
            PrerequisiteEntry lessThanOrEqualOne = CreateEntry(
                18707u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.LessThanOrEqual, 1u, 0u));
            PrerequisiteEntry equalZero = CreateEntry(
                30772u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.Equal, 0u, 0u));
            PrerequisiteEntry[] entries =
            [
                greaterThanOne,
                equalOne,
                duplicateGreaterThanOne,
                lessThanOrEqualOne,
                equalZero
            ];
            using var context = new ManagerContext(entries);
            uint health = 1u;
            Mock<IUnitEntity> unit = CreateUnit(health: () => health);

            bool[] expectedAtOne = [false, true, false, true, false];
            for (int i = 0; i < entries.Length; i++)
            {
                Assert.True(context.Manager.CanEvaluateForUnit(entries[i].Id));
                Assert.True(context.Manager.TryMeets(unit.Object, entries[i].Id, out bool meets));
                Assert.Equal(expectedAtOne[i], meets);
            }

            health = 0u;

            bool[] expectedAtZero = [false, false, false, true, true];
            for (int i = 0; i < entries.Length; i++)
            {
                Assert.True(context.Manager.TryMeets(unit.Object, entries[i].Id, out bool meets));
                Assert.Equal(expectedAtZero[i], meets);
            }

            unit.VerifyGet(entity => entity.Health, Times.Exactly(entries.Length * 2));
        }

        [Fact]
        public void HealthPercentageBuildRowsEvaluateCurrentFloatPercentageDynamically()
        {
            PrerequisiteEntry lessThanFifty = CreateEntry(
                315u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.LessThan, 50u, 0u));
            PrerequisiteEntry lessThanOrEqualTwentyFive = CreateEntry(
                965u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.LessThanOrEqual, 25u, 0u));
            PrerequisiteEntry percentageAndCurrentHealth = CreateEntry(
                3467u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.LessThanOrEqual, 25u, 0u),
                (PrerequisiteType.Vital, PrerequisiteComparison.GreaterThan, 0u, (uint)Vital.Health));
            PrerequisiteEntry[] entries =
            [
                lessThanFifty,
                lessThanOrEqualTwentyFive,
                percentageAndCurrentHealth
            ];
            using var context = new ManagerContext(entries);
            uint health = 25u;
            Mock<IUnitEntity> unit = CreateUnit(
                health: () => health,
                maximumHealth: () => 100u);

            foreach (PrerequisiteEntry entry in entries)
            {
                Assert.True(context.Manager.CanEvaluateForUnit(entry.Id));
                Assert.True(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
                Assert.True(meets);
            }

            health = 50u;

            foreach (PrerequisiteEntry entry in entries)
            {
                Assert.True(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
                Assert.False(meets);
            }

            health = 0u;

            Assert.True(context.Manager.TryMeets(unit.Object, lessThanFifty.Id, out bool belowFifty));
            Assert.True(belowFifty);
            Assert.True(context.Manager.TryMeets(
                unit.Object,
                lessThanOrEqualTwentyFive.Id,
                out bool atMostTwentyFive));
            Assert.True(atMostTwentyFive);
            Assert.True(context.Manager.TryMeets(
                unit.Object,
                percentageAndCurrentHealth.Id,
                out bool positiveCurrentHealth));
            Assert.False(positiveCurrentHealth);
        }

        [Fact]
        public void AbsoluteAndPercentageHealthBuildRowsEvaluateTogetherDynamically()
        {
            PrerequisiteEntry absoluteAndPercentage = CreateEntry(
                22677u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.GreaterThanOrEqual, 5_000u, 0u),
                (PrerequisiteType.Health, PrerequisiteComparison.GreaterThanOrEqual, 25u, 0u));
            PrerequisiteEntry absoluteOrPercentage = CreateEntry(
                22678u,
                EvaluationMode.EvaluateOR,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.LessThan, 5_000u, 0u),
                (PrerequisiteType.Health, PrerequisiteComparison.LessThan, 25u, 0u));
            using var context = new ManagerContext(absoluteAndPercentage, absoluteOrPercentage);
            uint health = 6_000u;
            uint maximumHealth = 10_000u;
            Mock<IUnitEntity> unit = CreateUnit(
                health: () => health,
                maximumHealth: () => maximumHealth);

            Assert.True(context.Manager.CanEvaluateForUnit(absoluteAndPercentage.Id));
            Assert.True(context.Manager.CanEvaluateForUnit(absoluteOrPercentage.Id));
            Assert.True(context.Manager.TryMeets(
                unit.Object,
                absoluteAndPercentage.Id,
                out bool meetsAnd));
            Assert.True(meetsAnd);
            Assert.True(context.Manager.TryMeets(
                unit.Object,
                absoluteOrPercentage.Id,
                out bool meetsOr));
            Assert.False(meetsOr);

            maximumHealth = 100_000u;

            Assert.True(context.Manager.TryMeets(
                unit.Object,
                absoluteAndPercentage.Id,
                out meetsAnd));
            Assert.False(meetsAnd);
            Assert.True(context.Manager.TryMeets(
                unit.Object,
                absoluteOrPercentage.Id,
                out meetsOr));
            Assert.True(meetsOr);
        }

        [Fact]
        public void UnsupportedAndMalformedHealthRowsRemainWhollyGated()
        {
            PrerequisiteEntry unknownAndAbsolute = CreateEntry(
                33064u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Unknown47, PrerequisiteComparison.NotEqual, 21_526u, 0u),
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.GreaterThan, 100u, 0u));
            PrerequisiteEntry invalidObject = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.Equal, 50u, 1u));
            PrerequisiteEntry invalidValue = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.Equal, 101u, 0u));
            PrerequisiteEntry invalidComparison = CreateEntry(
                3u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, (PrerequisiteComparison)999, 50u, 0u));
            PrerequisiteEntry malformedInactive = CreateEntry(
                4u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.Equal, 50u, 0u));
            malformedInactive.Value[1] = 1u;
            PrerequisiteEntry[] entries =
            [
                unknownAndAbsolute,
                invalidObject,
                invalidValue,
                invalidComparison,
                malformedInactive
            ];
            using var context = new ManagerContext(entries);
            int healthReads = 0;
            Mock<IUnitEntity> unit = CreateUnit(health: () =>
            {
                healthReads++;
                return 10_000u;
            }, maximumHealth: () => throw new InvalidOperationException("Gated max-health read."));

            foreach (PrerequisiteEntry entry in entries)
            {
                Assert.False(context.Manager.CanEvaluateForUnit(entry.Id));
                Assert.False(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
                Assert.False(meets);
            }

            Assert.Equal(0, healthReads);
            unit.VerifyGet(entity => entity.Health, Times.Never);
            unit.VerifyGet(entity => entity.MaxHealth, Times.Never);
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
        public void HealthRequirementReadException_IsContainedAsEvaluationFailure()
        {
            PrerequisiteEntry entry = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.GreaterThanOrEqual, 1u, 0u));
            using var context = new ManagerContext(entry);
            var unit = new Mock<IUnitEntity>();
            unit.SetupGet(entity => entity.Health)
                .Throws(new InvalidOperationException("Test unit health read failure."));

            Exception exception = Record.Exception(() =>
            {
                Assert.True(context.Manager.CanEvaluateForUnit(entry.Id));
                Assert.False(context.Manager.TryMeets(unit.Object, entry.Id, out bool meets));
                Assert.False(meets);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void HealthPercentageReadExceptionIsContainedAsEvaluationFailure()
        {
            PrerequisiteEntry entry = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.GreaterThanOrEqual, 50u, 0u));
            using var context = new ManagerContext(entry);
            var unit = new Mock<IUnitEntity>();
            unit.SetupGet(entity => entity.Health).Returns(50u);
            unit.SetupGet(entity => entity.MaxHealth)
                .Throws(new InvalidOperationException("Test unit max-health read failure."));

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
            Func<bool> inCombat = null,
            Func<uint> health = null,
            Func<uint> maximumHealth = null,
            Func<uint> creatureId = null,
            Func<uint> shield = null,
            Func<uint> maximumShield = null)
        {
            var unit = new Mock<IUnitEntity>();
            unit.SetupGet(entity => entity.Level).Returns(level);
            unit.SetupGet(entity => entity.Faction1).Returns(faction);
            unit.SetupGet(entity => entity.CreatureId).Returns(() => creatureId?.Invoke() ?? 0u);
            unit.SetupGet(entity => entity.IsAlive).Returns(() => alive?.Invoke() ?? true);
            unit.SetupGet(entity => entity.InCombat).Returns(() => inCombat?.Invoke() ?? false);
            unit.SetupGet(entity => entity.Health).Returns(() => health?.Invoke() ?? 1u);
            unit.SetupGet(entity => entity.MaxHealth).Returns(() => maximumHealth?.Invoke() ?? 1u);
            unit.SetupGet(entity => entity.Shield).Returns(() => shield?.Invoke() ?? 0u);
            unit.SetupGet(entity => entity.MaxShieldCapacity).Returns(() => maximumShield?.Invoke() ?? 0u);
            unit.Setup(entity => entity.TryGetVitalValue(
                    It.IsAny<Vital>(),
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital vital, out float value) =>
                {
                    if (vital == Vital.Health)
                    {
                        value = health?.Invoke() ?? 1u;
                        return true;
                    }

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
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckIsPlayer>(PrerequisiteType.IsPlayer)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckIsCreature>(PrerequisiteType.IsCreature)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckHealth>(PrerequisiteType.Health)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckHealthRequirement>(PrerequisiteType.HealthRequirement)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckShield>(PrerequisiteType.Shield215)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckShieldRequirement>(PrerequisiteType.Shield216)
                    .AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckDifficulty>(PrerequisiteType.Difficulty)
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
