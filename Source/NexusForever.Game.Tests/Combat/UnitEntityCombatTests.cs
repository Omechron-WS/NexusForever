using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Combat;
using NexusForever.Game.Static.Entity;
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
        public void ApplyProc_DuplicateApplicatorForEventType_IsRejected()
        {
            TestUnitEntity entity = CreateEntity(1u);
            Mock<IProcInfo> first = CreateProc(entity, ProcType.CriticalDamage, 123u);
            Mock<IProcInfo> duplicate = CreateProc(entity, ProcType.CriticalDamage, 123u);

            bool firstApplied = entity.ApplyProc(first.Object);
            bool duplicateApplied = entity.ApplyProc(duplicate.Object);
            entity.FireProc(ProcType.CriticalDamage);

            Assert.True(firstApplied);
            Assert.False(duplicateApplied);
            first.Verify(p => p.Trigger(), Times.Once);
            duplicate.Verify(p => p.Trigger(), Times.Never);
        }

        [Fact]
        public void Update_AdvancesRegisteredProcsAndRemovalStopsDispatch()
        {
            TestUnitEntity entity = CreateEntity(1u);
            Mock<IProcInfo> proc = CreateProc(entity, ProcType.BeginMoving, 123u);
            entity.ApplyProc(proc.Object);

            entity.Update(0.1d);
            bool removed = entity.RemoveProc(proc.Object);
            entity.FireProc(ProcType.BeginMoving);

            Assert.True(removed);
            proc.Verify(p => p.Update(0.1d), Times.Once);
            proc.Verify(p => p.Trigger(), Times.Never);
            proc.Verify(p => p.Cancel(), Times.Once);
        }

        [Fact]
        public void RemoveProc_CancelsOnlyExactProcAndPreservesOtherRegistrations()
        {
            TestUnitEntity entity = CreateEntity(1u);
            Mock<IProcInfo> removedProc = CreateProc(entity, ProcType.BeginMoving, 123u);
            Mock<IProcInfo> remainingProc = CreateProc(entity, ProcType.BeginMoving, 456u);
            entity.ApplyProc(removedProc.Object);
            entity.ApplyProc(remainingProc.Object);

            entity.RemoveProc(removedProc.Object);
            entity.FireProc(ProcType.BeginMoving);

            removedProc.Verify(p => p.Cancel(), Times.Once);
            removedProc.Verify(p => p.Trigger(), Times.Never);
            remainingProc.Verify(p => p.Cancel(), Times.Never);
            remainingProc.Verify(p => p.Trigger(), Times.Once);
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
                Mock<IProcInfo> proc = CreateProc(entity, ProcType.CriticalDamage, 123u);
                var castingSpell = new Mock<ISpell>();
                castingSpell.Setup(s => s.IsCasting).Returns(true);
                var executingSpell = new Mock<ISpell>();
                AddPendingSpell(entity, castingSpell.Object);
                AddPendingSpell(entity, executingSpell.Object);
                entity.ApplyProc(proc.Object);

                entity.Die();
                entity.FireProc(ProcType.CriticalDamage);

                proc.Verify(p => p.Cancel(), Times.Once);
                proc.Verify(p => p.Trigger(), Times.Never);
                castingSpell.Verify(s => s.CancelCast(CastResult.CasterCannotBeDead), Times.Once);
                executingSpell.Verify(s => s.Finish(), Times.Once);
            }
            finally
            {
                LegacyServiceProvider.Provider = previousProvider;
            }
        }

        [Fact]
        public void Dispose_CancelsRegisteredProcsAndDisposesPendingSpells()
        {
            TestUnitEntity entity = CreateEntity(1u);
            Mock<IProcInfo> proc = CreateProc(entity, ProcType.CriticalDamage, 123u);
            var spell = new Mock<ISpell>();
            AddPendingSpell(entity, spell.Object);
            entity.ApplyProc(proc.Object);

            entity.Dispose();

            proc.Verify(p => p.Cancel(), Times.Once);
            spell.Verify(s => s.Finish(), Times.Once);
            spell.Verify(s => s.Dispose(), Times.Once);
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
            uint applicatorSpell4Id)
        {
            var proc = new Mock<IProcInfo>();
            proc.Setup(p => p.Owner).Returns(owner);
            proc.Setup(p => p.Type).Returns(type);
            proc.Setup(p => p.ApplicatorSpell4Id).Returns(applicatorSpell4Id);
            proc.Setup(p => p.Trigger()).Returns(true);
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

            public void Die()
            {
                OnDeath();
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
