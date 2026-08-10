using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Reputation;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Reputation;

namespace NexusForever.Game.Reputation
{
    public class Reputation : IReputation
    {
        [Flags]
        private enum SaveMask
        {
            None   = 0x00,
            Create = 0x01,
            Amount = 0x02
        }

        public Faction Id => Entry.FactionId;
        public IFactionNode Entry { get; }

        public float Amount
        {
            get => amount;
            set
            {
                if (amount == value)
                    return;

                amount = value;
                saveMask.Mark(SaveMask.Amount);
            }
        }

        private float amount;

        private readonly VersionedSaveMask<SaveMask> saveMask = new();

        private readonly IPlayer player;

        /// <summary>
        /// Create a new <see cref="IReputation"/> from an existing database model.
        /// </summary>
        public Reputation(IPlayer player, IFactionNode entry, CharacterReputation model)
        {
            this.player = player;

            Entry    = entry;
            amount   = model.Amount;

        }

        /// <summary>
        /// Create a new <see cref="IReputation"/> from the supplied <see cref="IFactionNode"/> and amount.
        /// </summary>
        public Reputation(IPlayer player, IFactionNode entry, float amount)
        {
            this.player = player;

            Entry       = entry;
            this.amount = amount;

            saveMask.Mark(SaveMask.Create);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage reputation changes and acknowledge them after the character database commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            VersionedSaveMaskSnapshot<SaveMask> snapshot = saveMask.Capture();
            SaveMask mask = snapshot.Mask;
            if (mask == SaveMask.None)
                return;

            var model = new CharacterReputation
            {
                Id        = player.CharacterId,
                FactionId = (uint)Id,
                Amount    = Amount
            };

            if ((mask & SaveMask.Create) != 0)
                context.Add(model);
            else
            {
                EntityEntry<CharacterReputation> entity = context.Attach(model);
                if ((mask & SaveMask.Amount) != 0)
                {
                    model.Amount = Amount;
                    entity.Property(p => p.Amount).IsModified = true;
                }
            }

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }
    }
}
