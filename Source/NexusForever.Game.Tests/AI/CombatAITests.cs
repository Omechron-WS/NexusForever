using System.Collections;
using System.Numerics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Entity.Movement.Command.Position;
using NexusForever.Game.Abstract.Entity.Movement.Generator;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.Session;
using NexusForever.Script.Main.AI;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;
using Moq;

namespace NexusForever.Game.Tests.AI
{
    public class CombatAITests
    {
        [Fact]
        public void Profile_IsFilteredAndRuntimeGatedToTangleclawOnly()
        {
            ScriptFilterCreatureIdAttribute filter = typeof(CombatAI)
                .GetCustomAttribute<ScriptFilterCreatureIdAttribute>();

            Assert.NotNull(filter);
            Assert.Equal(new uint[] { 33932u }, filter.CreatureId);

            var harness = new CombatAIHarness(33931u);
            TargetHarness target = harness.AddTarget(2u, new Vector3(4f, 0f, 0f));
            harness.AddThreat(target, 10u);

            harness.AI.Update(10d);

            Assert.Equal(0, harness.TargetWriteCount);
            Assert.Empty(harness.Casts);
            Assert.Empty(harness.MovementLaunches);
        }

        [Fact]
        public void Constructor_UsesStringLoggerCategoryWithoutClosedGenericScriptType()
        {
            Type[] parameterTypes = typeof(CombatAI)
                .GetConstructors()
                .Single()
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray();

            Assert.Equal(new[] { typeof(ILoggerFactory), typeof(IDirectMovementGenerator) }, parameterTypes);

            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory
                .Setup(factory => factory.CreateLogger("NexusForever.Script.Main.AI.CombatAI"))
                .Returns(Mock.Of<ILogger>());

            _ = new CombatAI(loggerFactory.Object, Mock.Of<IDirectMovementGenerator>());

            loggerFactory.Verify(
                factory => factory.CreateLogger("NexusForever.Script.Main.AI.CombatAI"),
                Times.Once);
        }

        [Fact]
        public void VisibilityAndRangeCallbacks_DoNotCreateReactiveCombat()
        {
            var harness = new CombatAIHarness();
            TargetHarness target = harness.AddTarget(2u, new Vector3(4f, 0f, 0f));
            var script = (IGridEntityScript)harness.AI;

            script.OnAddVisibleEntity(target.Entity.Object);
            script.OnEnterRange(target.Entity.Object);
            harness.AI.Update(10d);

            Assert.False(harness.ThreatManager.IsThreatened);
            Assert.Equal(0, harness.TargetWriteCount);
            Assert.Empty(harness.Casts);
            Assert.Empty(harness.MovementLaunches);
        }

        [Fact]
        public void ThreatCallback_DefersSelectionAndSameTargetIsNotWrittenTwice()
        {
            var harness = new CombatAIHarness();
            TargetHarness target = harness.AddTarget(2u, new Vector3(4f, 0f, 0f));
            TestHostile hostile = harness.AddThreat(target, 10u);

            Assert.Equal(0, harness.TargetWriteCount);

            harness.AI.Update(0.1d);
            harness.AI.OnThreatChange(hostile);
            harness.AI.Update(0.1d);

            Assert.Equal(2u, harness.TargetGuid);
            Assert.Equal(1, harness.TargetWriteCount);
            Assert.Empty(harness.Casts);
        }

        [Fact]
        public void InvalidTopThreat_IsPrunedBeforeSelectingNextValidTarget()
        {
            var harness = new CombatAIHarness();
            TargetHarness dead = harness.AddTarget(2u, new Vector3(4f, 0f, 0f));
            dead.IsAlive = false;
            TargetHarness alive = harness.AddTarget(3u, new Vector3(4f, 0f, 0f));
            harness.AddThreat(dead, 20u);
            harness.AddThreat(alive, 10u);

            harness.AI.Update(0.1d);

            Assert.Equal(new uint[] { 2u }, harness.ThreatManager.RemovedIds);
            Assert.Equal(3u, harness.TargetGuid);
            Assert.Equal(1, harness.TargetWriteCount);
        }

        [Fact]
        public void OutOfRangeTarget_UsesBoundedChaseReplanningAndLiveSpeedMultiplier()
        {
            var harness = new CombatAIHarness
            {
                MoveSpeedMultiplier = 1.25f
            };
            TargetHarness target = harness.AddTarget(2u, new Vector3(10f, 0f, 0f));
            harness.AddThreat(target, 10u);

            harness.AI.Update(0.1d);
            harness.AI.Update(0.1d);

            Assert.Single(harness.MovementLaunches);
            Assert.Equal(8.75f, harness.MovementLaunches[0].Speed);
            Assert.Equal(target.Position, harness.MovementLaunches[0].Final);

            harness.AI.Update(0.2d);
            Assert.Equal(2, harness.MovementLaunches.Count);

            target.Position = new Vector3(11.1f, 0f, 0f);
            harness.AI.Update(0.01d);
            Assert.Equal(3, harness.MovementLaunches.Count);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void InvalidMoveSpeedMultiplier_FailsClosedWithoutMovementOrCast(float multiplier)
        {
            var harness = new CombatAIHarness
            {
                MoveSpeedMultiplier = multiplier
            };
            TargetHarness target = harness.AddTarget(2u, new Vector3(10f, 0f, 0f));
            harness.AddThreat(target, 10u);

            harness.AI.Update(10d);

            Assert.Empty(harness.MovementLaunches);
            Assert.Empty(harness.Casts);
            Assert.False(harness.ThreatManager.IsThreatened);
            Assert.Null(harness.TargetGuid);
        }

        [Fact]
        public void InRangeTarget_CastsOnly65812AtOnePointFiveSecondCadence()
        {
            var harness = new CombatAIHarness();
            TargetHarness target = harness.AddTarget(2u, new Vector3(4f, 0f, 0f));
            harness.AddThreat(target, 10u);

            harness.AI.Update(0.1d);
            harness.AI.Update(1.49d);
            Assert.Empty(harness.Casts);

            harness.AI.Update(0.01d);
            Assert.Single(harness.Casts);
            Assert.Equal(65812u, harness.Casts[0].SpellId);
            Assert.Equal(2u, harness.Casts[0].Parameters.PrimaryTargetId);

            harness.AI.Update(0.01d);
            Assert.Single(harness.Casts);

            harness.AI.Update(double.MaxValue);
            Assert.Equal(2, harness.Casts.Count);
        }

        [Fact]
        public void ActiveCastDefersAttack_AndAdmissionExceptionKeepsResetCadence()
        {
            var harness = new CombatAIHarness();
            TargetHarness target = harness.AddTarget(2u, new Vector3(4f, 0f, 0f));
            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);

            var activeSpell = new Mock<ISpell>();
            activeSpell.SetupGet(spell => spell.IsCasting).Returns(true);
            harness.ActiveSpell = activeSpell.Object;
            harness.AI.Update(1.5d);
            Assert.Empty(harness.Casts);

            harness.ActiveSpell = null;
            harness.ThrowOnCast = true;
            Exception exception = Record.Exception(() => harness.AI.Update(0.01d));
            Assert.Null(exception);
            Assert.Single(harness.Casts);

            harness.ThrowOnCast = false;
            harness.AI.Update(0.01d);
            Assert.Single(harness.Casts);
            harness.AI.Update(1.49d);
            Assert.Equal(2, harness.Casts.Count);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(0d)]
        [InlineData(-1d)]
        public void InvalidTick_FreezesActiveMovementAndPreservesAttackTimer(double lastTick)
        {
            var harness = new CombatAIHarness();
            TargetHarness target = harness.AddTarget(2u, new Vector3(10f, 0f, 0f));
            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);
            Assert.Single(harness.MovementLaunches);

            target.Position = new Vector3(4f, 0f, 0f);
            harness.AI.Update(lastTick);

            Assert.Equal(1, harness.StopMovementCount);
            Assert.Empty(harness.Casts);

            harness.AI.Update(1.49d);
            Assert.Empty(harness.Casts);
            harness.AI.Update(0.01d);
            Assert.Single(harness.Casts);
        }

        [Fact]
        public void Leash_EvadesClearsReturnThreatAndResetsAtHome()
        {
            var harness = new CombatAIHarness
            {
                Health = 40u,
                MaxHealth = 100u,
                HomeRotation = new Vector3(1f, 2f, 3f)
            };
            harness.Reattach();
            TargetHarness target = harness.AddTarget(2u, new Vector3(20f, 0f, 0f));
            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);

            harness.Position = new Vector3(16f, 0f, 0f);
            harness.AI.Update(0.1d);

            Assert.Null(harness.TargetGuid);
            Assert.False(harness.ThreatManager.IsThreatened);
            Assert.Equal(15f, harness.MovementLaunches[^1].Speed);
            Assert.Equal(Vector3.Zero, harness.MovementLaunches[^1].Final);

            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);
            Assert.False(harness.ThreatManager.IsThreatened);
            Assert.Null(harness.TargetGuid);

            harness.Position = Vector3.Zero;
            harness.AI.OnPositionEntityCommandFinalise(Mock.Of<IPositionCommand>());
            harness.AI.Update(0.1d);

            Assert.Equal(100u, harness.Health);
            Assert.Equal(harness.HomeRotation, harness.Rotation);
            Assert.Equal(1, harness.HealCount);
            Assert.Empty(harness.Casts);

            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);
            Assert.Equal(2u, harness.TargetGuid);
        }

        [Fact]
        public void ReloadWhileEngaged_AttachesInWorldWithoutMapCallbackAndKeepsLeashHome()
        {
            var harness = new CombatAIHarness();
            harness.Position = new Vector3(10f, 0f, 0f);
            TargetHarness target = harness.AddTarget(2u, new Vector3(20f, 0f, 0f));
            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);
            Assert.Single(harness.MovementLaunches);

            CombatAI reloaded = harness.ReloadWithoutMapCallback();

            Assert.Null(harness.TargetGuid);
            Assert.True(harness.ThreatManager.IsThreatened);
            Assert.Equal(2, harness.StopMovementCount);

            reloaded.Update(0.1d);
            Assert.Equal(2u, harness.TargetGuid);
            Assert.Equal(2, harness.MovementLaunches.Count);

            harness.Position = new Vector3(16f, 0f, 0f);
            reloaded.Update(0.1d);

            Assert.False(harness.ThreatManager.IsThreatened);
            Assert.Null(harness.TargetGuid);
            Assert.Equal(Vector3.Zero, harness.MovementLaunches[^1].Final);
        }

        [Fact]
        public void ReloadAfterUnloadMovementFailure_FreezesInheritedPathBeforeAttaching()
        {
            var harness = CreateChasingHarness();
            TargetHarness target = harness.GetTarget(2u);
            target.Position = new Vector3(4f, 0f, 0f);
            harness.PositionResetFailuresRemaining = 1;
            int launchCount = harness.MovementLaunches.Count;

            CombatAI reloaded = harness.ReloadWithoutMapCallback();

            Assert.True(harness.ThreatManager.IsThreatened);
            Assert.Null(harness.TargetGuid);
            Assert.Equal(1, harness.StopMovementCount);
            Assert.Equal(2, harness.MoveDefaultsCount);
            Assert.Equal(2, harness.StateDefaultCount);

            reloaded.Update(0.1d);

            Assert.Equal(2u, harness.TargetGuid);
            Assert.Equal(launchCount, harness.MovementLaunches.Count);
            Assert.True(harness.ThreatManager.IsThreatened);
        }

        [Fact]
        public void ReloadCleanupFailure_RemainsDetachedUntilInheritedPathResetSucceeds()
        {
            var harness = CreateChasingHarness();
            TargetHarness target = harness.GetTarget(2u);
            target.Position = new Vector3(4f, 0f, 0f);
            harness.PositionResetFailuresRemaining = 2;
            int launchCount = harness.MovementLaunches.Count;

            CombatAI reloaded = harness.ReloadWithoutMapCallback();

            Assert.True(harness.ThreatManager.IsThreatened);
            Assert.Null(harness.TargetGuid);
            Assert.Equal(0, harness.StopMovementCount);

            reloaded.Update(0.1d);

            Assert.True(harness.ThreatManager.IsThreatened);
            Assert.Null(harness.TargetGuid);
            Assert.Equal(1, harness.StopMovementCount);
            Assert.Equal(launchCount, harness.MovementLaunches.Count);

            reloaded.Update(0.1d);

            Assert.Equal(2u, harness.TargetGuid);
            Assert.Equal(launchCount, harness.MovementLaunches.Count);
        }

        [Fact]
        public void ReloadWithUnknownPosition_RemainsDetachedWithoutOriginTeleportUntilValidRead()
        {
            var harness = CreateChasingHarness();
            TargetHarness target = harness.GetTarget(2u);
            target.Position = new Vector3(10f, 0f, 0f);
            harness.Position = new Vector3(6f, 0f, 0f);
            int launchCount = harness.MovementLaunches.Count;

            CombatAI reloaded = harness.ReloadWithoutMapCallback(() =>
            {
                harness.Position = new Vector3(float.NaN, float.NaN, float.NaN);
                harness.LeashPosition = new Vector3(float.NaN, float.NaN, float.NaN);
            });

            Assert.True(float.IsNaN(harness.Position.X));
            Assert.Equal(1, harness.StopMovementCount);
            Assert.Null(harness.TargetGuid);
            Assert.True(harness.ThreatManager.IsThreatened);

            harness.Position = new Vector3(6f, 0f, 0f);
            harness.PositionReadFailuresRemaining = 1;
            reloaded.Update(0.1d);

            Assert.Equal(new Vector3(6f, 0f, 0f), harness.Position);
            Assert.Equal(1, harness.StopMovementCount);
            Assert.Null(harness.TargetGuid);

            harness.LeashPosition = new Vector3(5f, 0f, 0f);
            reloaded.Update(0.1d);

            Assert.Equal(new Vector3(6f, 0f, 0f), harness.Position);
            Assert.Equal(2, harness.StopMovementCount);
            Assert.Null(harness.TargetGuid);

            reloaded.Update(0.1d);

            Assert.Equal(2u, harness.TargetGuid);
            Assert.Equal(launchCount, harness.MovementLaunches.Count);
        }

        [Fact]
        public void ReloadWhileEvading_ResumesReturnToStableLeashAndHealsOnce()
        {
            var harness = new CombatAIHarness
            {
                Health = 40u,
                MaxHealth = 100u,
                LeashPosition = new Vector3(5f, 0f, 0f),
                HomeRotation = new Vector3(1f, 2f, 3f)
            };
            harness.Reattach();
            TargetHarness target = harness.AddTarget(2u, new Vector3(25f, 0f, 0f));
            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);

            harness.Position = new Vector3(21f, 0f, 0f);
            harness.AI.Update(0.1d);
            Assert.False(harness.ThreatManager.IsThreatened);
            Assert.Equal(harness.LeashPosition, harness.MovementLaunches[^1].Final);
            int launchCount = harness.MovementLaunches.Count;

            CombatAI reloaded = harness.ReloadWithoutMapCallback();
            reloaded.Update(0.1d);

            Assert.Equal(launchCount + 1, harness.MovementLaunches.Count);
            Assert.Equal(harness.LeashPosition, harness.MovementLaunches[^1].Final);

            harness.Position = harness.LeashPosition;
            reloaded.OnPositionEntityCommandFinalise(Mock.Of<IPositionCommand>());
            reloaded.Update(0.1d);
            reloaded.Update(0.1d);

            Assert.Equal(100u, harness.Health);
            Assert.Equal(1, harness.HealCount);
            Assert.Equal(harness.HomeRotation, harness.Rotation);
        }

        [Fact]
        public void ReloadTargetValidationFailure_DoesNotWedgeAttachAndEvadesWhenDisplaced()
        {
            var harness = new CombatAIHarness();
            TargetHarness target = harness.AddTarget(2u, new Vector3(20f, 0f, 0f));
            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);
            harness.Position = new Vector3(10f, 0f, 0f);
            harness.TargetValidationFailuresRemaining = 1;
            int launchCount = harness.MovementLaunches.Count;

            CombatAI reloaded = harness.ReloadWithoutMapCallback();

            Assert.Equal(1, harness.TargetValidationFailuresRemaining);
            Assert.True(harness.ThreatManager.IsThreatened);

            Exception exception = Record.Exception(() => reloaded.Update(0.1d));

            Assert.Null(exception);
            Assert.Equal(0, harness.TargetValidationFailuresRemaining);
            Assert.False(harness.ThreatManager.IsThreatened);
            Assert.Null(harness.TargetGuid);
            Assert.Equal(launchCount + 1, harness.MovementLaunches.Count);
            Assert.Equal(harness.LeashPosition, harness.MovementLaunches[^1].Final);
        }

        [Fact]
        public void RemoveAndReAddWhileChasing_StopsOldMovementClearsThreatAndUsesNewLeash()
        {
            var harness = new CombatAIHarness();
            TargetHarness target = harness.AddTarget(2u, new Vector3(10f, 0f, 0f));
            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);
            Assert.Single(harness.MovementLaunches);

            harness.AI.OnRemoveFromMap(harness.Map.Object);

            Assert.Null(harness.TargetGuid);
            Assert.False(harness.ThreatManager.IsThreatened);
            Assert.Equal(1, harness.StopMovementCount);

            harness.LeashPosition = new Vector3(50f, 0f, 0f);
            harness.Position = harness.LeashPosition;
            target.Position = new Vector3(60f, 0f, 0f);
            harness.AI.OnAddToMap(harness.Map.Object);
            harness.AI.Update(0.1d);
            Assert.Single(harness.MovementLaunches);

            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);

            Assert.Equal(2, harness.MovementLaunches.Count);
            Assert.Equal(harness.LeashPosition, harness.MovementLaunches[^1].Begin);
            Assert.Equal(target.Position, harness.MovementLaunches[^1].Final);
        }

        [Fact]
        public void RemoveAndReAddWhileEvading_DoesNotResumeOldReturnMovement()
        {
            var harness = new CombatAIHarness();
            TargetHarness target = harness.AddTarget(2u, new Vector3(20f, 0f, 0f));
            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);

            harness.Position = new Vector3(16f, 0f, 0f);
            harness.AI.Update(0.1d);
            Assert.Equal(Vector3.Zero, harness.MovementLaunches[^1].Final);

            harness.AI.OnRemoveFromMap(harness.Map.Object);
            int launchCount = harness.MovementLaunches.Count;

            harness.LeashPosition = new Vector3(50f, 0f, 0f);
            harness.Position = harness.LeashPosition;
            harness.AI.OnAddToMap(harness.Map.Object);
            harness.AI.Update(0.1d);

            Assert.Equal(launchCount, harness.MovementLaunches.Count);
            Assert.Null(harness.TargetGuid);
            Assert.False(harness.ThreatManager.IsThreatened);
        }

        [Fact]
        public void RemoveAndReAddCleanupFailure_RetriesAttachWithoutResumingOldPath()
        {
            var harness = CreateChasingHarness();
            TargetHarness target = harness.GetTarget(2u);
            harness.PositionResetFailuresRemaining = 2;
            int launchCount = harness.MovementLaunches.Count;

            harness.AI.OnRemoveFromMap(harness.Map.Object);
            harness.AI.OnAddToMap(harness.Map.Object);

            Assert.False(harness.ThreatManager.IsThreatened);
            Assert.Null(harness.TargetGuid);
            Assert.Equal(0, harness.StopMovementCount);

            harness.AI.Update(0.1d);

            Assert.Equal(1, harness.StopMovementCount);
            Assert.Equal(launchCount, harness.MovementLaunches.Count);

            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);

            Assert.Equal(launchCount + 1, harness.MovementLaunches.Count);
            Assert.Equal(target.Position, harness.MovementLaunches[^1].Final);
        }

        [Fact]
        public void DeadCleanup_RetriesFailuresAndStillAttemptsEveryMovementReset()
        {
            var harness = CreateChasingHarness();
            harness.TargetClearFailuresRemaining = 1;
            harness.PositionResetFailuresRemaining = 1;
            harness.IsAlive = false;

            Exception firstException = Record.Exception(() => harness.AI.Update(double.NaN));

            Assert.Null(firstException);
            Assert.Equal(2u, harness.TargetGuid);
            Assert.Equal(0, harness.StopMovementCount);
            Assert.Equal(1, harness.MoveDefaultsCount);
            Assert.Equal(1, harness.StateDefaultCount);

            harness.AI.Update(double.NaN);

            Assert.Null(harness.TargetGuid);
            Assert.Equal(1, harness.StopMovementCount);
            Assert.Equal(2, harness.MoveDefaultsCount);
            Assert.Equal(2, harness.StateDefaultCount);

            harness.AI.Update(double.NaN);
            Assert.Equal(1, harness.StopMovementCount);
        }

        [Fact]
        public void DisabledCleanup_RetriesFailuresAndStillAttemptsEveryMovementReset()
        {
            var harness = CreateChasingHarness();
            harness.TargetClearFailuresRemaining = 1;
            harness.PositionResetFailuresRemaining = 1;
            harness.CreatureId = 33931u;

            Exception firstException = Record.Exception(() => harness.AI.Update(0.1d));

            Assert.Null(firstException);
            Assert.Equal(2u, harness.TargetGuid);
            Assert.Equal(0, harness.StopMovementCount);
            Assert.Equal(1, harness.MoveDefaultsCount);
            Assert.Equal(1, harness.StateDefaultCount);
            Assert.False(harness.ThreatManager.IsThreatened);
            Assert.Equal(new[] { 2u }, harness.ThreatManager.RemovedIds);

            harness.AI.Update(0.1d);

            Assert.Null(harness.TargetGuid);
            Assert.Equal(1, harness.StopMovementCount);
            Assert.Equal(2, harness.MoveDefaultsCount);
            Assert.Equal(2, harness.StateDefaultCount);
        }

        [Fact]
        public void DeathPreemptsInvalidTick_StopsOnceAndNeverCastsOrHeals()
        {
            var harness = new CombatAIHarness
            {
                Health = 40u,
                MaxHealth = 100u
            };
            TargetHarness target = harness.AddTarget(2u, new Vector3(10f, 0f, 0f));
            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);
            int targetWritesBeforeDeath = harness.TargetWriteCount;

            harness.IsAlive = false;
            harness.AI.Update(double.NaN);
            harness.AI.Update(double.NaN);

            Assert.Equal(1, harness.StopMovementCount);
            Assert.Equal(targetWritesBeforeDeath + 1, harness.TargetWriteCount);
            Assert.Null(harness.TargetGuid);
            Assert.Empty(harness.Casts);
            Assert.Equal(0, harness.HealCount);
        }

        [Fact]
        public void RemovalPreemptsLaterUpdates_AndFreshInstanceStartsIdle()
        {
            var removed = new CombatAIHarness();
            TargetHarness removedTarget = removed.AddTarget(2u, new Vector3(4f, 0f, 0f));
            removed.AI.OnRemoveFromMap(removed.Map.Object);
            removed.AddThreat(removedTarget, 10u);
            removed.AI.Update(10d);

            Assert.Equal(0, removed.TargetWriteCount);
            Assert.Empty(removed.Casts);
            Assert.Empty(removed.MovementLaunches);

            var respawned = new CombatAIHarness();
            respawned.AI.Update(10d);
            Assert.Equal(0, respawned.TargetWriteCount);
            Assert.Empty(respawned.Casts);
            Assert.Empty(respawned.MovementLaunches);
        }

        [Fact]
        public void SplineBackedPilot_FailsClosed()
        {
            var harness = new CombatAIHarness(spline: new EntitySplineModel());
            TargetHarness target = harness.AddTarget(2u, new Vector3(4f, 0f, 0f));
            harness.AddThreat(target, 10u);

            harness.AI.Update(10d);

            Assert.Equal(0, harness.TargetWriteCount);
            Assert.Empty(harness.Casts);
            Assert.Empty(harness.MovementLaunches);
        }

        private static CombatAIHarness CreateChasingHarness()
        {
            var harness = new CombatAIHarness();
            TargetHarness target = harness.AddTarget(2u, new Vector3(10f, 0f, 0f));
            harness.AddThreat(target, 10u);
            harness.AI.Update(0.1d);
            Assert.Single(harness.MovementLaunches);
            return harness;
        }

        private sealed class CombatAIHarness
        {
            public Mock<ICreatureEntity> Owner { get; } = new();
            public Mock<IMovementManager> Movement { get; } = new();
            public Mock<IBaseMap> Map { get; } = new();
            public Mock<IDirectMovementGenerator> Generator { get; } = new();
            public Mock<ILoggerFactory> LoggerFactory { get; } = new();
            public TestThreatManager ThreatManager { get; } = new();
            public CombatAI AI { get; private set; }

            public uint CreatureId { get; set; }
            public bool IsAlive { get; set; } = true;
            public bool InWorld { get; set; }
            public Vector3 Position { get; set; }
            public Vector3 Rotation { get; set; }
            public Vector3 HomeRotation { get; set; }
            public Vector3 LeashPosition { get; set; }
            public float MoveSpeedMultiplier { get; set; } = 1f;
            public uint Health { get; set; } = 100u;
            public uint MaxHealth { get; set; } = 100u;
            public uint? TargetGuid { get; private set; }
            public ISpell ActiveSpell { get; set; }
            public bool ThrowOnCast { get; set; }
            public int TargetClearFailuresRemaining { get; set; }
            public int PositionResetFailuresRemaining { get; set; }
            public int PositionReadFailuresRemaining { get; set; }
            public int TargetValidationFailuresRemaining { get; set; }

            public int TargetWriteCount { get; private set; }
            public int StopMovementCount { get; private set; }
            public int MoveDefaultsCount { get; private set; }
            public int StateDefaultCount { get; private set; }
            public int HealCount { get; private set; }
            public List<CastAttempt> Casts { get; } = [];
            public List<MovementLaunch> MovementLaunches { get; } = [];

            private readonly EntitySplineModel spline;
            private readonly Dictionary<uint, TargetHarness> targets = [];

            public CombatAIHarness(uint creatureId = 33932u, EntitySplineModel spline = null)
            {
                CreatureId = creatureId;
                this.spline = spline;
                Generator.SetupAllProperties();

                Owner.SetupGet(entity => entity.Guid).Returns(1u);
                Owner.SetupGet(entity => entity.CreatureId).Returns(() => CreatureId);
                Owner.SetupGet(entity => entity.Spline).Returns(() => this.spline);
                Owner.SetupGet(entity => entity.Map).Returns(() => InWorld ? Map.Object : null);
                Owner.SetupGet(entity => entity.InWorld).Returns(() => InWorld);
                Owner.SetupGet(entity => entity.IsAlive).Returns(() => IsAlive);
                Owner.SetupGet(entity => entity.TargetGuid).Returns(() => TargetGuid);
                Owner.SetupGet(entity => entity.LeashPosition).Returns(() => LeashPosition);
                Owner.SetupGet(entity => entity.ThreatManager).Returns(ThreatManager);
                Owner.SetupGet(entity => entity.MovementManager).Returns(Movement.Object);
                Owner.SetupGet(entity => entity.Health).Returns(() => Health);
                Owner.SetupGet(entity => entity.MaxHealth).Returns(() => MaxHealth);
                Owner
                    .Setup(entity => entity.GetPropertyValue(Property.MoveSpeedMultiplier))
                    .Returns(() => MoveSpeedMultiplier);
                Owner
                    .Setup(entity => entity.GetVisible<IUnitEntity>(It.IsAny<uint>()))
                    .Returns((uint guid) => targets.TryGetValue(guid, out TargetHarness target) ? target.Entity.Object : null);
                Owner
                    .Setup(entity => entity.CanAttack(It.IsAny<IUnitEntity>()))
                    .Returns((IUnitEntity candidate) =>
                    {
                        if (TargetValidationFailuresRemaining > 0)
                        {
                            TargetValidationFailuresRemaining--;
                            throw new InvalidOperationException("Test target validation failure.");
                        }

                        return targets.TryGetValue(candidate.Guid, out TargetHarness target)
                            && target.Attackable;
                    });
                Owner
                    .Setup(entity => entity.SetTarget(It.IsAny<IWorldEntity>(), It.IsAny<uint>()))
                    .Callback<IWorldEntity, uint>((target, threat) =>
                    {
                        if (target == null && TargetClearFailuresRemaining > 0)
                        {
                            TargetClearFailuresRemaining--;
                            throw new InvalidOperationException("Test target cleanup failure.");
                        }

                        TargetGuid = target?.Guid;
                        TargetWriteCount++;
                    });
                Owner
                    .Setup(entity => entity.GetActiveSpell(It.IsAny<Func<ISpell, bool>>()))
                    .Returns((Func<ISpell, bool> predicate) => ActiveSpell != null && predicate(ActiveSpell)
                        ? ActiveSpell
                        : null);
                Owner
                    .Setup(entity => entity.CastSpellTracked(It.IsAny<uint>(), It.IsAny<ISpellParameters>()))
                    .Returns((uint spellId, ISpellParameters parameters) =>
                    {
                        Casts.Add(new CastAttempt(spellId, parameters));
                        if (ThrowOnCast)
                            throw new InvalidOperationException("Test cast admission failure.");
                        return null;
                    });
                Owner
                    .Setup(entity => entity.ModifyHealth(It.IsAny<uint>(), It.IsAny<DamageType>(), It.IsAny<IUnitEntity>()))
                    .Callback<uint, DamageType, IUnitEntity>((amount, damageType, source) =>
                    {
                        if (damageType != DamageType.Heal)
                            return;

                        Health = Math.Min(MaxHealth, Health + amount);
                        HealCount++;
                    });

                Movement
                    .Setup(manager => manager.GetPosition())
                    .Returns(() =>
                    {
                        if (PositionReadFailuresRemaining > 0)
                        {
                            PositionReadFailuresRemaining--;
                            throw new InvalidOperationException("Test movement position read failure.");
                        }

                        return Position;
                    });
                Movement.Setup(manager => manager.GetRotation()).Returns(() => Rotation);
                Movement
                    .Setup(manager => manager.SetPosition(It.IsAny<Vector3>(), It.IsAny<bool>()))
                    .Callback<Vector3, bool>((position, blend) =>
                    {
                        if (PositionResetFailuresRemaining > 0)
                        {
                            PositionResetFailuresRemaining--;
                            throw new InvalidOperationException("Test movement cleanup failure.");
                        }

                        Position = position;
                        StopMovementCount++;
                    });
                Movement
                    .Setup(manager => manager.SetMoveDefaults(It.IsAny<bool>()))
                    .Callback<bool>(blend => MoveDefaultsCount++);
                Movement
                    .Setup(manager => manager.SetStateDefault())
                    .Callback(() => StateDefaultCount++);
                Movement
                    .Setup(manager => manager.SetRotation(It.IsAny<Vector3>(), It.IsAny<bool>()))
                    .Callback<Vector3, bool>((rotation, blend) => Rotation = rotation);
                Movement
                    .Setup(manager => manager.LaunchGenerator(It.IsAny<IMovementGenerator>(), It.IsAny<float>(), It.IsAny<NexusForever.Game.Static.Entity.Movement.Spline.SplineMode>()))
                    .Callback<IMovementGenerator, float, NexusForever.Game.Static.Entity.Movement.Spline.SplineMode>((generator, speed, mode) =>
                    {
                        var directGenerator = Assert.IsAssignableFrom<IDirectMovementGenerator>(generator);
                        MovementLaunches.Add(new MovementLaunch(directGenerator.Begin, directGenerator.Final, speed));
                    });

                LoggerFactory
                    .Setup(factory => factory.CreateLogger(It.IsAny<string>()))
                    .Returns(Mock.Of<ILogger>());
                LoadAI();
                InWorld = true;
                AI.OnAddToMap(Map.Object);
            }

            public CombatAI ReloadWithoutMapCallback(Action afterUnload = null)
            {
                AI.OnUnload();
                afterUnload?.Invoke();
                LoadAI();
                return AI;
            }

            public void Reattach()
            {
                Position = LeashPosition;
                Rotation = HomeRotation;
                AI.OnAddToMap(Map.Object);
                StopMovementCount = 0;
            }

            public TargetHarness AddTarget(uint guid, Vector3 position)
            {
                var target = new TargetHarness(guid, Map.Object, position);
                targets.Add(guid, target);
                return target;
            }

            public TargetHarness GetTarget(uint guid)
            {
                return targets[guid];
            }

            public TestHostile AddThreat(TargetHarness target, uint threat)
            {
                TestHostile hostile = ThreatManager.Add(target.Entity.Object.Guid, threat);
                AI.OnThreatAddTarget(hostile);
                return hostile;
            }

            private void LoadAI()
            {
                AI = new CombatAI(LoggerFactory.Object, Generator.Object);
                ThreatManager.OnRemoved = AI.OnThreatRemoveTarget;
                AI.OnLoad(Owner.Object);
            }
        }

        private sealed class TargetHarness
        {
            public Mock<IUnitEntity> Entity { get; } = new();
            public Mock<IMovementManager> Movement { get; } = new();

            public Vector3 Position { get; set; }
            public bool IsAlive { get; set; } = true;
            public bool InWorld { get; set; } = true;
            public bool Attackable { get; set; } = true;
            public IBaseMap Map { get; set; }

            public TargetHarness(uint guid, IBaseMap map, Vector3 position)
            {
                Position = position;
                Map = map;

                Entity.SetupGet(target => target.Guid).Returns(guid);
                Entity.SetupGet(target => target.IsAlive).Returns(() => IsAlive);
                Entity.SetupGet(target => target.InWorld).Returns(() => InWorld);
                Entity.SetupGet(target => target.Map).Returns(() => Map);
                Entity.SetupGet(target => target.MovementManager).Returns(Movement.Object);
                Movement.Setup(manager => manager.GetPosition()).Returns(() => Position);
            }
        }

        private sealed class TestThreatManager : IThreatManager
        {
            private readonly Dictionary<uint, TestHostile> hostiles = [];

            public bool IsThreatened => hostiles.Count > 0;
            public List<uint> RemovedIds { get; } = [];
            public Action<IHostileEntity> OnRemoved { get; set; }

            public TestHostile Add(uint unitId, uint threat)
            {
                var hostile = new TestHostile(unitId, threat);
                hostiles.Add(unitId, hostile);
                return hostile;
            }

            public void Update(double lastTick)
            {
            }

            public IHostileEntity GetHostile(uint target)
            {
                return hostiles.GetValueOrDefault(target);
            }

            public IHostileEntity GetTopHostile()
            {
                return hostiles.Values
                    .OrderByDescending(hostile => hostile.Threat)
                    .ThenBy(hostile => hostile.HatedUnitId)
                    .FirstOrDefault();
            }

            public void UpdateThreat(IUnitEntity target, int threat)
            {
                throw new NotSupportedException();
            }

            public void ClearThreatList()
            {
                foreach (uint unitId in hostiles.Keys.ToArray())
                    RemoveHostile(unitId);
            }

            public void RemoveHostile(uint unitId)
            {
                if (!hostiles.Remove(unitId, out TestHostile hostile))
                    return;

                RemovedIds.Add(unitId);
                OnRemoved?.Invoke(hostile);
            }

            public void BroadcastThreatList()
            {
            }

            public void SendThreatList(IGameSession session)
            {
            }

            public IEnumerator<IHostileEntity> GetEnumerator()
            {
                return hostiles.Values.Cast<IHostileEntity>().GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private sealed class TestHostile : IHostileEntity
        {
            public uint HatedUnitId { get; }
            public uint Threat { get; private set; }
            public bool IsPvP => false;
            public bool IsExpired => false;

            public TestHostile(uint hatedUnitId, uint threat)
            {
                HatedUnitId = hatedUnitId;
                Threat = threat;
            }

            public void Update(double lastTick)
            {
            }

            public void UpdateThreat(int threatDelta)
            {
                Threat = (uint)Math.Clamp(Threat + threatDelta, 0u, uint.MaxValue);
            }

            public void Refresh()
            {
            }
        }

        private sealed record CastAttempt(uint SpellId, ISpellParameters Parameters);
        private sealed record MovementLaunch(Vector3 Begin, Vector3 Final, float Speed);
    }
}
