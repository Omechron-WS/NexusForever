using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Quest
{
    public class QuestObjective : IQuestObjective
    {
        [Flags]
        public enum QuestObjectiveSaveMask
        {
            None     = 0x00,
            Create   = 0x01,
            Progress = 0x02,
            Timer    = 0x04
        }

        public IQuestInfo QuestInfo { get; }
        public IQuestObjectiveInfo ObjectiveInfo { get; }

        public byte Index { get; }

        public uint Progress
        {
            get => progress;
            set
            {
                progress = value;
                saveMask.Mark(QuestObjectiveSaveMask.Progress);
            }
        }

        private uint progress;

        public uint? Timer
        {
            get => timer;
            set
            {
                timer = value;
                saveMask.Mark(QuestObjectiveSaveMask.Timer);
            }
        }

        private uint? timer;

        private readonly VersionedSaveMask<QuestObjectiveSaveMask> saveMask;

        private readonly IPlayer player;

        /// <summary>
        /// Create a new <see cref="IQuestObjective"/> from an existing database model.
        /// </summary>
        public QuestObjective(IPlayer owner, IQuestInfo questInfo, IQuestObjectiveInfo objectiveInfo, CharacterQuestObjectiveModel model)
        {
            player        = owner;

            QuestInfo     = questInfo;
            ObjectiveInfo = objectiveInfo;
            Index         = model.Index;
            progress      = model.Progress;
            timer         = model.Timer;
            saveMask      = new VersionedSaveMask<QuestObjectiveSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="IQuestObjective"/> from supplied <see cref="QuestObjectiveEntry"/>.
        /// </summary>
        public QuestObjective(IPlayer owner, IQuestInfo questInfo, IQuestObjectiveInfo objectiveInfo, byte index)
        {
            player        = owner;

            QuestInfo     = questInfo;
            ObjectiveInfo = objectiveInfo;
            Index         = index;

            if (objectiveInfo.Entry.MaxTimeAllowedMS != 0u)
            {
                // TODO
            }

            saveMask = new VersionedSaveMask<QuestObjectiveSaveMask>(QuestObjectiveSaveMask.Create);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage objective changes and acknowledge them after the character database commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<QuestObjectiveSaveMask> snapshot = saveMask.Capture();
            QuestObjectiveSaveMask stagedMask = snapshot.Mask;
            if (stagedMask == QuestObjectiveSaveMask.None)
                return;

            uint stagedProgress = progress;
            uint? stagedTimer = timer;

            if ((stagedMask & QuestObjectiveSaveMask.Create) != 0)
            {
                context.Add(new CharacterQuestObjectiveModel
                {
                    Id       = player.CharacterId,
                    QuestId  = (ushort)QuestInfo.Entry.Id,
                    Index    = Index,
                    Progress = stagedProgress,
                    Timer    = stagedTimer
                });
            }
            else
            {
                var model = new CharacterQuestObjectiveModel
                {
                    Id      = player.CharacterId,
                    QuestId = (ushort)QuestInfo.Entry.Id,
                    Index   = Index
                };

                EntityEntry<CharacterQuestObjectiveModel> entity = context.Entry(model);
                if ((stagedMask & QuestObjectiveSaveMask.Progress) != 0)
                {
                    model.Progress = stagedProgress;
                    entity.Property(p => p.Progress).IsModified = true;
                }

                if ((stagedMask & QuestObjectiveSaveMask.Timer) != 0)
                {
                    model.Timer = stagedTimer;
                    entity.Property(p => p.Timer).IsModified = true;
                }
            }

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }

        /// <summary>
        /// Enqueue the objective to be inserted after its parent quest deletion was cancelled.
        /// </summary>
        public void EnqueueCreate()
        {
            saveMask.Mark(QuestObjectiveSaveMask.Create);
        }

        public void Update(double lastTick)
        {
            // TODO: update timer
        }

        private bool IsDynamic()
        {
            // dynamic objectives have their progress based on percentage rather than count
            return ObjectiveInfo.Type is QuestObjectiveType.KillCreature
                    or QuestObjectiveType.KillTargetGroups
                    or QuestObjectiveType.Unknown15
                    or QuestObjectiveType.KillTargetGroup
                    or QuestObjectiveType.KillCreature2
                && ObjectiveInfo.Entry.Count > 1u
                && !ObjectiveInfo.HasUnknown0200();
        }

        /// <summary>
        /// Return if the objective has been completed.
        /// </summary>
        public bool IsComplete()
        {
            return progress >= GetMaxValue();
        }

        private uint GetMaxValue()
        {
            return IsDynamic() ? 1000u : ObjectiveInfo.Entry.Count;
        }

        /// <summary>
        /// Update object progress with supplied update.
        /// </summary>
        public void ObjectiveUpdate(uint update)
        {
            if (IsDynamic())
                update = (uint)(((float)update / ObjectiveInfo.Entry.Count) * 1000f);

            Progress = Math.Min(progress + update, GetMaxValue());
        }

       
        public void Complete()
        {
            Progress = GetMaxValue();
        }
    }
}
