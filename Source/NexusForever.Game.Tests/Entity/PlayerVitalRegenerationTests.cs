using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Tests.Entity
{
    public sealed class PlayerVitalRegenerationTests
    {
        [Fact]
        public void Update_RegeneratesBaseResourcesAtHalfAndOneSecondIntervals()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Medic);
            ConfigureBaseResources(owner);

            regenerator.Update(0.5d);

            Assert.Equal(11.25f, owner.GetVital(Vital.Resource0), 3);
            Assert.Equal(9f, owner.GetVital(Vital.Resource7), 3);
            Assert.Equal(0f, owner.GetVital(Vital.Focus));

            regenerator.Update(0.5d);

            Assert.Equal(22.5f, owner.GetVital(Vital.Resource0), 3);
            Assert.Equal(18f, owner.GetVital(Vital.Resource7), 3);
            Assert.Equal(20f, owner.GetVital(Vital.Focus), 3);
        }

        [Fact]
        public void Update_UsesCombatFocusRecoveryRate()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Esper);
            ConfigureBaseResources(owner);
            owner.InCombat = true;

            regenerator.Update(1d);

            Assert.Equal(22.5f, owner.GetVital(Vital.Resource0), 3);
            Assert.Equal(18f, owner.GetVital(Vital.Resource7), 3);
            Assert.Equal(5f, owner.GetVital(Vital.Focus), 3);
        }

        [Fact]
        public void Update_ProcessesDeterministicMultiTickCatchUp()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Esper);
            ConfigureBaseResources(owner);

            regenerator.Update(1.5d);

            Assert.Equal(33.75f, owner.GetVital(Vital.Resource0), 3);
            Assert.Equal(27f, owner.GetVital(Vital.Resource7), 3);
            Assert.Equal(20f, owner.GetVital(Vital.Focus), 3);
        }

        [Fact]
        public void Update_ExtremeElapsedTimeUsesBoundedCatchUp()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Esper);
            owner.SetProperty(Property.ResourceMax0, 10000f);
            owner.SetProperty(Property.ResourceRegenMultiplier0, 0.001f);

            regenerator.Update(1000d);

            Assert.Equal(200f, owner.GetVital(Vital.Resource0), 3);
            Assert.Equal(20, owner.GetModificationCount(Vital.Resource0));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-1d)]
        [InlineData(0d)]
        public void Update_InvalidElapsedTimeIsIgnored(double elapsed)
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Esper);
            ConfigureBaseResources(owner);

            regenerator.Update(elapsed);

            Assert.Empty(owner.Modifications);
        }

        [Fact]
        public void Update_InvalidRegenerationPropertiesDoNotDrainResources()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Stalker);
            owner.SetProperty(Property.ResourceMax0, 100f);
            owner.SetProperty(Property.ResourceRegenMultiplier0, -0.5f);
            owner.SetProperty(Property.ResourceMax3, float.PositiveInfinity);
            owner.SetProperty(Property.ResourceRegenMultiplier3, 1f);
            owner.SetProperty(Property.BaseFocusPool, 100f);
            owner.SetProperty(Property.BaseFocusRecoveryOutofCombat, float.NaN);
            owner.SetVital(Vital.Resource0, 50f);
            owner.SetVital(Vital.Resource3, 50f);
            owner.SetVital(Vital.Focus, 50f);

            regenerator.Update(1d);

            Assert.Equal(50f, owner.GetVital(Vital.Resource0));
            Assert.Equal(50f, owner.GetVital(Vital.Resource3));
            Assert.Equal(50f, owner.GetVital(Vital.Focus));
            Assert.Empty(owner.Modifications);
        }

        [Fact]
        public void Update_DeadPlayerDoesNotRegenerateOrBankCatchUp()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Esper);
            ConfigureBaseResources(owner);
            owner.IsAlive = false;

            regenerator.Update(10d);

            Assert.Empty(owner.Modifications);

            owner.IsAlive = true;
            regenerator.Update(0.5d);

            Assert.Equal(11.25f, owner.GetVital(Vital.Resource0), 3);
            Assert.Equal(1, owner.GetModificationCount(Vital.Resource0));
        }

        [Fact]
        public void Update_ZeroEnduranceRegenerationDoesNotRegenerateOrBankCatchUp()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Esper);
            owner.SetProperty(Property.ResourceMax0, 500f);
            owner.SetProperty(Property.ResourceRegenMultiplier0, 0f);

            regenerator.Update(10d);

            Assert.Equal(0f, owner.GetVital(Vital.Resource0));
            Assert.Empty(owner.Modifications);

            owner.SetProperty(Property.ResourceRegenMultiplier0, 0.0225f);
            regenerator.Update(0.5d);

            Assert.Equal(11.25f, owner.GetVital(Vital.Resource0), 3);
            Assert.Equal(1, owner.GetModificationCount(Vital.Resource0));
        }

        [Fact]
        public void Update_StalkerRegeneratesSuitPowerEveryHalfSecond()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Stalker);
            owner.SetProperty(Property.ResourceMax3, 100f);
            owner.SetProperty(Property.ResourceRegenMultiplier3, 0.035f);

            regenerator.Update(0.5d);

            Assert.Equal(3.5f, owner.GetVital(Vital.Resource3), 3);
        }

        [Fact]
        public void Update_SpellslingerRegeneratesSpellPowerEveryHalfSecond()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Spellslinger);
            owner.SetProperty(Property.ResourceMax4, 100f);

            regenerator.Update(0.5d);

            Assert.Equal(2f, owner.GetVital(Vital.Resource4), 3);
        }

        [Fact]
        public void Warrior_IncreaseDelaysDecayAndSelfDecayDoesNotResetDelay()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Warrior);
            owner.SetProperty(Property.ResourceMax1, 1000f);
            owner.SetVital(Vital.Resource1, 900f);
            owner.InCombat = true;

            regenerator.Update(2.5d);
            Assert.Equal(900f, owner.GetVital(Vital.Resource1));

            regenerator.Update(0.5d);
            Assert.Equal(825f, owner.GetVital(Vital.Resource1));

            regenerator.Update(0.5d);
            Assert.Equal(750f, owner.GetVital(Vital.Resource1));
        }

        [Fact]
        public void Warrior_OutOfCombatDecayStartsImmediately()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Warrior);
            owner.SetProperty(Property.ResourceMax1, 1000f);
            owner.SetVital(Vital.Resource1, 100f);
            owner.InCombat = false;

            regenerator.Update(0.5d);

            Assert.Equal(25f, owner.GetVital(Vital.Resource1));
        }

        [Fact]
        public void Medic_CombatEntryResetsPartialOutOfCombatInterval()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Medic);
            owner.SetProperty(Property.ResourceMax1, 4f);

            regenerator.Update(0.5d);
            owner.InCombat = true;
            regenerator.OnCombatStateChanged(true);
            regenerator.Update(0.5d);
            owner.InCombat = false;
            regenerator.OnCombatStateChanged(false);
            regenerator.Update(0.5d);

            Assert.Equal(0f, owner.GetVital(Vital.Resource1));

            regenerator.Update(0.5d);
            Assert.Equal(4f, owner.GetVital(Vital.Resource1));
        }

        [Fact]
        public void Esper_CombatEntryRestartsTenSecondResetDelay()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Esper);
            owner.SetProperty(Property.ResourceMax1, 5f);
            owner.SetVital(Vital.Resource1, 5f);

            regenerator.Update(9.5d);
            owner.InCombat = true;
            regenerator.OnCombatStateChanged(true);
            regenerator.Update(0.5d);
            owner.InCombat = false;
            regenerator.OnCombatStateChanged(false);
            regenerator.Update(9.5d);

            Assert.Equal(5f, owner.GetVital(Vital.Resource1));

            regenerator.Update(0.5d);
            Assert.Equal(0f, owner.GetVital(Vital.Resource1));
        }

        [Fact]
        public void Engineer_CombatEntryRestartsFiveSecondDecayDelay()
        {
            (TestOwner owner, PlayerVitalRegenerator regenerator) = CreateRegenerator(Class.Engineer);
            owner.SetProperty(Property.ResourceMax1, 100f);
            owner.SetVital(Vital.Resource1, 100f);

            regenerator.Update(4.5d);
            owner.InCombat = true;
            regenerator.OnCombatStateChanged(true);
            regenerator.Update(0.5d);
            owner.InCombat = false;
            regenerator.OnCombatStateChanged(false);
            regenerator.Update(4.5d);

            Assert.Equal(100f, owner.GetVital(Vital.Resource1));

            regenerator.Update(0.5d);
            Assert.Equal(90f, owner.GetVital(Vital.Resource1));
            regenerator.Update(1d);
            Assert.Equal(80f, owner.GetVital(Vital.Resource1));
        }

        private static (TestOwner Owner, PlayerVitalRegenerator Regenerator) CreateRegenerator(Class @class)
        {
            var owner = new TestOwner
            {
                Class = @class
            };
            var regenerator = new PlayerVitalRegenerator(owner);
            owner.StatUpdated = regenerator.OnStatUpdated;
            regenerator.Initialise();
            return (owner, regenerator);
        }

        private static void ConfigureBaseResources(TestOwner owner)
        {
            owner.SetProperty(Property.ResourceMax0, 500f);
            owner.SetProperty(Property.ResourceRegenMultiplier0, 0.0225f);
            owner.SetProperty(Property.ResourceMax7, 200f);
            owner.SetProperty(Property.ResourceRegenMultiplier7, 0.045f);
            owner.SetProperty(Property.BaseFocusPool, 1000f);
            owner.SetProperty(Property.BaseFocusRecoveryInCombat, 0.005f);
            owner.SetProperty(Property.BaseFocusRecoveryOutofCombat, 0.02f);
        }

        private sealed class TestOwner : IPlayerVitalRegenerationOwner
        {
            private readonly Dictionary<Property, float> properties = new();
            private readonly Dictionary<Vital, float> vitals = new();

            public bool IsAlive { get; set; } = true;
            public bool InCombat { get; set; }
            public Class Class { get; set; }
            public Action<Stat, float> StatUpdated { get; set; }
            public List<(Vital Vital, float Delta)> Modifications { get; } = new();

            public float GetPropertyValue(Property property)
            {
                return properties.GetValueOrDefault(property);
            }

            public bool TryGetVitalValue(Vital vital, out float value)
            {
                value = GetVital(vital);
                return TryGetMaximumProperty(vital, out _);
            }

            public bool TryModifyVital(Vital vital, float delta, IUnitEntity source = null)
            {
                if (!float.IsFinite(delta)
                    || !TryGetMaximumProperty(vital, out Property maximumProperty))
                    return false;

                float maximum = GetPropertyValue(maximumProperty);
                float current = GetVital(vital);
                float value = Math.Clamp(current + delta, 0f, maximum);
                vitals[vital] = value;
                Modifications.Add((vital, delta));

                if (vital == Vital.Resource1)
                    StatUpdated?.Invoke(Stat.Resource1, value);

                return true;
            }

            public void SetProperty(Property property, float value)
            {
                properties[property] = value;
            }

            public void SetVital(Vital vital, float value)
            {
                vitals[vital] = value;
                if (vital == Vital.Resource1)
                    StatUpdated?.Invoke(Stat.Resource1, value);
            }

            public float GetVital(Vital vital)
            {
                return vitals.GetValueOrDefault(vital);
            }

            public int GetModificationCount(Vital vital)
            {
                return Modifications.Count(modification => modification.Vital == vital);
            }

            private static bool TryGetMaximumProperty(Vital vital, out Property property)
            {
                property = vital switch
                {
                    Vital.Resource0 => Property.ResourceMax0,
                    Vital.Resource1 => Property.ResourceMax1,
                    Vital.Resource3 => Property.ResourceMax3,
                    Vital.Resource4 => Property.ResourceMax4,
                    Vital.Focus     => Property.BaseFocusPool,
                    Vital.Resource7 => Property.ResourceMax7,
                    _               => default
                };
                return vital is Vital.Resource0
                    or Vital.Resource1
                    or Vital.Resource3
                    or Vital.Resource4
                    or Vital.Focus
                    or Vital.Resource7;
            }
        }
    }
}
