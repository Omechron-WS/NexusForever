using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;
using Moq;

namespace NexusForever.Game.Tests.Spell
{
    public sealed class SpellConditionPolicyTests
    {
        [Theory]
        [InlineData(false, CastResult.Ok)]
        [InlineData(true, CastResult.CasterCannotBeInCombat)]
        public void TryCheck_CasterCombatConditionUsesAuthoritativeState(
            bool inCombat,
            CastResult expected)
        {
            var caster = new Mock<IUnitEntity>();
            caster.SetupGet(unit => unit.InCombat).Returns(inCombat);
            ISpellInfo spellInfo = CreateSpellInfo(
                casterConditionId: 48u,
                casterCondition: Ordinary(48u, 2u, 0u),
                targetConditionId: 4u,
                targetCondition: Ordinary(4u, 0u, 0u),
                casterCCConditionId: 2u,
                casterCCCondition: CC(2u, 0u, 0u),
                targetCCConditionId: 2u,
                targetCCCondition: CC(2u, 0u, 0u));

            bool supported = SpellConditionPolicy.TryCheck(
                caster.Object,
                spellInfo,
                0u,
                out CastResult result);

            Assert.True(supported);
            Assert.Equal(expected, result);
            caster.VerifyGet(unit => unit.InCombat, Times.Once);
            caster.VerifyGet(unit => unit.IsAlive, Times.Never);
            caster.Verify(
                unit => unit.GetVisible<IWorldEntity>(It.IsAny<uint>()),
                Times.Never);
        }

        [Theory]
        [InlineData(3u, true, CastResult.Ok)]
        [InlineData(3u, false, CastResult.TargetCannotBeDead)]
        [InlineData(8u, true, CastResult.Ok)]
        [InlineData(8u, false, CastResult.TargetCannotBeDead)]
        public void TryCheck_TargetDeadConditionUsesResolvedPrimaryUnit(
            uint conditionId,
            bool targetAlive,
            CastResult expected)
        {
            var target = new Mock<IUnitEntity>();
            target.SetupGet(unit => unit.IsAlive).Returns(targetAlive);
            var caster = new Mock<IUnitEntity>();
            caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u)).Returns(target.Object);
            ISpellInfo spellInfo = CreateSpellInfo(
                casterConditionId: 4u,
                casterCondition: Ordinary(4u, 0u, 0u),
                targetConditionId: conditionId,
                targetCondition: Ordinary(conditionId, 1u, 0u));

            bool supported = SpellConditionPolicy.TryCheck(
                caster.Object,
                spellInfo,
                42u,
                out CastResult result);

            Assert.True(supported);
            Assert.Equal(expected, result);
            caster.VerifyGet(unit => unit.InCombat, Times.Never);
            caster.Verify(unit => unit.GetVisible<IWorldEntity>(42u), Times.Once);
            target.VerifyGet(unit => unit.IsAlive, Times.Once);
        }

        [Fact]
        public void TryCheck_ZeroPrimaryTargetUsesCasterAsTarget()
        {
            var caster = new Mock<IUnitEntity>();
            caster.SetupGet(unit => unit.IsAlive).Returns(false);
            ISpellInfo spellInfo = CreateSpellInfo(
                targetConditionId: 8u,
                targetCondition: Ordinary(8u, 1u, 0u));

            bool supported = SpellConditionPolicy.TryCheck(
                caster.Object,
                spellInfo,
                0u,
                out CastResult result);

            Assert.True(supported);
            Assert.Equal(CastResult.TargetCannotBeDead, result);
            caster.VerifyGet(unit => unit.IsAlive, Times.Once);
            caster.Verify(
                unit => unit.GetVisible<IWorldEntity>(It.IsAny<uint>()),
                Times.Never);
        }

        [Fact]
        public void TryCheck_UnresolvedNonzeroPrimaryTargetReturnsNoTarget()
        {
            var caster = new Mock<IUnitEntity>();
            caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns((IWorldEntity)null);
            ISpellInfo spellInfo = CreateSpellInfo(
                targetConditionId: 8u,
                targetCondition: Ordinary(8u, 1u, 0u));

            bool supported = SpellConditionPolicy.TryCheck(
                caster.Object,
                spellInfo,
                42u,
                out CastResult result);

            Assert.True(supported);
            Assert.Equal(CastResult.NoTarget, result);
            caster.Verify(unit => unit.GetVisible<IWorldEntity>(42u), Times.Once);
        }

        [Fact]
        public void TryCheck_CasterFailureWinsBeforeTargetResolution()
        {
            var caster = new Mock<IUnitEntity>();
            caster.SetupGet(unit => unit.InCombat).Returns(true);
            ISpellInfo spellInfo = CreateSpellInfo(
                casterConditionId: 48u,
                casterCondition: Ordinary(48u, 2u, 0u),
                targetConditionId: 8u,
                targetCondition: Ordinary(8u, 1u, 0u));

            bool supported = SpellConditionPolicy.TryCheck(
                caster.Object,
                spellInfo,
                42u,
                out CastResult result);

            Assert.True(supported);
            Assert.Equal(CastResult.CasterCannotBeInCombat, result);
            caster.VerifyGet(unit => unit.InCombat, Times.Once);
            caster.Verify(
                unit => unit.GetVisible<IWorldEntity>(It.IsAny<uint>()),
                Times.Never);
        }

        [Fact]
        public void TryCheck_UnsupportedTuplePreservesLegacyWithoutUnitStateReads()
        {
            (string Name, ISpellInfo SpellInfo)[] cases =
            [
                ("caster dead", CreateSpellInfo(
                    casterConditionId: 3u,
                    casterCondition: Ordinary(3u, 1u, 0u))),
                ("caster movement", CreateSpellInfo(
                    casterConditionId: 29u,
                    casterCondition: Ordinary(29u, 41u, 40u))),
                ("caster sprint", CreateSpellInfo(
                    casterConditionId: 28u,
                    casterCondition: Ordinary(28u, 1024u, 0u))),
                ("target taxi", CreateSpellInfo(
                    targetConditionId: 15u,
                    targetCondition: Ordinary(15u, 129u, 0u))),
                ("target movement", CreateSpellInfo(
                    targetConditionId: 29u,
                    targetCondition: Ordinary(29u, 41u, 40u))),
                ("caster CC", CreateSpellInfo(
                    casterConditionId: 48u,
                    casterCondition: Ordinary(48u, 2u, 0u),
                    casterCCConditionId: 13u,
                    casterCCCondition: CC(13u, 512u, 0u))),
                ("target CC", CreateSpellInfo(
                    targetConditionId: 8u,
                    targetCondition: Ordinary(8u, 1u, 0u),
                    targetCCConditionId: 13u,
                    targetCCCondition: CC(13u, 512u, 0u))),
                ("malformed CC no-op", CreateSpellInfo(
                    casterConditionId: 48u,
                    casterCondition: Ordinary(48u, 2u, 0u),
                    casterCCConditionId: 2u,
                    casterCCCondition: CC(2u, 0u, 1u))),
                ("malformed caster row", CreateSpellInfo(
                    casterConditionId: 48u,
                    casterCondition: Ordinary(48u, 2u, 2u))),
                ("missing target row", CreateSpellInfo(targetConditionId: 8u)),
                ("mismatched target row identity", CreateSpellInfo(
                    targetConditionId: 8u,
                    targetCondition: Ordinary(3u, 1u, 0u))),
                ("row present for zero FK", CreateSpellInfo(
                    casterCondition: Ordinary(4u, 0u, 0u),
                    targetConditionId: 8u,
                    targetCondition: Ordinary(8u, 1u, 0u))),
                ("supported caster plus unsupported target", CreateSpellInfo(
                    casterConditionId: 48u,
                    casterCondition: Ordinary(48u, 2u, 0u),
                    targetConditionId: 15u,
                    targetCondition: Ordinary(15u, 129u, 0u))),
                ("no active condition", CreateSpellInfo(
                    casterConditionId: 4u,
                    casterCondition: Ordinary(4u, 0u, 0u),
                    targetConditionId: 4u,
                    targetCondition: Ordinary(4u, 0u, 0u),
                    casterCCConditionId: 2u,
                    casterCCCondition: CC(2u, 0u, 0u),
                    targetCCConditionId: 2u,
                    targetCCCondition: CC(2u, 0u, 0u)))
            ];

            foreach ((string name, ISpellInfo spellInfo) in cases)
            {
                var caster = new Mock<IUnitEntity>(MockBehavior.Strict);

                bool supported = SpellConditionPolicy.TryCheck(
                    caster.Object,
                    spellInfo,
                    42u,
                    out CastResult result);

                Assert.True(!supported, $"Expected unsupported condition tuple: {name}.");
                Assert.Equal(CastResult.Ok, result);
                caster.VerifyNoOtherCalls();
            }
        }

        [Theory]
        [InlineData(1316u, 1u, 225u)]
        [InlineData(56476u, 3u, 1u)]
        [InlineData(56621u, 3u, 1u)]
        public void TryCheck_Build16042SprintSpellTuplesRemainUnsupported(
            uint spellId,
            uint casterConditionId,
            uint casterMask)
        {
            var caster = new Mock<IUnitEntity>(MockBehavior.Strict);
            ISpellInfo spellInfo = CreateSpellInfo(
                spellId,
                casterConditionId,
                Ordinary(casterConditionId, casterMask, 0u),
                15u,
                Ordinary(15u, 129u, 0u),
                2u,
                CC(2u, 0u, 0u));

            bool supported = SpellConditionPolicy.TryCheck(
                caster.Object,
                spellInfo,
                42u,
                out CastResult result);

            Assert.False(supported);
            Assert.Equal(CastResult.Ok, result);
            caster.VerifyNoOtherCalls();
        }

        private static ISpellInfo CreateSpellInfo(
            uint spellId = 123u,
            uint casterConditionId = 0u,
            Spell4ConditionsEntry casterCondition = null,
            uint targetConditionId = 0u,
            Spell4ConditionsEntry targetCondition = null,
            uint casterCCConditionId = 0u,
            Spell4CCConditionsEntry casterCCCondition = null,
            uint targetCCConditionId = 0u,
            Spell4CCConditionsEntry targetCCCondition = null)
        {
            var info = new Mock<ISpellInfo>();
            info.SetupGet(value => value.Entry).Returns(new Spell4Entry
            {
                Id                           = spellId,
                Spell4ConditionsIdCaster     = casterConditionId,
                Spell4ConditionsIdTarget     = targetConditionId,
                Spell4CCConditionsIdCaster   = casterCCConditionId,
                Spell4CCConditionsIdTarget   = targetCCConditionId
            });
            info.SetupGet(value => value.CasterConditions).Returns(casterCondition);
            info.SetupGet(value => value.TargetConditions).Returns(targetCondition);
            info.SetupGet(value => value.CasterCCConditions).Returns(casterCCCondition);
            info.SetupGet(value => value.TargetCCConditions).Returns(targetCCCondition);
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
    }
}
