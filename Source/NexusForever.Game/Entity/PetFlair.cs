using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Persistence;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Entity
{
    public class PetFlair : IPetFlair
    {
        [Flags]
        private enum PetFlairSaveMask
        {
            None   = 0x00,
            Create = 0x01
        }

        public ulong Owner { get; }
        public PetFlairEntry Entry { get; }

        private readonly VersionedSaveMask<PetFlairSaveMask> saveMask;

        /// <summary>
        /// Create a new <see cref="IPetFlair"/> from existing <see cref="CharacterPetFlairModel"/> database model.
        /// </summary>
        public PetFlair(CharacterPetFlairModel model)
        {
            Owner    = model.Id;
            Entry    = GameTableManager.Instance.PetFlair.GetEntry(model.PetFlairId);
            saveMask = new VersionedSaveMask<PetFlairSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="IPetFlair"/> from supplied <see cref="PetFlairEntry"/>.
        /// </summary>
        public PetFlair(ulong owner, PetFlairEntry entry)
        {
            Entry    = entry;
            Owner    = owner;
            saveMask = new VersionedSaveMask<PetFlairSaveMask>(PetFlairSaveMask.Create);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage pet flair changes and register their successful-commit acknowledgements.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<PetFlairSaveMask> snapshot = saveMask.Capture();
            if ((snapshot.Mask & PetFlairSaveMask.Create) == 0)
                return;

            var model = new CharacterPetFlairModel
            {
                Id         = Owner,
                PetFlairId = Entry.Id
            };

            context.Add(model);
            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }
    }
}
