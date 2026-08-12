using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Combat;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Combat
{
    [Collection(CombatServiceProviderCollection.Name)]
    public class UnitEntityCombatTests
    {
        [Fact]
        public void ApplyProc_DuplicateEffectForEventType_IsRejected()
        {
            TestUnitEntity entity = CreateEntity(1u);
            Mock<IProcInfo> first = CreateProc(entity, ProcType.CriticalDamage, 789u, 123u);
            Mock<IProcInfo> duplicate = CreateProc(entity, ProcType.CriticalDamage, 789u, 456u);

            bool firstApplied = entity.ApplyProc(first.Object);
            bool duplicateApplied = entity.ApplyProc(duplicate.Object);
            entity.FireProc(ProcType.CriticalDamage);

            Assert.True(firstApplied);
            Assert.False(duplicateApplied);
            first.Verify(p => p.Trigger(null), Times.Once);
            duplicate.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
        }

        [Fact]
        public void ApplyProc_DistinctEffectsFromSameApplicator_AreBothRegistered()
        {
            TestUnitEntity entity = CreateEntity(1u);
            Mock<IProcInfo> first = CreateProc(entity, ProcType.OnDamageReceived, 789u, 123u);
            Mock<IProcInfo> second = CreateProc(entity, ProcType.OnDamageReceived, 790u, 123u);

            bool firstApplied = entity.ApplyProc(first.Object);
            bool secondApplied = entity.ApplyProc(second.Object);
            entity.FireProc(ProcType.OnDamageReceived);

            Assert.True(firstApplied);
            Assert.True(secondApplied);
            first.Verify(p => p.Trigger(null), Times.Once);
            second.Verify(p => p.Trigger(null), Times.Once);
        }

        [Fact]
        public void Update_AdvancesRegisteredProcsAndRemovalStopsDispatch()
        {
            TestUnitEntity entity = CreateEntity(1u);
            Mock<IProcInfo> proc = CreateProc(entity, ProcType.BeginMoving, 789u, 123u);
            entity.ApplyProc(proc.Object);

            entity.Update(0.1d);
            bool removed = entity.RemoveProc(proc.Object);
            entity.FireProc(ProcType.BeginMoving);

            Assert.True(removed);
            proc.Verify(p => p.Update(0.1d), Times.Once);
            proc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
            proc.Verify(p => p.Cancel(), Times.Once);
        }

        [Fact]
        public void Update_AdvancesThreatManagerWithWorldTickDelta()
        {
            TestUnitEntity entity = CreateEntity(1u);
            var threatManager = new Mock<IThreatManager>();
            SetThreatManager(entity, threatManager.Object);

            entity.Update(0.25d);

            threatManager.Verify(t => t.Update(0.25d), Times.Once);
        }

        [Fact]
        public void Update_ThrowingProcDoesNotBlockSiblingOrThreatTick()
        {
            TestUnitEntity entity = CreateEntity(1u);
            var threatManager = new Mock<IThreatManager>();
            SetThreatManager(entity, threatManager.Object);
            Mock<IProcInfo> failedProc = CreateProc(entity, ProcType.BeginMoving, 789u, 123u);
            Mock<IProcInfo> healthyProc = CreateProc(entity, ProcType.BeginMoving, 790u, 456u);
            failedProc.Setup(p => p.Update(0.1d)).Throws<InvalidOperationException>();
            entity.ApplyProc(failedProc.Object);
            entity.ApplyProc(healthyProc.Object);

            entity.Update(0.1d);
            entity.Update(0.2d);

            failedProc.Verify(p => p.Update(It.IsAny<double>()), Times.Once);
            failedProc.Verify(p => p.Cancel(), Times.Once);
            healthyProc.Verify(p => p.Update(0.1d), Times.Once);
            healthyProc.Verify(p => p.Update(0.2d), Times.Once);
            threatManager.Verify(t => t.Update(0.1d), Times.Once);
            threatManager.Verify(t => t.Update(0.2d), Times.Once);
        }

        [Fact]
        public void Update_ThrowingProcCleanupDoesNotBlockSiblingOrThreatTick()
        {
            TestUnitEntity entity = CreateEntity(1u);
            var threatManager = new Mock<IThreatManager>();
            SetThreatManager(entity, threatManager.Object);
            Mock<IProcInfo> failedProc = CreateProc(entity, ProcType.BeginMoving, 789u, 123u);
            Mock<IProcInfo> healthyProc = CreateProc(entity, ProcType.BeginMoving, 790u, 456u);
            failedProc.Setup(p => p.Update(0.1d)).Throws<InvalidOperationException>();
            failedProc.Setup(p => p.Cancel()).Throws<InvalidOperationException>();
            entity.ApplyProc(failedProc.Object);
            entity.ApplyProc(healthyProc.Object);

            entity.Update(0.1d);
            entity.Update(0.2d);

            failedProc.Verify(p => p.Update(It.IsAny<double>()), Times.Once);
            failedProc.Verify(p => p.Cancel(), Times.Once);
            healthyProc.Verify(p => p.Update(0.1d), Times.Once);
            healthyProc.Verify(p => p.Update(0.2d), Times.Once);
            threatManager.Verify(t => t.Update(0.1d), Times.Once);
            threatManager.Verify(t => t.Update(0.2d), Times.Once);
        }

        [Fact]
        public void Update_ReentrantRemovalSkipsRemovedSnapshotSibling()
        {
            TestUnitEntity entity = CreateEntity(1u);
            var threatManager = new Mock<IThreatManager>();
            SetThreatManager(entity, threatManager.Object);
            Mock<IProcInfo> firstProc = CreateProc(entity, ProcType.BeginMoving, 789u, 123u);
            Mock<IProcInfo> removedProc = CreateProc(entity, ProcType.BeginMoving, 790u, 456u);
            firstProc
                .Setup(p => p.Update(0.1d))
                .Callback(() => entity.RemoveProc(removedProc.Object));
            entity.ApplyProc(firstProc.Object);
            entity.ApplyProc(removedProc.Object);

            entity.Update(0.1d);

            firstProc.Verify(p => p.Update(0.1d), Times.Once);
            removedProc.Verify(p => p.Update(It.IsAny<double>()), Times.Never);
            removedProc.Verify(p => p.Cancel(), Times.Once);
            threatManager.Verify(t => t.Update(0.1d), Times.Once);
        }

        [Theory]
        [InlineData(PendingSpellFailureStage.Update)]
        [InlineData(PendingSpellFailureStage.LateUpdate)]
        [InlineData(PendingSpellFailureStage.IsFinished)]
        public void Update_PendingSpellStageFailureRetriesWithoutBlockingSiblingOrCoreTick(
            PendingSpellFailureStage failureStage)
        {
            TestUnitEntity entity = CreateEntity(1u);
            var operations = new List<string>();
            var threatManager = new Mock<IThreatManager>();
            threatManager
                .Setup(value => value.Update(It.IsAny<double>()))
                .Callback<double>(_ => operations.Add("threat"));
            SetThreatManager(entity, threatManager.Object);

            bool hasThrown = false;
            void ThrowOnce(PendingSpellFailureStage stage)
            {
                if (hasThrown || failureStage != stage)
                    return;

                hasThrown = true;
                throw new InvalidOperationException("Test pending spell failure.");
            }

            var failedSpell = new Mock<ISpell>();
            failedSpell
                .Setup(value => value.Update(It.IsAny<double>()))
                .Callback<double>(_ =>
                {
                    operations.Add("failed-update");
                    ThrowOnce(PendingSpellFailureStage.Update);
                });
            failedSpell
                .Setup(value => value.LateUpdate(It.IsAny<double>()))
                .Callback<double>(_ =>
                {
                    operations.Add("failed-late");
                    ThrowOnce(PendingSpellFailureStage.LateUpdate);
                });
            failedSpell
                .SetupGet(value => value.IsFinished)
                .Returns(() =>
                {
                    operations.Add("failed-finished");
                    ThrowOnce(PendingSpellFailureStage.IsFinished);
                    return true;
                });
            failedSpell
                .Setup(value => value.Dispose())
                .Callback(() => operations.Add("failed-dispose"));

            var healthySpell = new Mock<ISpell>();
            healthySpell
                .Setup(value => value.Update(It.IsAny<double>()))
                .Callback<double>(_ => operations.Add("healthy-update"));
            healthySpell
                .Setup(value => value.LateUpdate(It.IsAny<double>()))
                .Callback<double>(_ => operations.Add("healthy-late"));
            healthySpell
                .SetupGet(value => value.IsFinished)
                .Returns(() =>
                {
                    operations.Add("healthy-finished");
                    return true;
                });
            healthySpell
                .Setup(value => value.Dispose())
                .Callback(() => operations.Add("healthy-dispose"));

            Mock<IProcInfo> proc = CreateProc(entity, ProcType.BeginMoving, 789u, 123u);
            proc
                .Setup(value => value.Update(It.IsAny<double>()))
                .Callback<double>(_ => operations.Add("proc"));
            AddPendingSpell(entity, failedSpell.Object);
            AddPendingSpell(entity, healthySpell.Object);
            entity.ApplyProc(proc.Object);

            Exception firstException = Record.Exception(() => entity.Update(0.01d));
            Exception secondException = Record.Exception(() => entity.Update(0.02d));
            Exception thirdException = Record.Exception(() => entity.Update(0.03d));

            Assert.Null(firstException);
            Assert.Null(secondException);
            Assert.Null(thirdException);
            string[] failedPrefix = failureStage switch
            {
                PendingSpellFailureStage.Update => ["failed-update"],
                PendingSpellFailureStage.LateUpdate => ["failed-update", "failed-late"],
                PendingSpellFailureStage.IsFinished => ["failed-update", "failed-late", "failed-finished"],
                _ => throw new ArgumentOutOfRangeException(nameof(failureStage))
            };
            Assert.Equal(
                failedPrefix
                    .Concat([
                        "healthy-update", "healthy-late", "healthy-finished", "healthy-dispose", "proc", "threat",
                        "failed-update", "failed-late", "failed-finished", "failed-dispose", "proc", "threat",
                        "proc", "threat"
                    ]),
                operations);
            failedSpell.Verify(value => value.Finish(), Times.Never);
            failedSpell.Verify(value => value.Dispose(), Times.Once);
            healthySpell.Verify(value => value.Dispose(), Times.Once);
            proc.Verify(value => value.Update(It.IsAny<double>()), Times.Exactly(3));
            threatManager.Verify(value => value.Update(It.IsAny<double>()), Times.Exactly(3));
        }

        [Fact]
        public void Update_PendingSpellDisposeFailureRemainsOwnedAndRetriesWithoutBlockingCoreTick()
        {
            TestUnitEntity entity = CreateEntity(1u);
            var threatManager = new Mock<IThreatManager>();
            SetThreatManager(entity, threatManager.Object);
            int disposeAttempts = 0;
            var failedSpell = new Mock<ISpell>();
            failedSpell.SetupGet(value => value.IsFinished).Returns(true);
            failedSpell
                .Setup(value => value.Dispose())
                .Callback(() =>
                {
                    if (disposeAttempts++ == 0)
                        throw new InvalidOperationException("Test pending spell disposal failure.");
                });
            var healthySpell = new Mock<ISpell>();
            healthySpell.SetupGet(value => value.IsFinished).Returns(false);
            Mock<IProcInfo> proc = CreateProc(entity, ProcType.BeginMoving, 789u, 123u);
            AddPendingSpell(entity, failedSpell.Object);
            AddPendingSpell(entity, healthySpell.Object);
            entity.ApplyProc(proc.Object);

            Exception firstException = Record.Exception(() => entity.Update(0.01d));
            Exception secondException = Record.Exception(() => entity.Update(0.02d));
            Exception thirdException = Record.Exception(() => entity.Update(0.03d));

            Assert.Null(firstException);
            Assert.Null(secondException);
            Assert.Null(thirdException);
            failedSpell.Verify(value => value.Update(It.IsAny<double>()), Times.Exactly(2));
            failedSpell.Verify(value => value.LateUpdate(It.IsAny<double>()), Times.Exactly(2));
            failedSpell.VerifyGet(value => value.IsFinished, Times.Exactly(2));
            failedSpell.Verify(value => value.Dispose(), Times.Exactly(2));
            failedSpell.Verify(value => value.Finish(), Times.Never);
            healthySpell.Verify(value => value.Update(It.IsAny<double>()), Times.Exactly(3));
            proc.Verify(value => value.Update(It.IsAny<double>()), Times.Exactly(3));
            threatManager.Verify(value => value.Update(It.IsAny<double>()), Times.Exactly(3));
        }

        [Fact]
        public void Update_PendingSpellReentrantRemovalSkipsStaleSnapshotAndDefersAddition()
        {
            TestUnitEntity entity = CreateEntity(1u);
            var threatManager = new Mock<IThreatManager>();
            SetThreatManager(entity, threatManager.Object);
            var firstSpell = new Mock<ISpell>();
            var removedSpell = new Mock<ISpell>();
            var addedSpell = new Mock<ISpell>();
            firstSpell
                .Setup(value => value.Update(0.1d))
                .Callback(() =>
                {
                    RemovePendingSpell(entity, firstSpell.Object);
                    RemovePendingSpell(entity, removedSpell.Object);
                    AddPendingSpell(entity, addedSpell.Object);
                });
            addedSpell.SetupGet(value => value.IsFinished).Returns(false);
            AddPendingSpell(entity, firstSpell.Object);
            AddPendingSpell(entity, removedSpell.Object);

            entity.Update(0.1d);

            firstSpell.Verify(value => value.Update(0.1d), Times.Once);
            firstSpell.Verify(value => value.LateUpdate(It.IsAny<double>()), Times.Never);
            firstSpell.VerifyGet(value => value.IsFinished, Times.Never);
            removedSpell.Verify(value => value.Update(It.IsAny<double>()), Times.Never);
            addedSpell.Verify(value => value.Update(It.IsAny<double>()), Times.Never);

            entity.Update(0.2d);

            addedSpell.Verify(value => value.Update(0.2d), Times.Once);
            addedSpell.Verify(value => value.LateUpdate(0.2d), Times.Once);
            addedSpell.VerifyGet(value => value.IsFinished, Times.Once);
            threatManager.Verify(value => value.Update(It.IsAny<double>()), Times.Exactly(2));
        }

        [Fact]
        public void RemoveProc_CancelsOnlyExactProcAndPreservesOtherRegistrations()
        {
            TestUnitEntity entity = CreateEntity(1u);
            Mock<IProcInfo> removedProc = CreateProc(entity, ProcType.BeginMoving, 789u, 123u);
            Mock<IProcInfo> remainingProc = CreateProc(entity, ProcType.BeginMoving, 790u, 456u);
            entity.ApplyProc(removedProc.Object);
            entity.ApplyProc(remainingProc.Object);

            entity.RemoveProc(removedProc.Object);
            entity.FireProc(ProcType.BeginMoving);

            removedProc.Verify(p => p.Cancel(), Times.Once);
            removedProc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
            remainingProc.Verify(p => p.Cancel(), Times.Never);
            remainingProc.Verify(p => p.Trigger(null), Times.Once);
        }

        [Fact]
        public void FireProc_ThrowingCleanupDoesNotBlockSiblingAndFailedProcDoesNotReplay()
        {
            TestUnitEntity entity = CreateEntity(1u);
            Mock<IProcInfo> failedProc = CreateProc(entity, ProcType.BeginMoving, 789u, 123u);
            Mock<IProcInfo> healthyProc = CreateProc(entity, ProcType.BeginMoving, 790u, 456u);
            failedProc
                .Setup(p => p.Trigger(null))
                .Throws<InvalidOperationException>();
            failedProc
                .Setup(p => p.Cancel())
                .Throws<InvalidOperationException>();
            entity.ApplyProc(failedProc.Object);
            entity.ApplyProc(healthyProc.Object);

            entity.FireProc(ProcType.BeginMoving);
            entity.FireProc(ProcType.BeginMoving);

            failedProc.Verify(p => p.Trigger(null), Times.Once);
            failedProc.Verify(p => p.Cancel(), Times.Once);
            healthyProc.Verify(p => p.Trigger(null), Times.Exactly(2));
        }

        [Fact]
        public void FireProc_ReentrantRemovalSkipsRemovedSnapshotSibling()
        {
            TestUnitEntity entity = CreateEntity(1u);
            Mock<IProcInfo> firstProc = CreateProc(entity, ProcType.BeginMoving, 789u, 123u);
            Mock<IProcInfo> removedProc = CreateProc(entity, ProcType.BeginMoving, 790u, 456u);
            Mock<IProcInfo> addedProc = CreateProc(entity, ProcType.BeginMoving, 791u, 789u);
            firstProc
                .Setup(p => p.Trigger(null))
                .Callback(() =>
                {
                    entity.RemoveProc(removedProc.Object);
                    entity.ApplyProc(addedProc.Object);
                })
                .Returns(true);
            entity.ApplyProc(firstProc.Object);
            entity.ApplyProc(removedProc.Object);

            entity.FireProc(ProcType.BeginMoving);

            firstProc.Verify(p => p.Trigger(null), Times.Once);
            removedProc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
            removedProc.Verify(p => p.Cancel(), Times.Once);
            addedProc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);

            entity.FireProc(ProcType.BeginMoving);

            addedProc.Verify(p => p.Trigger(null), Times.Once);
        }

        [Fact]
        public void TakeDamage_FiresHitAndReceivedProcsWithOpposingEventTargets()
        {
            IServiceProvider previousProvider = LegacyServiceProvider.Provider;
            var entityManager = new EntityManager();
            typeof(EntityManager).GetMethod(
                    "InitialiseEntityStats",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(entityManager, null);
            using ServiceProvider serviceProvider = new ServiceCollection()
                .AddSingleton(entityManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;

            try
            {
                TestUnitEntity attacker = CreateEntity(1u);
                TestUnitEntity victim = CreateEntity(2u);
                attacker.MaxHealth = 100u;
                attacker.SetHealth(100u);
                victim.MaxHealth = 100u;
                victim.SetHealth(100u);

                Mock<IProcInfo> hitProc = CreateProc(attacker, ProcType.OnHit, 789u, 123u);
                Mock<IProcInfo> receivedProc = CreateProc(victim, ProcType.OnDamageReceived, 790u, 456u);
                attacker.ApplyProc(hitProc.Object);
                victim.ApplyProc(receivedProc.Object);

                var damage = new Mock<IDamageDescription>();
                damage.SetupGet(d => d.DamageType).Returns(DamageType.Physical);
                damage.SetupGet(d => d.RawDamage).Returns(10u);
                damage.SetupGet(d => d.AdjustedDamage).Returns(10u);

                victim.TakeDamage(attacker, damage.Object);

                hitProc.Verify(p => p.Trigger(victim), Times.Once);
                receivedProc.Verify(p => p.Trigger(attacker), Times.Once);
                Assert.Equal(90u, victim.Health);
            }
            finally
            {
                LegacyServiceProvider.Provider = previousProvider;
            }
        }

        [Fact]
        public void TakeDamage_ThrowingHitProcDoesNotBlockSiblingVictimProcOrDamage()
        {
            IServiceProvider previousProvider = LegacyServiceProvider.Provider;
            var entityManager = new EntityManager();
            typeof(EntityManager).GetMethod(
                    "InitialiseEntityStats",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(entityManager, null);
            using ServiceProvider serviceProvider = new ServiceCollection()
                .AddSingleton(entityManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;

            try
            {
                TestUnitEntity attacker = CreateEntity(1u);
                TestUnitEntity victim = CreateEntity(2u);
                attacker.MaxHealth = 100u;
                attacker.SetHealth(100u);
                victim.MaxHealth = 100u;
                victim.SetHealth(100u);

                Mock<IProcInfo> failedProc = CreateProc(attacker, ProcType.OnHit, 789u, 123u);
                Mock<IProcInfo> healthyProc = CreateProc(attacker, ProcType.OnHit, 790u, 456u);
                Mock<IProcInfo> receivedProc = CreateProc(victim, ProcType.OnDamageReceived, 791u, 789u);
                failedProc
                    .Setup(p => p.Trigger(victim))
                    .Throws<InvalidOperationException>();
                failedProc
                    .Setup(p => p.Cancel())
                    .Throws<InvalidOperationException>();
                attacker.ApplyProc(failedProc.Object);
                attacker.ApplyProc(healthyProc.Object);
                victim.ApplyProc(receivedProc.Object);

                var damage = new Mock<IDamageDescription>();
                damage.SetupGet(d => d.DamageType).Returns(DamageType.Physical);
                damage.SetupGet(d => d.RawDamage).Returns(10u);
                damage.SetupGet(d => d.AdjustedDamage).Returns(10u);

                victim.TakeDamage(attacker, damage.Object);
                attacker.FireProc(ProcType.OnHit, victim);

                failedProc.Verify(p => p.Trigger(victim), Times.Once);
                failedProc.Verify(p => p.Cancel(), Times.Once);
                healthyProc.Verify(p => p.Trigger(victim), Times.Exactly(2));
                receivedProc.Verify(p => p.Trigger(attacker), Times.Once);
                Assert.NotNull(victim.ThreatManager.GetHostile(attacker.Guid));
                Assert.Equal(90u, victim.Health);
            }
            finally
            {
                LegacyServiceProvider.Provider = previousProvider;
            }
        }

        [Fact]
        public void TakeDamage_ProcOriginDoesNotTriggerUnrelatedDamageProcs()
        {
            IServiceProvider previousProvider = LegacyServiceProvider.Provider;
            var entityManager = new EntityManager();
            typeof(EntityManager).GetMethod(
                    "InitialiseEntityStats",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(entityManager, null);
            using ServiceProvider serviceProvider = new ServiceCollection()
                .AddSingleton(entityManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;

            try
            {
                TestUnitEntity attacker = CreateEntity(1u);
                TestUnitEntity victim = CreateEntity(2u);
                attacker.MaxHealth = 100u;
                attacker.SetHealth(100u);
                victim.MaxHealth = 100u;
                victim.SetHealth(100u);

                Mock<IProcInfo> hitProc = CreateProc(attacker, ProcType.OnHit, 789u, 123u);
                Mock<IProcInfo> receivedProc = CreateProc(victim, ProcType.OnDamageReceived, 790u, 456u);
                attacker.ApplyProc(hitProc.Object);
                victim.ApplyProc(receivedProc.Object);

                var damage = new Mock<IDamageDescription>();
                damage.SetupGet(d => d.DamageType).Returns(DamageType.Physical);
                damage.SetupGet(d => d.RawDamage).Returns(10u);
                damage.SetupGet(d => d.AdjustedDamage).Returns(10u);

                victim.TakeDamage(attacker, damage.Object, false);

                hitProc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
                receivedProc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
                Assert.Equal(90u, victim.Health);
            }
            finally
            {
                LegacyServiceProvider.Provider = previousProvider;
            }
        }

        [Fact]
        public void Death_CancelsRegisteredProcsAndEndsPendingSpells()
        {
            IServiceProvider previousProvider = LegacyServiceProvider.Provider;
            var entityManager = new EntityManager();
            typeof(EntityManager).GetMethod(
                    "InitialiseEntityStats",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(entityManager, null);
            using ServiceProvider serviceProvider = new ServiceCollection()
                .AddSingleton(entityManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;

            try
            {
                TestUnitEntity entity = CreateEntity(1u);
                var threatManager = new Mock<IThreatManager>();
                threatManager
                    .Setup(t => t.GetEnumerator())
                    .Returns(() => Enumerable.Empty<IHostileEntity>().GetEnumerator());
                SetThreatManager(entity, threatManager.Object);
                Mock<IProcInfo> proc = CreateProc(entity, ProcType.CriticalDamage, 789u, 123u);
                var castingSpell = new Mock<ISpell>();
                castingSpell.Setup(s => s.IsCasting).Returns(true);
                var executingSpell = new Mock<ISpell>();
                AddPendingSpell(entity, castingSpell.Object);
                AddPendingSpell(entity, executingSpell.Object);
                entity.ApplyProc(proc.Object);

                entity.Die();
                entity.FireProc(ProcType.CriticalDamage);

                proc.Verify(p => p.Cancel(), Times.Once);
                proc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
                castingSpell.Verify(s => s.CancelCast(CastResult.CasterCannotBeDead), Times.Once);
                executingSpell.Verify(s => s.Finish(), Times.Once);
                threatManager.Verify(t => t.ClearThreatList(), Times.Once);
            }
            finally
            {
                LegacyServiceProvider.Provider = previousProvider;
            }
        }

        [Fact]
        public void Death_ThrowingProcCancellationDoesNotBlockSiblingOrThreatCleanup()
        {
            TestUnitEntity entity = CreateEntity(1u);
            var operations = new List<string>();
            var threatManager = new Mock<IThreatManager>();
            threatManager
                .Setup(t => t.GetEnumerator())
                .Returns(() => Enumerable.Empty<IHostileEntity>().GetEnumerator());
            threatManager
                .Setup(t => t.ClearThreatList())
                .Callback(() => operations.Add("threat-clear"));
            SetThreatManager(entity, threatManager.Object);
            Mock<IProcInfo> failedProc = CreateProc(entity, ProcType.CriticalDamage, 789u, 123u);
            Mock<IProcInfo> healthyProc = CreateProc(entity, ProcType.CriticalDamage, 790u, 456u);
            failedProc
                .Setup(p => p.Cancel())
                .Callback(() =>
                {
                    operations.Add("failed-cancel");
                    throw new InvalidOperationException("Test cancellation failure.");
                });
            healthyProc
                .Setup(p => p.Cancel())
                .Callback(() => operations.Add("healthy-cancel"));
            entity.ApplyProc(failedProc.Object);
            entity.ApplyProc(healthyProc.Object);

            entity.Die();
            entity.FireProc(ProcType.CriticalDamage);

            Assert.Equal(
                ["failed-cancel", "healthy-cancel", "threat-clear"],
                operations);
            failedProc.Verify(p => p.Cancel(), Times.Once);
            healthyProc.Verify(p => p.Cancel(), Times.Once);
            failedProc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
            healthyProc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
            threatManager.Verify(t => t.ClearThreatList(), Times.Once);
        }

        [Fact]
        public void Dispose_CancelsRegisteredProcsAndDisposesPendingSpells()
        {
            TestUnitEntity entity = CreateEntity(1u);
            var threatManager = new Mock<IThreatManager>();
            SetThreatManager(entity, threatManager.Object);
            Mock<IProcInfo> proc = CreateProc(entity, ProcType.CriticalDamage, 789u, 123u);
            var spell = new Mock<ISpell>();
            AddPendingSpell(entity, spell.Object);
            entity.ApplyProc(proc.Object);

            entity.Dispose();

            proc.Verify(p => p.Cancel(), Times.Once);
            spell.Verify(s => s.Finish(), Times.Once);
            spell.Verify(s => s.Dispose(), Times.Once);
            threatManager.Verify(t => t.ClearThreatList(), Times.Once);
        }

        [Fact]
        public void Dispose_ThrowingProcCancellationDoesNotBlockSiblingOrPendingSpellCleanup()
        {
            TestUnitEntity entity = CreateEntity(1u);
            var operations = new List<string>();
            var threatManager = new Mock<IThreatManager>();
            threatManager
                .Setup(t => t.ClearThreatList())
                .Callback(() => operations.Add("threat-clear"));
            SetThreatManager(entity, threatManager.Object);
            Mock<IProcInfo> failedProc = CreateProc(entity, ProcType.CriticalDamage, 789u, 123u);
            Mock<IProcInfo> healthyProc = CreateProc(entity, ProcType.CriticalDamage, 790u, 456u);
            failedProc
                .Setup(p => p.Cancel())
                .Callback(() =>
                {
                    operations.Add("failed-cancel");
                    throw new InvalidOperationException("Test cancellation failure.");
                });
            healthyProc
                .Setup(p => p.Cancel())
                .Callback(() => operations.Add("healthy-cancel"));
            var spell = new Mock<ISpell>();
            spell
                .Setup(s => s.Finish())
                .Callback(() => operations.Add("spell-finish"));
            spell
                .Setup(s => s.Dispose())
                .Callback(() => operations.Add("spell-dispose"));
            AddPendingSpell(entity, spell.Object);
            entity.ApplyProc(failedProc.Object);
            entity.ApplyProc(healthyProc.Object);

            Exception exception = Record.Exception(entity.Dispose);
            entity.FireProc(ProcType.CriticalDamage);

            Assert.Null(exception);
            Assert.Equal(
                ["threat-clear", "failed-cancel", "healthy-cancel", "spell-finish", "spell-dispose"],
                operations);
            failedProc.Verify(p => p.Cancel(), Times.Once);
            healthyProc.Verify(p => p.Cancel(), Times.Once);
            failedProc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
            healthyProc.Verify(p => p.Trigger(It.IsAny<IUnitEntity>()), Times.Never);
            spell.Verify(s => s.Finish(), Times.Once);
            spell.Verify(s => s.Dispose(), Times.Once);
            threatManager.Verify(t => t.ClearThreatList(), Times.Once);
        }

        [Fact]
        public void Dispose_ThrowingPendingSpellCleanupDoesNotBlockSiblingCleanupAndDoesNotRetry()
        {
            TestUnitEntity entity = CreateEntity(1u);
            var operations = new List<string>();
            var threatManager = new Mock<IThreatManager>();
            SetThreatManager(entity, threatManager.Object);
            var finishFailure = new Mock<ISpell>();
            var disposeFailure = new Mock<ISpell>();
            var healthySpell = new Mock<ISpell>();
            var reentrantSpell = new Mock<ISpell>();
            finishFailure
                .Setup(s => s.Finish())
                .Callback(() =>
                {
                    operations.Add("first-finish");
                    AddPendingSpell(entity, reentrantSpell.Object);
                    throw new InvalidOperationException("Test finish failure.");
                });
            finishFailure
                .Setup(s => s.Dispose())
                .Callback(() => operations.Add("first-dispose"));
            disposeFailure
                .Setup(s => s.Finish())
                .Callback(() => operations.Add("second-finish"));
            disposeFailure
                .Setup(s => s.Dispose())
                .Callback(() =>
                {
                    operations.Add("second-dispose");
                    throw new InvalidOperationException("Test dispose failure.");
                });
            healthySpell
                .Setup(s => s.Finish())
                .Callback(() => operations.Add("third-finish"));
            healthySpell
                .Setup(s => s.Dispose())
                .Callback(() => operations.Add("third-dispose"));
            AddPendingSpell(entity, finishFailure.Object);
            AddPendingSpell(entity, disposeFailure.Object);
            AddPendingSpell(entity, healthySpell.Object);

            Exception firstException = Record.Exception(entity.Dispose);
            ISpell activeSpell = entity.GetActiveSpell(_ => true);
            Exception secondException = Record.Exception(entity.Dispose);

            Assert.Null(firstException);
            Assert.Null(secondException);
            Assert.Null(activeSpell);
            Assert.Equal(
                ["first-finish", "first-dispose", "second-finish", "second-dispose", "third-finish", "third-dispose"],
                operations);
            finishFailure.Verify(s => s.Finish(), Times.Once);
            finishFailure.Verify(s => s.Dispose(), Times.Once);
            disposeFailure.Verify(s => s.Finish(), Times.Once);
            disposeFailure.Verify(s => s.Dispose(), Times.Once);
            healthySpell.Verify(s => s.Finish(), Times.Once);
            healthySpell.Verify(s => s.Dispose(), Times.Once);
            reentrantSpell.Verify(s => s.Finish(), Times.Never);
            reentrantSpell.Verify(s => s.Dispose(), Times.Never);
            threatManager.Verify(t => t.ClearThreatList(), Times.Exactly(2));
        }

        [Fact]
        public void ThreatRemoval_LeavesCombatAfterTwoUpdateTicks()
        {
            TestUnitEntity entity = CreateEntity(1u);
            TestUnitEntity target = CreateEntity(2u);
            entity.ThreatManager.UpdateThreat(target, 10);

            Assert.True(entity.InCombat);
            Assert.Equal(CombatState.Engaged, entity.CombatState);

            entity.ThreatManager.ClearThreatList();

            Assert.True(entity.InCombat);
            Assert.Equal(CombatState.Exiting, entity.CombatState);

            entity.Update(0.01d);

            Assert.True(entity.InCombat);
            Assert.Equal(CombatState.Exited, entity.CombatState);

            entity.Update(0.01d);

            Assert.False(entity.InCombat);
            Assert.Equal(CombatState.Free, entity.CombatState);
        }

        private static TestUnitEntity CreateEntity(uint guid)
        {
            var movementManager = new Mock<IMovementManager>();
            var entity = new TestUnitEntity(movementManager.Object);
            entity.SetGuid(guid);
            return entity;
        }

        private static Mock<IProcInfo> CreateProc(
            TestUnitEntity owner,
            ProcType type,
            uint effectId,
            uint applicatorSpell4Id)
        {
            var proc = new Mock<IProcInfo>();
            proc.Setup(p => p.Owner).Returns(owner);
            proc.Setup(p => p.EffectId).Returns(effectId);
            proc.Setup(p => p.Type).Returns(type);
            proc.Setup(p => p.ApplicatorSpell4Id).Returns(applicatorSpell4Id);
            proc.Setup(p => p.Trigger(It.IsAny<IUnitEntity>())).Returns(true);
            return proc;
        }

        private static void AddPendingSpell(TestUnitEntity entity, ISpell spell)
        {
            FieldInfo field = typeof(UnitEntity).GetField(
                "pendingSpells",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var spells = (List<ISpell>)field.GetValue(entity);
            spells.Add(spell);
        }

        private static void RemovePendingSpell(TestUnitEntity entity, ISpell spell)
        {
            FieldInfo field = typeof(UnitEntity).GetField(
                "pendingSpells",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var spells = (List<ISpell>)field.GetValue(entity);
            int index = spells.FindIndex(candidate => ReferenceEquals(candidate, spell));
            if (index >= 0)
                spells.RemoveAt(index);
        }

        private static void SetThreatManager(TestUnitEntity entity, IThreatManager threatManager)
        {
            PropertyInfo property = typeof(UnitEntity).GetProperty(nameof(UnitEntity.ThreatManager));
            property.SetValue(entity, threatManager);
        }

        public enum PendingSpellFailureStage
        {
            Update,
            LateUpdate,
            IsFinished
        }

        private sealed class TestUnitEntity : UnitEntity
        {
            public override EntityType Type => EntityType.NonPlayer;

            public TestUnitEntity(IMovementManager movementManager)
                : base(movementManager)
            {
            }

            public void SetGuid(uint guid)
            {
                Guid = guid;
            }

            public void SetHealth(uint health)
            {
                Health = health;
            }

            public void Die()
            {
                OnDeath();
            }

            protected override float CalculateDefaultProperty(Property property)
            {
                return 0f;
            }

            protected override IEntityModel BuildEntityModel()
            {
                return new NonPlayerEntityModel();
            }

            protected override void OnCombatStateChange(bool inCombat)
            {
            }
        }
    }
}
