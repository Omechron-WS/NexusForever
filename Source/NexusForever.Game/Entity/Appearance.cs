using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Entity
{
    public class Appearance : IAppearance
    {
        [Flags]
        public enum AppearanceSaveMask
        {
            None   = 0x0000,
            Create = 0x0001,
            Modify = 0x0002,
            Delete = 0x0004
        }

        public ulong Owner { get; }
        public ItemSlot ItemSlot { get; }

        public ushort DisplayId
        {
            get => displayId;
            set
            {
                if (displayId != value)
                {
                    displayId = value;
                    saveMask.Mark(AppearanceSaveMask.Modify);
                }
            }
        }
        private ushort displayId;

        public bool PendingDelete => (saveMask.Current & AppearanceSaveMask.Delete) != 0;

        private VersionedSaveMask<AppearanceSaveMask> saveMask = new();

        /// <summary>
        /// Create a new <see cref="IAppearance"/> from database model.
        /// </summary>
        public Appearance(CharacterAppearanceModel model)
        {
            Owner     = model.Id;
            ItemSlot  = (ItemSlot)model.Slot;
            displayId = model.DisplayId;

            saveMask  = new VersionedSaveMask<AppearanceSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="IAppearance"/> from supplied data.
        /// </summary>
        public Appearance(ulong characterId, ItemSlot itemSlot, ushort displayId)
        {
            Owner          = characterId;
            ItemSlot       = itemSlot;
            this.displayId = displayId;

            saveMask       = new VersionedSaveMask<AppearanceSaveMask>(AppearanceSaveMask.Create);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage appearance changes and register their successful-commit acknowledgements.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            Save(context, commitScope, null);
        }

        /// <summary>
        /// Stage appearance changes and invoke the supplied action when a requested deletion commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<AppearanceSaveMask> snapshot = saveMask.Capture();
            AppearanceSaveMask mask = snapshot.Mask;
            if (mask == AppearanceSaveMask.None)
                return;

            void Acknowledge()
            {
                bool deleteStillRequested = PendingDelete;
                saveMask.Acknowledge(snapshot);

                if ((mask & AppearanceSaveMask.Delete) == 0)
                    return;

                if (deleteStillRequested)
                    deleteAcknowledged?.Invoke();
                else
                    saveMask.Mark(AppearanceSaveMask.Create);
            }

            if ((mask & (AppearanceSaveMask.Create | AppearanceSaveMask.Delete)) ==
                (AppearanceSaveMask.Create | AppearanceSaveMask.Delete))
            {
                commitScope.Register(Acknowledge);
                return;
            }

            var model = new CharacterAppearanceModel
            {
                Id        = Owner,
                Slot      = (byte)ItemSlot,
                DisplayId = DisplayId
            };

            if ((mask & AppearanceSaveMask.Create) != 0)
                context.Add(model);
            else if ((mask & AppearanceSaveMask.Delete) != 0)
                context.Entry(model).State = EntityState.Deleted;
            else if ((mask & AppearanceSaveMask.Modify) != 0)
            {
                EntityEntry<CharacterAppearanceModel> entity = context.Attach(model);
                entity.Property(e => e.DisplayId).IsModified = true;
            }

            commitScope.Register(Acknowledge);
        }

        public void Delete()
        {
            EnqueueDelete(true);
        }

        /// <summary>
        /// Enqueue or cancel deletion of this appearance.
        /// </summary>
        public void EnqueueDelete(bool set)
        {
            if (set)
                saveMask.Mark(AppearanceSaveMask.Delete);
            else
                saveMask.Clear(AppearanceSaveMask.Delete);
        }
    }
}
