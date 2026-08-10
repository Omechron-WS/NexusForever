using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Persistence;

namespace NexusForever.Game.Entity
{
    public class Bone : IBone
    {
        [Flags]
        public enum BoneSaveMask
        {
            None   = 0x0000,
            Create = 0x0001,
            Modify = 0x0002,
            Delete = 0x0004
        }

        public ulong Owner { get; }
        public byte BoneIndex { get; }

        public float BoneValue
        {
            get => boneValue;
            set
            {
                if (boneValue != value)
                {
                    boneValue = value;
                    saveMask.Mark(BoneSaveMask.Modify);
                }
            }
        }
        private float boneValue;

        public bool PendingDelete => (saveMask.Current & BoneSaveMask.Delete) != 0;

        private VersionedSaveMask<BoneSaveMask> saveMask = new();

        /// <summary>
        /// Create a new <see cref="IBone"/> from database model.
        /// </summary>
        public Bone(CharacterBoneModel model)
        {
            Owner     = model.Id;
            BoneIndex = model.BoneIndex;
            boneValue = model.Bone;

            saveMask  = new VersionedSaveMask<BoneSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="IBone"/> from supplied data.
        /// </summary>
        public Bone(ulong characterId, byte boneIndex, float value)
        {
            Owner     = characterId;
            BoneIndex = boneIndex;
            boneValue = value;

            saveMask  = new VersionedSaveMask<BoneSaveMask>(BoneSaveMask.Create);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage bone changes and register their successful-commit acknowledgements.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            Save(context, commitScope, null);
        }

        /// <summary>
        /// Stage bone changes and invoke the supplied action when a requested deletion commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<BoneSaveMask> snapshot = saveMask.Capture();
            BoneSaveMask mask = snapshot.Mask;
            if (mask == BoneSaveMask.None)
                return;

            void Acknowledge()
            {
                bool deleteStillRequested = PendingDelete;
                saveMask.Acknowledge(snapshot);

                if ((mask & BoneSaveMask.Delete) == 0)
                    return;

                if (deleteStillRequested)
                    deleteAcknowledged?.Invoke();
                else
                    saveMask.Mark(BoneSaveMask.Create);
            }

            if ((mask & (BoneSaveMask.Create | BoneSaveMask.Delete)) ==
                (BoneSaveMask.Create | BoneSaveMask.Delete))
            {
                commitScope.Register(Acknowledge);
                return;
            }

            var model = new CharacterBoneModel
            {
                Id        = Owner,
                BoneIndex = BoneIndex,
                Bone      = BoneValue
            };

            if ((mask & BoneSaveMask.Create) != 0)
                context.Add(model);
            else if ((mask & BoneSaveMask.Delete) != 0)
                context.Entry(model).State = EntityState.Deleted;
            else if ((mask & BoneSaveMask.Modify) != 0)
            {
                EntityEntry<CharacterBoneModel> entity = context.Attach(model);
                entity.Property(e => e.Bone).IsModified = true;
            }

            commitScope.Register(Acknowledge);
        }

        public void Delete()
        {
            EnqueueDelete(true);
        }

        /// <summary>
        /// Enqueue or cancel deletion of this bone.
        /// </summary>
        public void EnqueueDelete(bool set)
        {
            if (set)
                saveMask.Mark(BoneSaveMask.Delete);
            else
                saveMask.Clear(BoneSaveMask.Delete);
        }
    }
}
