using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Spell
{
    public class ActionSetShortcut : IActionSetShortcut
    {
        [Flags]
        public enum ShortcutSaveMask
        {
            None         = 0x0000,
            Create       = 0x0001,
            Delete       = 0x0002,
            ObjectId     = 0x0004,
            Tier         = 0x0008,
            ShortcutType = 0x0010
        }

        public UILocation Location { get; }

        public ShortcutType ShortcutType
        {
            get => shortcutType;
            set
            {
                if (value != shortcutType)
                    saveMask.Mark(ShortcutSaveMask.ShortcutType);
                shortcutType = value;
            }
        }

        private ShortcutType shortcutType;

        public uint ObjectId
        {
            get => objectId;
            set
            {
                if (value != objectId)
                    saveMask.Mark(ShortcutSaveMask.ObjectId);
                objectId = value;
            }
        }

        private uint objectId;

        public byte Tier
        {
            get => tier;
            set
            {
                if (value != tier)
                    saveMask.Mark(ShortcutSaveMask.Tier);
                tier = value;
            }
        }

        private byte tier;

        /// <summary>
        /// Returns if <see cref="IActionSetShortcut"/> is enqueued to be saved to the database.
        /// </summary>
        public bool PendingCreate => (saveMask.Current & ShortcutSaveMask.Create) != 0;

        /// <summary>
        /// Returns if <see cref="IActionSetShortcut"/> is enqueued to be deleted from the database.
        /// </summary>
        public bool PendingDelete => (saveMask.Current & ShortcutSaveMask.Delete) != 0;

        private VersionedSaveMask<ShortcutSaveMask> saveMask = new();
        private readonly ActionSet actionSet;

        /// <summary>
        /// Create a new <see cref="IActionSetShortcut"/> from an existing database model.
        /// </summary>
        public ActionSetShortcut(ActionSet actionSet, CharacterActionSetShortcutModel model)
        {
            this.actionSet = actionSet;
            Location       = (UILocation)model.Location;
            shortcutType   = (ShortcutType)model.ShortcutType;
            objectId       = model.ObjectId;
            tier           = model.Tier;
            saveMask       = new VersionedSaveMask<ShortcutSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="IActionSetShortcut"/>.
        /// </summary>
        public ActionSetShortcut(ActionSet actionSet, UILocation location, ShortcutType shortcutType, uint objectId, byte tier)
        {
            this.actionSet = actionSet;
            Location       = location;
            this.shortcutType = shortcutType;
            this.objectId     = objectId;
            this.tier         = tier;

            saveMask          = new VersionedSaveMask<ShortcutSaveMask>(ShortcutSaveMask.Create);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage shortcut changes and register their successful-commit acknowledgements.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            Save(context, commitScope, null);
        }

        /// <summary>
        /// Stage shortcut changes and invoke the supplied action when a requested deletion commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<ShortcutSaveMask> snapshot = saveMask.Capture();
            ShortcutSaveMask mask = snapshot.Mask;
            if (mask == ShortcutSaveMask.None)
                return;

            void Acknowledge()
            {
                bool deleteStillRequested = PendingDelete;
                saveMask.Acknowledge(snapshot);

                if ((mask & ShortcutSaveMask.Delete) == 0)
                    return;

                if (deleteStillRequested)
                    deleteAcknowledged?.Invoke();
                else
                    saveMask.Mark(ShortcutSaveMask.Create);
            }

            if ((mask & (ShortcutSaveMask.Create | ShortcutSaveMask.Delete)) ==
                (ShortcutSaveMask.Create | ShortcutSaveMask.Delete))
            {
                commitScope.Register(Acknowledge);
                return;
            }

            if ((mask & ShortcutSaveMask.Create) != 0)
            {
                var model = new CharacterActionSetShortcutModel
                {
                    Id           = actionSet.Owner,
                    SpecIndex    = actionSet.Index,
                    Location     = (ushort)Location,
                    ShortcutType = (byte)ShortcutType,
                    ObjectId     = ObjectId,
                    Tier         = tier
                };

                context.Add(model);
            }
            else
            {
                var model = new CharacterActionSetShortcutModel
                {
                    Id        = actionSet.Owner,
                    SpecIndex = actionSet.Index,
                    Location  = (ushort)Location
                };

                if ((mask & ShortcutSaveMask.Delete) != 0)
                    context.Entry(model).State = EntityState.Deleted;
                else
                {
                    EntityEntry<CharacterActionSetShortcutModel> entity = context.Attach(model);
                    if ((mask & ShortcutSaveMask.ShortcutType) != 0)
                    {
                        model.ShortcutType = (byte)ShortcutType;
                        entity.Property(p => p.ShortcutType).IsModified = true;
                    }

                    if ((mask & ShortcutSaveMask.ObjectId) != 0)
                    {
                        model.ObjectId = ObjectId;
                        entity.Property(p => p.ObjectId).IsModified = true;
                    }

                    if ((mask & ShortcutSaveMask.Tier) != 0)
                    {
                        model.Tier = Tier;
                        entity.Property(p => p.Tier).IsModified = true;
                    }
                }
            }

            commitScope.Register(Acknowledge);
        }

        /// <summary>
        /// Enqueue or dequeue <see cref="IActionSetShortcut"/> to be deleted from the database.
        /// </summary>
        public void EnqueueDelete(bool set)
        {
            if (set)
                saveMask.Mark(ShortcutSaveMask.Delete);
            else
                saveMask.Clear(ShortcutSaveMask.Delete);
        }
    }
}
