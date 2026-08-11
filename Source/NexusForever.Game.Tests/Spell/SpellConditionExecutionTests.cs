using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.Script;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Spell
{
    [Collection(CombatServiceProviderCollection.Name)]
    public sealed class SpellConditionExecutionTests : IDisposable
    {
        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public SpellConditionExecutionTests()
        {
            previousProvider = LegacyServiceProvider.Provider;

            var scriptCollection = new Mock<IScriptCollection>();
            var scriptManager = new Mock<IScriptManager>();
            scriptManager.Setup(manager => manager.InitialiseOwnedScripts<ISpell>(
                    It.IsAny<ISpell>(),
                    It.IsAny<uint>()))
                .Returns(scriptCollection.Object);

            serviceProvider = new ServiceCollection()
                .AddSingleton(scriptManager.Object)
                .AddSingleton(new GlobalSpellManager())
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        [Fact]
        public void Cast_CasterCombatFailurePublishesOnlyResultBeforeCommitWork()
        {
            using var context = new CastContext(inCombat: true);
            Mock<ICharacterSpell> characterSpell = CreateCharacterSpell();
            ISpellInfo spellInfo = CreateSpellInfo(
                63684u,
                casterConditionId: 48u,
                casterCondition: Ordinary(48u, 2u, 0u),
                targetConditionId: 4u,
                targetCondition: Ordinary(4u, 0u, 0u),
                casterCCConditionId: 2u,
                casterCCCondition: CC(2u, 0u, 0u),
                targetCCConditionId: 2u,
                targetCCCondition: CC(2u, 0u, 0u),
                globalCooldown: 1500u);
            var spell = new TestSpell(
                context.Caster.Object,
                CreateParameters(spellInfo, characterSpell.Object));

            spell.Cast();
            spell.Update(0d);

            Assert.True(spell.IsFinishing);
            AssertFailurePacket(
                context.SessionPackets,
                63684u,
                CastResult.CasterCannotBeInCombat);
            Assert.Empty(context.VisiblePackets);
            Assert.Equal(0, spell.TargetSelectionCount);
            context.SpellManager.Verify(
                manager => manager.GetSpellCooldown(It.IsAny<uint>()),
                Times.Never);
            context.SpellManager.Verify(
                manager => manager.GetGlobalSpellCooldown(It.IsAny<uint>()),
                Times.Never);
            context.SpellManager.Verify(
                manager => manager.SetGlobalSpellCooldown(It.IsAny<uint>(), It.IsAny<double>()),
                Times.Never);
            context.SpellManager.Verify(
                manager => manager.SetSpellCooldown(It.IsAny<uint>(), It.IsAny<double>()),
                Times.Never);
            characterSpell.Verify(value => value.UseCharge(), Times.Never);
        }

        [Fact]
        public void Cast_DeadPrimaryTargetPublishesTargetCannotBeDeadBeforeStart()
        {
            using var context = new CastContext();
            var target = new Mock<IUnitEntity>();
            target.SetupGet(unit => unit.IsAlive).Returns(false);
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            ISpellInfo spellInfo = CreateSpellInfo(
                5747u,
                casterConditionId: 4u,
                casterCondition: Ordinary(4u, 0u, 0u),
                targetConditionId: 8u,
                targetCondition: Ordinary(8u, 1u, 0u));
            var spell = new TestSpell(
                context.Caster.Object,
                CreateParameters(spellInfo, primaryTargetId: 42u));

            spell.Cast();

            Assert.True(spell.IsFinishing);
            AssertFailurePacket(
                context.SessionPackets,
                5747u,
                CastResult.TargetCannotBeDead);
            Assert.Empty(context.VisiblePackets);
            Assert.Equal(0, spell.TargetSelectionCount);
            target.VerifyGet(unit => unit.IsAlive, Times.Once);
        }

        [Fact]
        public void Cast_UnresolvedPrimaryTargetPublishesNoTargetBeforeStart()
        {
            using var context = new CastContext();
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns((IWorldEntity)null);
            ISpellInfo spellInfo = CreateSpellInfo(
                80305u,
                targetConditionId: 8u,
                targetCondition: Ordinary(8u, 1u, 0u));
            var spell = new TestSpell(
                context.Caster.Object,
                CreateParameters(spellInfo, primaryTargetId: 42u));

            spell.Cast();

            Assert.True(spell.IsFinishing);
            AssertFailurePacket(context.SessionPackets, 80305u, CastResult.NoTarget);
            Assert.Empty(context.VisiblePackets);
            Assert.Equal(0, spell.TargetSelectionCount);
            context.Caster.Verify(
                unit => unit.GetVisible<IWorldEntity>(42u),
                Times.Once);
        }

        [Fact]
        public void Cast_UnsupportedCompanionConditionPreservesWholeSpellLegacyPath()
        {
            using var context = new CastContext(inCombat: true);
            ISpellInfo spellInfo = CreateSpellInfo(
                123u,
                casterConditionId: 48u,
                casterCondition: Ordinary(48u, 2u, 0u),
                targetConditionId: 15u,
                targetCondition: Ordinary(15u, 129u, 0u),
                casterCCConditionId: 2u,
                casterCCCondition: CC(2u, 0u, 0u));
            var spell = new TestSpell(
                context.Caster.Object,
                CreateParameters(spellInfo));

            spell.Cast();

            Assert.True(spell.IsCasting);
            Assert.Empty(context.SessionPackets);
            ServerSpellStart start = Assert.IsType<ServerSpellStart>(
                Assert.Single(context.VisiblePackets));
            Assert.Equal(123u, start.Spell4Id);
            context.Caster.VerifyGet(unit => unit.InCombat, Times.Never);
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        private static void AssertFailurePacket(
            List<IWritable> packets,
            uint spell4Id,
            CastResult castResult)
        {
            ServerSpellCastResult result = Assert.IsType<ServerSpellCastResult>(
                Assert.Single(packets));
            Assert.Equal(0u, result.Unknown0);
            Assert.Equal(spell4Id, result.Spell4Id);
            Assert.Equal(castResult, result.CastResult);
        }

        private static Mock<ICharacterSpell> CreateCharacterSpell()
        {
            var characterSpell = new Mock<ICharacterSpell>();
            characterSpell.SetupGet(value => value.MaxAbilityCharges).Returns(1u);
            characterSpell.SetupGet(value => value.AbilityCharges).Returns(1u);
            return characterSpell;
        }

        private static SpellParameters CreateParameters(
            ISpellInfo spellInfo,
            ICharacterSpell characterSpell = null,
            uint primaryTargetId = 0u)
        {
            return new SpellParameters
            {
                SpellInfo       = spellInfo,
                CharacterSpell  = characterSpell,
                PrimaryTargetId = primaryTargetId
            };
        }

        private static ISpellInfo CreateSpellInfo(
            uint spellId,
            uint casterConditionId = 0u,
            Spell4ConditionsEntry casterCondition = null,
            uint targetConditionId = 0u,
            Spell4ConditionsEntry targetCondition = null,
            uint casterCCConditionId = 0u,
            Spell4CCConditionsEntry casterCCCondition = null,
            uint targetCCConditionId = 0u,
            Spell4CCConditionsEntry targetCCCondition = null,
            uint globalCooldown = 0u)
        {
            var info = new Mock<ISpellInfo>();
            info.SetupGet(value => value.Entry).Returns(new Spell4Entry
            {
                Id                           = spellId,
                GlobalCooldownEnum           = globalCooldown == 0u ? 0u : 3u,
                Spell4ConditionsIdCaster     = casterConditionId,
                Spell4ConditionsIdTarget     = targetConditionId,
                Spell4CCConditionsIdCaster   = casterCCConditionId,
                Spell4CCConditionsIdTarget   = targetCCConditionId
            });
            info.SetupGet(value => value.CasterConditions).Returns(casterCondition);
            info.SetupGet(value => value.TargetConditions).Returns(targetCondition);
            info.SetupGet(value => value.CasterCCConditions).Returns(casterCCCondition);
            info.SetupGet(value => value.TargetCCConditions).Returns(targetCCCondition);
            info.SetupGet(value => value.GlobalCooldown).Returns(globalCooldown == 0u
                ? null
                : new SpellCoolDownEntry { CooldownTime = globalCooldown });
            info.SetupGet(value => value.Effects).Returns([]);
            info.SetupGet(value => value.Telegraphs).Returns([]);
            info.SetupGet(value => value.PrerequisiteRunners).Returns([]);
            return info.Object;
        }

        private static Spell4ConditionsEntry Ordinary(uint id, uint mask, uint value)
        {
            return new Spell4ConditionsEntry
            {
                Id             = id,
                ConditionMask  = mask,
                ConditionValue = value
            };
        }

        private static Spell4CCConditionsEntry CC(uint id, uint mask, uint required)
        {
            return new Spell4CCConditionsEntry
            {
                Id                   = id,
                CcStateMask          = mask,
                CcStateFlagsRequired = required
            };
        }

        private sealed class CastContext : IDisposable
        {
            public Mock<IPlayer> Caster { get; } = new();
            public Mock<ISpellManager> SpellManager { get; } = new();
            public List<IWritable> SessionPackets { get; } = [];
            public List<IWritable> VisiblePackets { get; } = [];

            private readonly Mock<IGameSession> session = new();

            public CastContext(bool inCombat = false)
            {
                Caster.SetupGet(unit => unit.Guid).Returns(7u);
                Caster.SetupGet(unit => unit.IsAlive).Returns(true);
                Caster.SetupGet(unit => unit.InCombat).Returns(inCombat);
                Caster.SetupGet(unit => unit.IsLoading).Returns(false);
                Caster.SetupGet(unit => unit.Session).Returns(session.Object);
                Caster.SetupGet(unit => unit.SpellManager).Returns(SpellManager.Object);
                Caster.Setup(unit => unit.EnqueueToVisible(
                        It.IsAny<IWritable>(),
                        It.IsAny<bool>()))
                    .Callback<IWritable, bool>((packet, _) => VisiblePackets.Add(packet));
                session.Setup(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                    .Callback<IWritable>(SessionPackets.Add);
            }

            public void Dispose()
            {
            }
        }

        private sealed class TestSpell : NexusForever.Game.Spell.Spell
        {
            public int TargetSelectionCount { get; private set; }

            public TestSpell(IUnitEntity caster, ISpellParameters parameters)
                : base(caster, parameters)
            {
            }

            protected override void SelectTargets()
            {
                TargetSelectionCount++;
            }
        }
    }
}
