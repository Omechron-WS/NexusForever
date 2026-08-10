using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Persistence;

namespace NexusForever.Game.Entity
{
    public class Customisation : ICustomisation
    {
        [Flags]
        public enum CustomisationSaveMask
        {
            None   = 0x0000,
            Create = 0x0001,
            Modify = 0x0002,
            Delete = 0x0004
        }

        public ulong CharacterId { get; }
        public uint Label { get; }

        public uint Value
        {
            get => value;
            set
            {
                if (this.value != value)
                {
                    this.value = value;
                    saveMask.Mark(CustomisationSaveMask.Modify);
                }
            }
        }
        private uint value;

        public bool PendingDelete => (saveMask.Current & CustomisationSaveMask.Delete) != 0;

        private VersionedSaveMask<CustomisationSaveMask> saveMask = new();

        /// <summary>
        /// Create a new <see cref="ICustomisation"/> from database model.
        /// </summary>
        public Customisation(CharacterCustomisationModel model)
        {
            CharacterId = model.Id;
            Label       = model.Label;
            value       = model.Value;

            saveMask    = new VersionedSaveMask<CustomisationSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="ICustomisation"/> from supplied data.
        /// </summary>
        public Customisation(ulong characterId, uint label, uint value)
        {
            CharacterId = characterId;
            Label       = label;
            this.value  = value;

            saveMask    = new VersionedSaveMask<CustomisationSaveMask>(CustomisationSaveMask.Create);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage customisation changes and register their successful-commit acknowledgements.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            Save(context, commitScope, null);
        }

        /// <summary>
        /// Stage customisation changes and invoke the supplied action when a requested deletion commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<CustomisationSaveMask> snapshot = saveMask.Capture();
            CustomisationSaveMask mask = snapshot.Mask;
            if (mask == CustomisationSaveMask.None)
                return;

            void Acknowledge()
            {
                bool deleteStillRequested = PendingDelete;
                saveMask.Acknowledge(snapshot);

                if ((mask & CustomisationSaveMask.Delete) == 0)
                    return;

                if (deleteStillRequested)
                    deleteAcknowledged?.Invoke();
                else
                    saveMask.Mark(CustomisationSaveMask.Create);
            }

            if ((mask & (CustomisationSaveMask.Create | CustomisationSaveMask.Delete)) ==
                (CustomisationSaveMask.Create | CustomisationSaveMask.Delete))
            {
                commitScope.Register(Acknowledge);
                return;
            }

            var model = new CharacterCustomisationModel
            {
                Id    = CharacterId,
                Label = Label,
                Value = Value
            };

            if ((mask & CustomisationSaveMask.Create) != 0)
                context.Add(model);
            else if ((mask & CustomisationSaveMask.Delete) != 0)
                context.Entry(model).State = EntityState.Deleted;
            else if ((mask & CustomisationSaveMask.Modify) != 0)
            {
                EntityEntry<CharacterCustomisationModel> entity = context.Attach(model);
                entity.Property(e => e.Value).IsModified = true;
            }

            commitScope.Register(Acknowledge);
        }

        public void Delete()
        {
            EnqueueDelete(true);
        }

        /// <summary>
        /// Enqueue or cancel deletion of this customisation.
        /// </summary>
        public void EnqueueDelete(bool set)
        {
            if (set)
                saveMask.Mark(CustomisationSaveMask.Delete);
            else
                saveMask.Clear(CustomisationSaveMask.Delete);
        }
    }
}
