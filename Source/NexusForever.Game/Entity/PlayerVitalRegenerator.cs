using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Entity
{
    /// <summary>
    /// Narrow player surface consumed by <see cref="PlayerVitalRegenerator"/>.
    /// </summary>
    internal interface IPlayerVitalRegenerationOwner
    {
        bool IsAlive { get; }
        bool InCombat { get; }
        Class Class { get; }

        float GetPropertyValue(Property property);
        bool TryGetVitalValue(Vital vital, out float value);
        bool TryModifyVital(Vital vital, float delta, IUnitEntity source = null);
    }

    /// <summary>
    /// Applies fixed-step player resource regeneration and class resource lifecycle rules.
    /// </summary>
    internal sealed class PlayerVitalRegenerator
    {
        private const double RegenerationInterval = 0.5d;
        private const int MaximumCatchUpTicks = 20;

        private const double WarriorDecayDelay = 3d;
        private const float WarriorDecayPerTick = 75f;
        private const float SpellslingerRegenerationPerTick = 2f;
        private const float MedicRegenerationPerSecond = 4f;
        private const double EsperResetDelay = 10d;
        private const double EngineerDecayDelay = 5d;
        private const float EngineerDecayPerSecond = 10f;

        private readonly IPlayerVitalRegenerationOwner owner;

        private double regenerationAccumulator;
        private double focusAccumulator;
        private double warriorTimeSinceIncrease;
        private double medicOutOfCombatAccumulator;
        private double esperOutOfCombatTime;
        private double engineerOutOfCombatTime;
        private double engineerNextDecayTime;
        private float? previousResource1;

        /// <summary>
        /// Creates a new resource regenerator for the supplied player.
        /// </summary>
        public PlayerVitalRegenerator(IPlayerVitalRegenerationOwner owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Reset();
        }

        /// <summary>
        /// Captures the initial Resource1 value after persisted stats have loaded.
        /// </summary>
        public void Initialise()
        {
            previousResource1 = owner.TryGetVitalValue(Vital.Resource1, out float value)
                && float.IsFinite(value)
                ? value
                : null;
        }

        /// <summary>
        /// Advances resource regeneration by the supplied elapsed seconds.
        /// </summary>
        public void Update(double elapsed)
        {
            if (!owner.IsAlive)
            {
                Reset();
                return;
            }

            if (!double.IsFinite(elapsed) || elapsed <= 0d)
                return;

            if (previousResource1 == null)
                Initialise();

            double maximumCatchUp = RegenerationInterval * MaximumCatchUpTicks;
            regenerationAccumulator = Math.Min(regenerationAccumulator + elapsed, maximumCatchUp);

            int ticks = Math.Min(
                (int)Math.Floor(regenerationAccumulator / RegenerationInterval),
                MaximumCatchUpTicks);
            regenerationAccumulator -= ticks * RegenerationInterval;

            for (int i = 0; i < ticks; i++)
                Regenerate();
        }

        /// <summary>
        /// Tracks Resource1 changes so Warrior decay is delayed only by increases.
        /// </summary>
        public void OnStatUpdated(Stat stat, float value)
        {
            if (stat != Stat.Resource1)
                return;

            if (!float.IsFinite(value))
            {
                previousResource1 = null;
                return;
            }

            if (owner.Class == Class.Warrior
                && previousResource1.HasValue
                && value > previousResource1.Value)
                warriorTimeSinceIncrease = 0d;

            previousResource1 = value;
        }

        /// <summary>
        /// Resets class timers that must restart when combat begins.
        /// </summary>
        public void OnCombatStateChanged(bool inCombat)
        {
            if (!inCombat)
                return;

            medicOutOfCombatAccumulator = 0d;
            esperOutOfCombatTime = 0d;
            ResetEngineerTimers();
        }

        /// <summary>
        /// Clears accumulated time and class state without modifying player resources.
        /// </summary>
        public void Reset()
        {
            regenerationAccumulator = 0d;
            focusAccumulator = 0d;
            warriorTimeSinceIncrease = WarriorDecayDelay;
            medicOutOfCombatAccumulator = 0d;
            esperOutOfCombatTime = 0d;
            previousResource1 = null;
            ResetEngineerTimers();
        }

        private void Regenerate()
        {
            warriorTimeSinceIncrease = Math.Min(
                warriorTimeSinceIncrease + RegenerationInterval,
                WarriorDecayDelay);

            RegenerateProportional(
                Vital.Resource0,
                Property.ResourceMax0,
                Property.ResourceRegenMultiplier0);
            RegenerateProportional(
                Vital.Resource7,
                Property.ResourceMax7,
                Property.ResourceRegenMultiplier7);

            focusAccumulator += RegenerationInterval;
            if (focusAccumulator >= 1d)
            {
                focusAccumulator -= 1d;
                RegenerateFocus();
            }

            switch (owner.Class)
            {
                case Class.Warrior:
                    RegenerateWarrior();
                    break;
                case Class.Engineer:
                    RegenerateEngineer();
                    break;
                case Class.Esper:
                    RegenerateEsper();
                    break;
                case Class.Medic:
                    RegenerateMedic();
                    break;
                case Class.Stalker:
                    RegenerateProportional(
                        Vital.Resource3,
                        Property.ResourceMax3,
                        Property.ResourceRegenMultiplier3);
                    break;
                case Class.Spellslinger:
                    ModifyVital(Vital.Resource4, SpellslingerRegenerationPerTick);
                    break;
            }
        }

        private void RegenerateFocus()
        {
            float maximum = owner.GetPropertyValue(Property.BaseFocusPool);
            float multiplier = owner.GetPropertyValue(owner.InCombat
                ? Property.BaseFocusRecoveryInCombat
                : Property.BaseFocusRecoveryOutofCombat);
            if (!float.IsFinite(maximum)
                || !float.IsFinite(multiplier)
                || maximum <= 0f
                || multiplier <= 0f)
                return;

            ModifyVital(Vital.Focus, (double)maximum * multiplier);
        }

        private void RegenerateWarrior()
        {
            if (!owner.InCombat || warriorTimeSinceIncrease >= WarriorDecayDelay)
                ModifyVital(Vital.Resource1, -WarriorDecayPerTick);
        }

        private void RegenerateMedic()
        {
            if (owner.InCombat)
            {
                medicOutOfCombatAccumulator = 0d;
                return;
            }

            medicOutOfCombatAccumulator += RegenerationInterval;
            if (medicOutOfCombatAccumulator < 1d)
                return;

            medicOutOfCombatAccumulator -= 1d;
            ModifyVital(Vital.Resource1, MedicRegenerationPerSecond);
        }

        private void RegenerateEsper()
        {
            if (owner.InCombat)
            {
                esperOutOfCombatTime = 0d;
                return;
            }

            esperOutOfCombatTime += RegenerationInterval;
            if (esperOutOfCombatTime < EsperResetDelay)
                return;

            esperOutOfCombatTime = 0d;
            if (owner.TryGetVitalValue(Vital.Resource1, out float value)
                && float.IsFinite(value)
                && value > 0f)
                ModifyVital(Vital.Resource1, -value);
        }

        private void RegenerateEngineer()
        {
            if (owner.InCombat)
            {
                ResetEngineerTimers();
                return;
            }

            engineerOutOfCombatTime += RegenerationInterval;
            if (engineerOutOfCombatTime < engineerNextDecayTime)
                return;

            engineerNextDecayTime += 1d;
            ModifyVital(Vital.Resource1, -EngineerDecayPerSecond);
        }

        private void RegenerateProportional(Vital vital, Property maximumProperty, Property multiplierProperty)
        {
            float maximum = owner.GetPropertyValue(maximumProperty);
            float multiplier = owner.GetPropertyValue(multiplierProperty);
            if (!float.IsFinite(maximum)
                || !float.IsFinite(multiplier)
                || maximum <= 0f
                || multiplier <= 0f)
                return;

            ModifyVital(vital, (double)maximum * multiplier);
        }

        private void ModifyVital(Vital vital, double amount)
        {
            if (!double.IsFinite(amount)
                || amount == 0d
                || amount > float.MaxValue
                || amount < -float.MaxValue)
                return;

            owner.TryModifyVital(vital, (float)amount);
        }

        private void ResetEngineerTimers()
        {
            engineerOutOfCombatTime = 0d;
            engineerNextDecayTime = EngineerDecayDelay;
        }
    }
}
