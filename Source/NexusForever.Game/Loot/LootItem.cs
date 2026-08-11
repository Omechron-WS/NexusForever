using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Static.Loot;

namespace NexusForever.Game.Loot
{
    public class LootItem : ILootItem
    {
        public LootItemType Type { get; }
        public uint StaticId { get; }

        private readonly float probability;
        private readonly uint minCount;
        private readonly uint maxCount;
        private readonly Func<double> probabilityRoll;
        private readonly Func<long, long, long> countRoll;

        public LootItem(LootItemModel model)
            : this(
                model,
                static () => Random.Shared.NextDouble(),
                static (minimum, maximum) => Random.Shared.NextInt64(minimum, maximum))
        {
        }

        internal LootItem(LootItemModel model, Func<long, long, long> countRoll)
            : this(model, static () => Random.Shared.NextDouble(), countRoll)
        {
        }

        internal LootItem(
            LootItemModel model,
            Func<double> probabilityRoll,
            Func<long, long, long> countRoll)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(probabilityRoll);
            ArgumentNullException.ThrowIfNull(countRoll);
            if (!TryValidateModel(model, out string validationError))
                throw new ArgumentException(validationError, nameof(model));

            Type        = (LootItemType)model.Type;
            StaticId    = model.StaticId;
            probability = model.Probability;
            minCount    = model.MinCount;
            maxCount    = model.MaxCount;
            this.probabilityRoll = probabilityRoll;
            this.countRoll       = countRoll;
        }

        /// <summary>
        /// Roll against probability and return whether the item drops, with the count.
        /// </summary>
        public bool GetDrop(out uint count)
        {
            count = 0;

            double probabilityValue = probabilityRoll();
            if (!double.IsFinite(probabilityValue)
                || probabilityValue < 0d
                || probabilityValue >= 1d)
                throw new InvalidOperationException("The loot probability generator returned a value outside [0, 1).");

            double chance = probabilityValue * 100d;
            if (chance >= probability)
                return false;

            if (maxCount == minCount)
            {
                count = minCount;
                return true;
            }

            long rolledCount = countRoll(minCount, (long)maxCount + 1L);
            if (rolledCount < minCount || rolledCount > maxCount)
                throw new InvalidOperationException("The loot count generator returned a value outside the configured range.");

            count = (uint)rolledCount;

            return true;
        }

        /// <summary>
        /// Validate database-backed probability and item-count bounds.
        /// </summary>
        internal static bool TryValidateModel(LootItemModel model, out string error)
        {
            if (model == null)
            {
                error = "The loot item model is null.";
                return false;
            }

            if (!float.IsFinite(model.Probability)
                || model.Probability < 0f
                || model.Probability > 100f)
            {
                error = $"has invalid probability {model.Probability}; expected a finite value from 0 through 100.";
                return false;
            }

            if (model.MinCount == 0u || model.MaxCount < model.MinCount)
            {
                error = $"has invalid count range {model.MinCount} through {model.MaxCount}.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
