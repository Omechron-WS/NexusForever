using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;
using Moq;

namespace NexusForever.Game.Tests.Spell
{
    public class SpellVitalPolicyTests
    {
        [Theory]
        [InlineData(0u, 50f, 100f, 50u, true)]
        [InlineData(1u, 50f, 100f, 50u, false)]
        [InlineData(2u, 50f, 100f, 50u, true)]
        [InlineData(3u, 50f, 100f, 50u, false)]
        [InlineData(4u, 50f, 100f, 50u, true)]
        [InlineData(5u, 50f, 100f, 50u, false)]
        [InlineData(6u, 50f, 100f, 50u, true)]
        [InlineData(7u, 50f, 100f, 50u, false)]
        [InlineData(8u, 50f, 100f, 50u, true)]
        [InlineData(9u, 50f, 100f, 50u, false)]
        public void TryMeetsRequirement_DecodesBuild16042Evaluation(
            uint evaluation,
            float current,
            float maximum,
            uint requirement,
            bool expected)
        {
            Mock<IUnitEntity> entity = CreateEntity(
                new Dictionary<Vital, float> { [Vital.Health] = current },
                new Dictionary<Vital, float> { [Vital.Health] = maximum });

            bool supported = SpellVitalPolicy.TryMeetsRequirement(
                entity.Object,
                (uint)Vital.Health,
                requirement,
                evaluation,
                out bool meets);

            Assert.True(supported);
            Assert.Equal(expected, meets);
        }

        [Theory]
        [InlineData(0u, 49f, 50u, false)]
        [InlineData(1u, 49f, 50u, true)]
        [InlineData(2u, 49f, 50u, false)]
        [InlineData(3u, 51f, 50u, true)]
        [InlineData(4u, 51f, 50u, false)]
        [InlineData(5u, 49f, 50u, true)]
        public void TryMeetsRequirement_UsesAbsoluteValueForEvaluationZeroThroughFive(
            uint evaluation,
            float current,
            uint requirement,
            bool expected)
        {
            Mock<IUnitEntity> entity = CreateEntity(
                new Dictionary<Vital, float> { [Vital.Resource1] = current });

            bool supported = SpellVitalPolicy.TryMeetsRequirement(
                entity.Object,
                (uint)Vital.Resource1,
                requirement,
                evaluation,
                out bool meets);

            Assert.True(supported);
            Assert.Equal(expected, meets);
        }

        [Fact]
        public void TryMeetsRequirement_PercentageOnUnboundedVitalFailsClosed()
        {
            Mock<IUnitEntity> entity = CreateEntity(
                new Dictionary<Vital, float> { [Vital.InterruptArmor] = 10f });

            bool supported = SpellVitalPolicy.TryMeetsRequirement(
                entity.Object,
                (uint)Vital.InterruptArmor,
                50u,
                6u,
                out bool meets);

            Assert.False(supported);
            Assert.False(meets);
        }

        [Theory]
        [InlineData((uint)Vital.Breath, 2u)]
        [InlineData((uint)Vital.Health, 10u)]
        public void TryMeetsRequirement_UnknownVitalOrEvaluationFailsClosed(uint vital, uint evaluation)
        {
            Mock<IUnitEntity> entity = CreateEntity(
                new Dictionary<Vital, float> { [Vital.Health] = 100f },
                new Dictionary<Vital, float> { [Vital.Health] = 100f });

            bool supported = SpellVitalPolicy.TryMeetsRequirement(
                entity.Object,
                vital,
                50u,
                evaluation,
                out bool meets);

            Assert.False(supported);
            Assert.False(meets);
        }

        [Fact]
        public void CheckCosts_AliasCollisionAggregatesBeforeValidationWithoutMutation()
        {
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IUnitEntity> entity = CreateEntity(
                new Dictionary<Vital, float> { [Vital.Resource1] = 150f },
                mutations: mutations);
            var entry = new Spell4Entry
            {
                InnateCostType0 = (uint)Vital.Resource1,
                InnateCost0     = 100u,
                InnateCostType1 = (uint)Vital.KineticCell,
                InnateCost1     = 100u
            };

            CastResult result = SpellVitalPolicy.CheckCosts(entity.Object, entry);

            Assert.Equal(CastResult.CasterVitalCostResource1, result);
            Assert.Empty(mutations);
        }

        [Fact]
        public void TryConsumeCosts_AliasCollisionConsumesCanonicalStorageOnce()
        {
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 250f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IUnitEntity> entity = CreateEntity(values, mutations: mutations);
            var entry = new Spell4Entry
            {
                InnateCostType0 = (uint)Vital.Resource1,
                InnateCost0     = 100u,
                InnateCostType1 = (uint)Vital.KineticCell,
                InnateCost1     = 100u
            };

            CastResult result = SpellVitalPolicy.TryConsumeCosts(entity.Object, entry);

            Assert.Equal(CastResult.Ok, result);
            Assert.Equal(50f, values[Vital.Resource1]);
            Assert.Equal([(Vital.Resource1, -200f)], mutations);
        }

        [Fact]
        public void TryConsumeCosts_SecondInsufficientCostDoesNotConsumeFirst()
        {
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IUnitEntity> entity = CreateEntity(
                new Dictionary<Vital, float>
                {
                    [Vital.Resource1] = 100f,
                    [Vital.Focus]     = 10f
                },
                mutations: mutations);
            var entry = new Spell4Entry
            {
                InnateCostType0 = (uint)Vital.Resource1,
                InnateCost0     = 50u,
                InnateCostType1 = (uint)Vital.Focus,
                InnateCost1     = 20u
            };

            CastResult result = SpellVitalPolicy.TryConsumeCosts(entity.Object, entry);

            Assert.Equal(CastResult.CasterVitalCostFocus, result);
            Assert.Empty(mutations);
        }

        [Fact]
        public void TryConsumeCosts_UnexpectedCommitFailureRollsBackEarlierCost()
        {
            var values = new Dictionary<Vital, float>
            {
                [Vital.Resource1] = 100f,
                [Vital.Focus]     = 100f
            };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IUnitEntity> entity = CreateEntity(
                values,
                mutations: mutations,
                rejectMutation: (vital, delta) => vital == Vital.Focus && delta < 0f);
            var entry = new Spell4Entry
            {
                InnateCostType0 = (uint)Vital.Resource1,
                InnateCost0     = 25u,
                InnateCostType1 = (uint)Vital.Focus,
                InnateCost1     = 20u
            };

            CastResult result = SpellVitalPolicy.TryConsumeCosts(entity.Object, entry);

            Assert.Equal(CastResult.SpellBad, result);
            Assert.Equal(100f, values[Vital.Resource1]);
            Assert.Equal(100f, values[Vital.Focus]);
            Assert.Equal(
                [(Vital.Resource1, -25f), (Vital.Resource1, 25f)],
                mutations);
        }

        [Fact]
        public void TryConsumeCosts_PreMutationExceptionRollsBackEarlierCost()
        {
            var values = new Dictionary<Vital, float>
            {
                [Vital.Resource1] = 100f,
                [Vital.Focus]     = 100f
            };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IUnitEntity> entity = CreateEntity(
                values,
                mutations: mutations,
                throwBeforeMutation: (vital, delta) => vital == Vital.Focus && delta < 0f);
            var entry = new Spell4Entry
            {
                InnateCostType0 = (uint)Vital.Resource1,
                InnateCost0     = 25u,
                InnateCostType1 = (uint)Vital.Focus,
                InnateCost1     = 20u
            };

            CastResult result = SpellVitalPolicy.TryConsumeCosts(entity.Object, entry);

            Assert.Equal(CastResult.SpellBad, result);
            Assert.Equal(100f, values[Vital.Resource1]);
            Assert.Equal(100f, values[Vital.Focus]);
            Assert.Equal(
                [(Vital.Resource1, -25f), (Vital.Resource1, 25f)],
                mutations);
        }

        [Fact]
        public void TryConsumeCosts_PostMutationNotificationExceptionKeepsCommittedCost()
        {
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IUnitEntity> entity = CreateEntity(
                values,
                mutations: mutations,
                throwAfterMutation: (vital, delta) => vital == Vital.Resource1 && delta < 0f);
            var entry = new Spell4Entry
            {
                InnateCostType0 = (uint)Vital.Resource1,
                InnateCost0     = 25u
            };

            CastResult result = SpellVitalPolicy.TryConsumeCosts(entity.Object, entry);

            Assert.Equal(CastResult.Ok, result);
            Assert.Equal(75f, values[Vital.Resource1]);
            Assert.Equal([(Vital.Resource1, -25f)], mutations);
        }

        [Fact]
        public void TryConsumeCosts_HealthZeroNotificationExceptionFailsClosed()
        {
            var values = new Dictionary<Vital, float> { [Vital.Health] = 25f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IUnitEntity> entity = CreateEntity(
                values,
                mutations: mutations,
                throwAfterMutation: (vital, delta) => vital == Vital.Health && delta < 0f);
            var entry = new Spell4Entry
            {
                InnateCostType0 = (uint)Vital.Health,
                InnateCost0     = 25u
            };

            CastResult result = SpellVitalPolicy.TryConsumeCosts(entity.Object, entry);

            Assert.Equal(CastResult.SpellBad, result);
            Assert.Equal(0f, values[Vital.Health]);
            Assert.Equal([(Vital.Health, -25f)], mutations);
        }

        [Fact]
        public void TryConsumeCosts_UnsupportedVitalFailsWithoutMutation()
        {
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IUnitEntity> entity = CreateEntity(
                new Dictionary<Vital, float> { [Vital.Resource1] = 100f },
                mutations: mutations);
            var entry = new Spell4Entry
            {
                InnateCostType0 = (uint)Vital.PublicResource1,
                InnateCost0     = 10u
            };

            CastResult result = SpellVitalPolicy.TryConsumeCosts(entity.Object, entry);

            Assert.Equal(CastResult.SpellBad, result);
            Assert.Empty(mutations);
        }

        [Fact]
        public void CheckCosts_ZeroAmountIgnoresUnsupportedVitalIdentifier()
        {
            Mock<IUnitEntity> entity = CreateEntity([]);
            var entry = new Spell4Entry
            {
                InnateCostType0 = (uint)Vital.PublicResource1,
                InnateCost0     = 0u
            };

            CastResult result = SpellVitalPolicy.CheckCosts(entity.Object, entry);

            Assert.Equal(CastResult.Ok, result);
            entity.Verify(unit => unit.TryGetVitalValue(
                It.IsAny<Vital>(), out It.Ref<float>.IsAny), Times.Never);
        }

        [Theory]
        [InlineData(Vital.Health, CastResult.CasterVitalCostHealth)]
        [InlineData(Vital.Resource0, CastResult.CasterVitalCostResource0)]
        [InlineData(Vital.KineticCell, CastResult.CasterVitalCostResource1)]
        [InlineData(Vital.StalkerA, CastResult.CasterVitalCostResource3)]
        [InlineData(Vital.SpellSurge, CastResult.CasterVitalCostResource4)]
        [InlineData(Vital.Focus, CastResult.CasterVitalCostFocus)]
        [InlineData(Vital.ShieldCapacity, CastResult.CasterVitalCostShieldCapacity)]
        [InlineData(Vital.Resource7, CastResult.CasterVitalCostResource7)]
        [InlineData(Vital.InterruptArmor, CastResult.CasterVitalCostInterruptArmor)]
        [InlineData(Vital.Breath, CastResult.SpellBad)]
        public void GetFailureResult_MapsBuild16042Vital(Vital vital, CastResult expected)
        {
            Assert.Equal(expected, SpellVitalPolicy.GetFailureResult(vital));
        }

        private static Mock<IUnitEntity> CreateEntity(
            Dictionary<Vital, float> values,
            Dictionary<Vital, float> maxima = null,
            List<(Vital Vital, float Delta)> mutations = null,
            Func<Vital, float, bool> rejectMutation = null,
            Func<Vital, float, bool> throwBeforeMutation = null,
            Func<Vital, float, bool> throwAfterMutation = null)
        {
            maxima ??= [];
            mutations ??= [];

            var entity = new Mock<IUnitEntity>();
            entity.Setup(unit => unit.TryGetVitalValue(
                    It.IsAny<Vital>(),
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital vital, out float value) =>
                {
                    return values.TryGetValue(Canonical(vital), out value);
                }));
            entity.Setup(unit => unit.TryGetVitalMaximum(
                    It.IsAny<Vital>(),
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalMaximum((Vital vital, out float maximum) =>
                {
                    return maxima.TryGetValue(Canonical(vital), out maximum);
                }));
            entity.Setup(unit => unit.TryModifyVital(
                    It.IsAny<Vital>(),
                    It.IsAny<float>(),
                    It.IsAny<IUnitEntity>()))
                .Returns((Vital vital, float delta, IUnitEntity _) =>
                {
                    Vital canonical = Canonical(vital);
                    if (throwBeforeMutation?.Invoke(canonical, delta) == true)
                        throw new InvalidOperationException("Test pre-mutation failure.");

                    if (rejectMutation?.Invoke(canonical, delta) == true)
                        return false;

                    if (!values.TryGetValue(canonical, out float value))
                        return false;

                    mutations.Add((vital, delta));
                    values[canonical] = value + delta;

                    if (throwAfterMutation?.Invoke(canonical, delta) == true)
                        throw new InvalidOperationException("Test post-mutation notification failure.");

                    return true;
                });
            return entity;
        }

        private static Vital Canonical(Vital vital)
        {
            return vital switch
            {
                Vital.KineticCell or Vital.StalkerB or Vital.MedicCore or Vital.Volatility => Vital.Resource1,
                Vital.StalkerA                                                             => Vital.Resource3,
                Vital.SpellSurge                                                           => Vital.Resource4,
                _                                                                          => vital
            };
        }

        private delegate bool TryGetVitalValue(Vital vital, out float value);
        private delegate bool TryGetVitalMaximum(Vital vital, out float maximum);
    }
}
