using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Achievement;
using NexusForever.Game.Persistence;
using AchievementNetworkModel = NexusForever.Network.World.Message.Model.Achievement.Achievement;

namespace NexusForever.Game.Achievement
{
    public class Achievement<T> : IAchievement
        where T : class, IAchievementModel, new()
    {
        [Flags]
        protected enum SaveMask
        {
            None          = 0x00,
            Create        = 0x01,
            Data0         = 0x02,
            Data1         = 0x04,
            TimeCompleted = 0x08
        }

        public IAchievementInfo Info { get; }
        public ushort Id => Info.Id;

        public uint Data0
        {
            get => data0;
            set
            {
                data0 = value;
                saveMask.Mark(SaveMask.Data0);
            }
        }

        private uint data0;

        public uint Data1
        {
            get => data1;
            set
            {
                data1 = value;
                saveMask.Mark(SaveMask.Data1);
            }
        }

        private uint data1;

        public DateTime? DateCompleted
        {
            get => dateCompleted;
            set
            {
                dateCompleted = value;
                saveMask.Mark(SaveMask.TimeCompleted);
            }
        }

        private DateTime? dateCompleted;

        protected readonly VersionedSaveMask<SaveMask> saveMask;

        // this can either be a characterId or guildId depending on the achievement type
        private readonly ulong ownerId;

        /// <summary>
        /// Create a new <see cref="IAchievement"/> from an existing database model.
        /// </summary>
        public Achievement(IAchievementInfo info, IAchievementModel model)
        {
            ownerId       = model.Id;
            Info          = info;
            data0         = model.Data0;
            data1         = model.Data1;
            dateCompleted = model.DateCompleted;
            saveMask      = new VersionedSaveMask<SaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="IAchievement"/> from <see cref="IAchievementInfo"/> and supplied data.
        /// </summary>
        public Achievement(ulong ownerId, IAchievementInfo info)
        {
            this.ownerId = ownerId;
            Info         = info;

            saveMask = new VersionedSaveMask<SaveMask>(SaveMask.Create);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage achievement changes and acknowledge them after the character database commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<SaveMask> snapshot = saveMask.Capture();
            SaveMask stagedMask = snapshot.Mask;
            if (stagedMask == SaveMask.None)
                return;

            uint stagedData0 = data0;
            uint stagedData1 = data1;
            DateTime? stagedDateCompleted = dateCompleted;

            if ((stagedMask & SaveMask.Create) != 0)
            {
                context.Add(new T
                {
                    Id            = ownerId,
                    AchievementId = Id,
                    Data0         = stagedData0,
                    Data1         = stagedData1,
                    DateCompleted = stagedDateCompleted
                });
            }
            else
            {
                var model = new T
                {
                    Id            = ownerId,
                    AchievementId = Id
                };

                EntityEntry<T> entity = context.Attach(model);
                if ((stagedMask & SaveMask.Data0) != 0)
                {
                    model.Data0 = stagedData0;
                    entity.Property(p => p.Data0).IsModified = true;
                }
                if ((stagedMask & SaveMask.Data1) != 0)
                {
                    model.Data1 = stagedData1;
                    entity.Property(p => p.Data1).IsModified = true;
                }
                if ((stagedMask & SaveMask.TimeCompleted) != 0)
                {
                    model.DateCompleted = stagedDateCompleted;
                    entity.Property(p => p.DateCompleted).IsModified = true;
                }
            }

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }

        /// <summary>
        /// Build a network model from <see cref="IAchievement"/>
        /// </summary>
        public AchievementNetworkModel Build()
        {
            return new()
            {
                AchievementId = Id,
                Data0         = Data0,
                Data1         = Data1,
                DateCompleted = (ulong)(DateCompleted?.ToFileTimeUtc() ?? 0L)
            };
        }

        /// <summary>
        /// Returns if <see cref="IAchievement"/> has been completed.
        /// </summary>
        public bool IsComplete()
        {
            if (DateCompleted != null)
                return true;

            if (Info.ChecklistEntries.Count == 0)
                return Data0 == Info.Entry.Value;

            return Info.ChecklistEntries.All(entry => (Data0 & (1u << (int)entry.Bit)) != 0);
        }
    }
}
