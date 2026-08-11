using System.Numerics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.CSI
{
    /// <summary>
    /// Validates the server-owned spatial boundary for a client-side interaction.
    /// </summary>
    public static class ClientSideInteractionValidator
    {
        private const float DefaultMaximumRange = 5f;

        /// <summary>
        /// Returns whether an activating player can interact with the supplied entity.
        /// </summary>
        public static bool IsValid(IPlayer player, IWorldEntity entity)
        {
            if (player == null || entity == null || !player.InWorld || !entity.InWorld)
                return false;

            if (!ReferenceEquals(player.Map, entity.Map))
                return false;

            if (!ReferenceEquals(player.GetVisible<IWorldEntity>(entity.Guid), entity))
                return false;

            Creature2Entry creatureEntry = entity.CreatureEntry;
            if (creatureEntry == null)
                return false;

            if (!IsFinite(player.Position) || !IsFinite(entity.Position))
                return false;

            float minimumRange = creatureEntry.ActivateSpellMinRange;
            float maximumRange = creatureEntry.ActivateSpellMaxRange;
            if (!float.IsFinite(minimumRange) || !float.IsFinite(maximumRange)
                || minimumRange < 0f || maximumRange < 0f)
                return false;

            float distance = Vector3.Distance(player.Position, entity.Position);
            if (!float.IsFinite(distance))
                return false;

            if (minimumRange > 0f && distance < minimumRange)
                return false;

            if (maximumRange == 0f)
                maximumRange = DefaultMaximumRange;

            return distance <= maximumRange;
        }

        /// <summary>
        /// Returns whether the supplied entity declares a cast-based activation spell.
        /// </summary>
        public static bool HasCastActivation(IWorldEntity entity)
        {
            Creature2Entry entry = entity?.CreatureEntry;
            return entry != null
                && (entry.Spell4IdActivate00 != 0u
                    || entry.Spell4IdActivate01 != 0u
                    || entry.Spell4IdActivate02 != 0u
                    || entry.Spell4IdActivate03 != 0u);
        }

        private static bool IsFinite(Vector3 position)
        {
            return float.IsFinite(position.X)
                && float.IsFinite(position.Y)
                && float.IsFinite(position.Z);
        }
    }
}
