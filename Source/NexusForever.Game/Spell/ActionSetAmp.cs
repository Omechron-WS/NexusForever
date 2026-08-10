using Microsoft.EntityFrameworkCore;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Persistence;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Spell
{
    public class ActionSetAmp : IActionSetAmp
    {
        [Flags]
        public enum AmpSaveMask
        {
            None    = 0x0000,
            Create  = 0x0001,
            Delete  = 0x0002
        }

        public EldanAugmentationEntry Entry { get; set; }

        /// <summary>
        /// Returns if <see cref="IActionSetAmp"/> is enqueued to be saved to the database.
        /// </summary>
        public bool PendingCreate => (saveMask.Current & AmpSaveMask.Create) != 0;

        /// <summary>
        /// Returns if <see cref="IActionSetAmp"/> is enqueued to be deleted from the database.
        /// </summary>
        public bool PendingDelete => (saveMask.Current & AmpSaveMask.Delete) != 0;

        private readonly VersionedSaveMask<AmpSaveMask> saveMask;
        private readonly ActionSet actionSet;

        /// <summary>
        /// Create a new <see cref="IActionSetAmp"/> from supplied <see cref="EldanAugmentationEntry"/>.
        /// </summary>
        public ActionSetAmp(ActionSet actionSet, EldanAugmentationEntry entry, bool isDirty)
        {
            Entry          = entry;
            this.actionSet = actionSet;

            saveMask = isDirty
                ? new VersionedSaveMask<AmpSaveMask>(AmpSaveMask.Create)
                : new VersionedSaveMask<AmpSaveMask>();
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage AMP changes and register their successful-commit acknowledgements.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            Save(context, commitScope, null);
        }

        /// <summary>
        /// Stage AMP changes and invoke the supplied action when a requested deletion commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<AmpSaveMask> snapshot = saveMask.Capture();
            AmpSaveMask mask = snapshot.Mask;
            if (mask == AmpSaveMask.None)
                return;

            void Acknowledge()
            {
                bool deleteStillRequested = PendingDelete;
                saveMask.Acknowledge(snapshot);

                if ((mask & AmpSaveMask.Delete) == 0)
                    return;

                if (deleteStillRequested)
                    deleteAcknowledged?.Invoke();
                else
                    saveMask.Mark(AmpSaveMask.Create);
            }

            if ((mask & (AmpSaveMask.Create | AmpSaveMask.Delete)) ==
                (AmpSaveMask.Create | AmpSaveMask.Delete))
            {
                commitScope.Register(Acknowledge);
                return;
            }

            var model = new CharacterActionSetAmpModel
            {
                Id        = actionSet.Owner,
                SpecIndex = actionSet.Index,
                AmpId     = (byte)Entry.Id
            };

            if ((mask & AmpSaveMask.Create) != 0)
                context.Add(model);
            else if ((mask & AmpSaveMask.Delete) != 0)
                context.Entry(model).State = EntityState.Deleted;

            commitScope.Register(Acknowledge);
        }

        /// <summary>
        /// Enqueue or dequeue <see cref="IActionSetAmp"/> to be deleted from the database.
        /// </summary>
        public void EnqueueDelete(bool set)
        {
            if (set)
                saveMask.Mark(AmpSaveMask.Delete);
            else
                saveMask.Clear(AmpSaveMask.Delete);
        }
    }
}
