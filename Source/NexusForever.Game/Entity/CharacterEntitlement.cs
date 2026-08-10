using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Persistence;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.Game.Entity
{
    public class CharacterEntitlement : Entitlement.Entitlement, ICharacterEntitlement
    {
        private readonly IPlayer player;

        /// <summary>
        /// Create a new <see cref="ICharacterEntitlement"/> from an existing database model.
        /// </summary>
        public CharacterEntitlement(IPlayer player, CharacterEntitlementModel model, EntitlementEntry entry)
            : base(entry, model.Amount)
        {
            this.player = player;
        }

        /// <summary>
        /// Create a new <see cref="ICharacterEntitlement"/> from supplied <see cref="EntitlementEntry"/> and value.
        /// </summary>
        public CharacterEntitlement(IPlayer player, EntitlementEntry entry, uint value)
            : base(entry, value)
        {
            this.player = player;
            saveMask.Mark(SaveMask.Create);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage the character entitlement change and acknowledge it after the character database commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            VersionedSaveMaskSnapshot<SaveMask> snapshot = saveMask.Capture();
            SaveMask mask = snapshot.Mask;
            if (mask == SaveMask.None)
                return;

            var model = new CharacterEntitlementModel
            {
                Id            = player.CharacterId,
                EntitlementId = (byte)Type,
                Amount        = amount
            };

            if ((mask & SaveMask.Create) != 0)
                context.Add(model);
            else
            {
                EntityEntry<CharacterEntitlementModel> entity = context.Attach(model);
                entity.Property(p => p.Amount).IsModified = true;
            }

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }

        public ServerEntitlement Build()
        {
            return new ServerEntitlement
            {
                Entitlement = Type,
                Count       = Amount
            };
        }
    }
}
