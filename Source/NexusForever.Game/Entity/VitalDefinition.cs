using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Entity
{
    /// <summary>
    /// Defines the stat storage and optional maximum property for a client vital identifier.
    /// </summary>
    internal readonly record struct VitalDefinition(Stat Stat, Property? MaximumProperty, bool UsesIntegerStorage)
    {
        /// <summary>
        /// Returns the definition for a supported build-16042 vital identifier.
        /// </summary>
        public static bool TryGet(Vital vital, out VitalDefinition definition)
        {
            switch (vital)
            {
                case Vital.Health:
                    definition = new VitalDefinition(Stat.Health, Property.BaseHealth, true);
                    return true;
                case Vital.ShieldCapacity:
                    definition = new VitalDefinition(Stat.Shield, Property.ShieldCapacityMax, true);
                    return true;
                case Vital.Resource0:
                    definition = new VitalDefinition(Stat.Resource0, Property.ResourceMax0, false);
                    return true;
                case Vital.Focus:
                    definition = new VitalDefinition(Stat.Focus, Property.BaseFocusPool, false);
                    return true;
                case Vital.Resource7:
                    definition = new VitalDefinition(Stat.Dash, Property.ResourceMax7, false);
                    return true;
                case Vital.Resource1:
                case Vital.KineticCell:
                case Vital.StalkerB:
                case Vital.MedicCore:
                case Vital.Volatility:
                    definition = new VitalDefinition(Stat.Resource1, Property.ResourceMax1, false);
                    return true;
                case Vital.Resource2:
                    definition = new VitalDefinition(Stat.Resource2, Property.ResourceMax2, false);
                    return true;
                case Vital.Resource3:
                case Vital.StalkerA:
                    definition = new VitalDefinition(Stat.Resource3, Property.ResourceMax3, false);
                    return true;
                case Vital.Resource4:
                case Vital.SpellSurge:
                    definition = new VitalDefinition(Stat.Resource4, Property.ResourceMax4, false);
                    return true;
                case Vital.Resource5:
                    definition = new VitalDefinition(Stat.Resource5, Property.ResourceMax5, false);
                    return true;
                case Vital.Resource6:
                    definition = new VitalDefinition(Stat.Resource6, Property.ResourceMax6, false);
                    return true;
                case Vital.InterruptArmor:
                    definition = new VitalDefinition(Stat.InterruptArmour, null, true);
                    return true;
                default:
                    definition = default;
                    return false;
            }
        }
    }
}
