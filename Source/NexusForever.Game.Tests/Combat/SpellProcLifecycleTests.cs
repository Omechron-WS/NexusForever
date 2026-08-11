using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell;
using NexusForever.Game.Spell.SpellType;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.Script;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Combat
{
    [Collection(CombatServiceProviderCollection.Name)]
    public sealed class SpellProcLifecycleTests : IDisposable
    {
        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public SpellProcLifecycleTests()
        {
            previousProvider = LegacyServiceProvider.Provider;

            var scriptCollection = new Mock<IScriptCollection>();
            var scriptManager = new Mock<IScriptManager>();
            scriptManager.Setup(manager => manager.InitialiseOwnedScripts<ISpell>(
                    It.IsAny<ISpell>(), It.IsAny<uint>()))
                .Returns(scriptCollection.Object);

            serviceProvider = new ServiceCollection()
                .AddSingleton(scriptManager.Object)
                .AddSingleton(new GlobalSpellManager())
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        [Fact]
        public void LateUpdate_NormalInstantSpellRemovesAppliedProc()
        {
            var target = new Mock<IUnitEntity>();
            Mock<IProcInfo> proc = CreateProc(target.Object);
            TestSpell spell = CreateSpell();
            spell.TrackProc(target.Object, proc.Object);
            spell.SetStatus(SpellStatus.Executing);

            spell.LateUpdate(0d);

            Assert.True(spell.IsFinished);
            target.Verify(unit => unit.RemoveProc(proc.Object), Times.Once);
        }

        [Fact]
        public void Finish_RemovesEveryTrackedProcAndPreservesUnrelatedProc()
        {
            var firstTarget = new Mock<IUnitEntity>();
            var secondTarget = new Mock<IUnitEntity>();
            Mock<IProcInfo> firstProc = CreateProc(firstTarget.Object);
            Mock<IProcInfo> secondProc = CreateProc(secondTarget.Object);
            Mock<IProcInfo> unrelatedProc = CreateProc(firstTarget.Object);
            TestSpell spell = CreateSpell();
            spell.TrackProc(firstTarget.Object, firstProc.Object);
            spell.TrackProc(secondTarget.Object, secondProc.Object);

            spell.Finish();
            spell.Finish();

            firstTarget.Verify(unit => unit.RemoveProc(firstProc.Object), Times.Once);
            secondTarget.Verify(unit => unit.RemoveProc(secondProc.Object), Times.Once);
            firstTarget.Verify(unit => unit.RemoveProc(unrelatedProc.Object), Times.Never);
        }

        [Fact]
        public void CancelCast_RemovesTrackedProcImmediately()
        {
            var target = new Mock<IUnitEntity>();
            Mock<IProcInfo> proc = CreateProc(target.Object);
            TestSpell spell = CreateSpell();
            spell.TrackProc(target.Object, proc.Object);
            spell.SetStatus(SpellStatus.Casting);

            spell.CancelCast(CastResult.SpellCancelled);

            target.Verify(unit => unit.RemoveProc(proc.Object), Times.Once);
        }

        [Fact]
        public void Dispose_RemovesTrackedProcIdempotently()
        {
            var target = new Mock<IUnitEntity>();
            Mock<IProcInfo> proc = CreateProc(target.Object);
            TestSpell spell = CreateSpell();
            spell.TrackProc(target.Object, proc.Object);

            spell.Dispose();
            spell.Dispose();

            target.Verify(unit => unit.RemoveProc(proc.Object), Times.Once);
        }

        [Fact]
        public void AuraTargetExit_RemovesOnlyExitedTargetsProc()
        {
            var caster = new Mock<IUnitEntity>();
            var exitedTarget = new Mock<IUnitEntity>();
            var remainingTarget = new Mock<IUnitEntity>();
            exitedTarget.SetupGet(unit => unit.Guid).Returns(2u);
            remainingTarget.SetupGet(unit => unit.Guid).Returns(3u);
            Mock<IProcInfo> exitedProc = CreateProc(exitedTarget.Object);
            Mock<IProcInfo> remainingProc = CreateProc(remainingTarget.Object);
            var spell = new TestSpellAura(caster.Object, CreateParameters());
            spell.AddTarget(exitedTarget.Object);
            spell.TrackProc(exitedTarget.Object, exitedProc.Object);
            spell.TrackProc(remainingTarget.Object, remainingProc.Object);
            spell.SetStatus(SpellStatus.Executing);

            spell.Update(0.1d);

            exitedTarget.Verify(unit => unit.RemoveProc(exitedProc.Object), Times.Once);
            remainingTarget.Verify(unit => unit.RemoveProc(remainingProc.Object), Times.Never);
        }

        [Fact]
        public void SpellTargetSelection_WithoutPrimaryTarget_UsesCasterAsTargetFallback()
        {
            var caster = new Mock<IUnitEntity>();
            var spell = new TestSpell(caster.Object, CreateParameters());

            IReadOnlyList<ISpellTargetInfo> targets = spell.SelectTargetsForTest();

            ISpellTargetInfo target = Assert.Single(targets);
            Assert.Same(caster.Object, target.Entity);
            Assert.Equal(
                SpellEffectTargetFlags.Caster | SpellEffectTargetFlags.Target,
                target.Flags);
        }

        [Fact]
        public void AuraTargetSelection_WithoutPrimaryTarget_UsesCasterAsTargetFallback()
        {
            var caster = new Mock<IUnitEntity>();
            var spell = new TestSpellAura(caster.Object, CreateParameters());

            IReadOnlyList<ISpellTargetInfo> targets = spell.SelectTargetsForTest();

            ISpellTargetInfo target = Assert.Single(targets);
            Assert.Same(caster.Object, target.Entity);
            Assert.Equal(
                SpellEffectTargetFlags.Caster | SpellEffectTargetFlags.Target,
                target.Flags);
        }

        /// <summary>
        /// Restore the shared service provider used before this fixture.
        /// </summary>
        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        private static TestSpell CreateSpell()
        {
            return new TestSpell(new Mock<IUnitEntity>().Object, CreateParameters());
        }

        private static SpellParameters CreateParameters()
        {
            var info = new Mock<ISpellInfo>();
            info.SetupGet(spell => spell.Entry).Returns(new Spell4Entry
            {
                Id = 123u
            });
            info.SetupGet(spell => spell.Effects).Returns([]);
            info.SetupGet(spell => spell.Telegraphs).Returns([]);
            return new SpellParameters
            {
                SpellInfo = info.Object
            };
        }

        private static Mock<IProcInfo> CreateProc(IUnitEntity owner)
        {
            var proc = new Mock<IProcInfo>();
            proc.SetupGet(info => info.Owner).Returns(owner);
            return proc;
        }

        private sealed class TestSpell : NexusForever.Game.Spell.Spell
        {
            public TestSpell(IUnitEntity caster, ISpellParameters parameters)
                : base(caster, parameters)
            {
            }

            public void SetStatus(SpellStatus value)
            {
                status = value;
            }

            public IReadOnlyList<ISpellTargetInfo> SelectTargetsForTest()
            {
                SelectTargets();
                return targets;
            }
        }

        private sealed class TestSpellAura : SpellAura
        {
            public TestSpellAura(IUnitEntity caster, ISpellParameters parameters)
                : base(caster, parameters)
            {
            }

            public void AddTarget(IUnitEntity target)
            {
                targets.Add(new SpellTargetInfo(SpellEffectTargetFlags.Telegraph, target));
            }

            public void SetStatus(SpellStatus value)
            {
                status = value;
            }

            public IReadOnlyList<ISpellTargetInfo> SelectTargetsForTest()
            {
                SelectTargets();
                return targets;
            }
        }
    }
}
