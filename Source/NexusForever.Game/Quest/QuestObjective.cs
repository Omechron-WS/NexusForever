using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable.Model;
using NLog;

namespace NexusForever.Game.Quest
{
    public class QuestObjective : IQuestObjective
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly ConcurrentDictionary<uint, byte> reportedOversizedChecklists = new();

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
                if (progress == value)
                    return;

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
        private readonly ImmutableHashSet<uint> targetIds;

        /// <summary>
        /// Create a new <see cref="IQuestObjective"/> from an existing database model.
        /// </summary>
        public QuestObjective(IPlayer owner, IQuestInfo questInfo, IQuestObjectiveInfo objectiveInfo, CharacterQuestObjectiveModel model)
            : this(owner, questInfo, objectiveInfo, model, null)
        {
        }

        internal QuestObjective(
            IPlayer owner,
            IQuestInfo questInfo,
            IQuestObjectiveInfo objectiveInfo,
            CharacterQuestObjectiveModel model,
            IAssetManager assetManager)
        {
            player        = owner;

            QuestInfo     = questInfo;
            ObjectiveInfo = objectiveInfo;
            Index         = model.Index;
            progress      = model.Progress;
            timer         = model.Timer;
            saveMask      = new VersionedSaveMask<QuestObjectiveSaveMask>();
            targetIds     = BuildTargets(assetManager);
            ReportOversizedChecklist();
        }

        /// <summary>
        /// Create a new <see cref="IQuestObjective"/> from supplied <see cref="QuestObjectiveEntry"/>.
        /// </summary>
        public QuestObjective(IPlayer owner, IQuestInfo questInfo, IQuestObjectiveInfo objectiveInfo, byte index)
            : this(owner, questInfo, objectiveInfo, index, null)
        {
        }

        internal QuestObjective(
            IPlayer owner,
            IQuestInfo questInfo,
            IQuestObjectiveInfo objectiveInfo,
            byte index,
            IAssetManager assetManager)
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
            targetIds = BuildTargets(assetManager);
            ReportOversizedChecklist();
        }

        private void ReportOversizedChecklist()
        {
            if (!IsChecklist()
                || ObjectiveInfo.Entry.Count <= 32u
                || !reportedOversizedChecklists.TryAdd(ObjectiveInfo.Id, 0))
                return;

            log.Error(
                $"Quest objective {ObjectiveInfo.Id} for quest {QuestInfo.Entry.Id} requires " +
                $"{ObjectiveInfo.Entry.Count} checklist bits, but build-16042 progress storage supports only 32; " +
                "the objective will remain incomplete.");
        }

        private ImmutableHashSet<uint> BuildTargets(IAssetManager assetManager)
        {
            if (!UsesTargetGroups())
                return ImmutableHashSet<uint>.Empty;

            assetManager ??= AssetManager.Instance;
            return (assetManager.GetQuestObjectiveTargetIds(ObjectiveInfo.Id) ?? ImmutableList<uint>.Empty)
                .ToImmutableHashSet();
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
            return (ObjectiveInfo.Type is QuestObjectiveType.KillCreature
                    or QuestObjectiveType.KillTargetGroups
                    or QuestObjectiveType.Unknown15
                    or QuestObjectiveType.KillTargetGroup
                    or QuestObjectiveType.KillCreature2)
                && ObjectiveInfo.Entry.Count > 1u
                && !ObjectiveInfo.HasUnknown0200();
        }

        /// <summary>
        /// Return if the objective stores progress as a checklist bitmask.
        /// </summary>
        public bool IsChecklist()
        {
            return ObjectiveInfo.Type is QuestObjectiveType.ActivateTargetGroupChecklist
                or QuestObjectiveType.Unknown10;
        }

        private bool UsesTargetGroups()
        {
            return ObjectiveInfo.Type is QuestObjectiveType.ActivateTargetGroup
                or QuestObjectiveType.ActivateTargetGroupChecklist
                or QuestObjectiveType.KillTargetGroup
                or QuestObjectiveType.KillTargetGroups
                or QuestObjectiveType.TalkToTargetGroup
                or QuestObjectiveType.Unknown10
                || ObjectiveInfo.Type == QuestObjectiveType.ActivateEntity
                    && ObjectiveInfo.Entry.TargetGroupIdRewardPane != 0u;
        }

        /// <summary>
        /// Return if the supplied identifier is a direct or expanded objective target.
        /// </summary>
        public bool IsTarget(uint id)
        {
            return ObjectiveInfo.Entry.Data == id || targetIds.Contains(id);
        }

        /// <summary>
        /// Return if the objective has been completed.
        /// </summary>
        public bool IsComplete()
        {
            if (IsChecklist())
            {
                uint count = ObjectiveInfo.Entry.Count;
                if (count > 32u)
                    return false;

                return BitOperations.PopCount(progress & GetChecklistMask(count)) >= count;
            }

            return progress >= GetMaxValue();
        }

        private uint GetMaxValue()
        {
            return IsDynamic() ? 1000u : ObjectiveInfo.Entry.Count;
        }

        private static uint GetChecklistMask(uint count)
        {
            if (count == 0u)
                return 0u;
            if (count == 32u)
                return uint.MaxValue;

            return (1u << (int)count) - 1u;
        }

        /// <summary>
        /// Update object progress with supplied update.
        /// </summary>
        public void ObjectiveUpdate(uint update)
        {
            if (IsChecklist())
            {
                uint count = ObjectiveInfo.Entry.Count;
                if (count > 32u || update >= count || update >= 32u)
                    return;

                uint bit = 1u << (int)update;
                if ((progress & bit) != 0u)
                    return;

                Progress = progress | bit;
                return;
            }

            uint maximum = GetMaxValue();
            if (progress >= maximum || update == 0u)
                return;

            if (IsDynamic())
            {
                uint count = ObjectiveInfo.Entry.Count;
                if (count == 0u)
                    return;

                // Build 16042 stores dynamic progress in thousandths. Counts up to 1,000 have an
                // exact inverse, so reconstruct the completed count before projecting the new total.
                ulong completed = ((ulong)progress * count + maximum - 1u) / maximum;
                ulong dynamicRemaining = count - Math.Min((ulong)count, completed);
                completed += Math.Min((ulong)update, dynamicRemaining);

                uint projected = (uint)(completed * maximum / count);
                if (projected <= progress && progress < maximum)
                    projected = progress + 1u;

                Progress = Math.Min(projected, maximum);
                return;
            }

            uint remaining = maximum - progress;
            Progress = update >= remaining ? maximum : progress + update;
        }

       
        public void Complete()
        {
            if (IsChecklist())
            {
                uint count = ObjectiveInfo.Entry.Count;
                if (count > 32u)
                    return;

                Progress = GetChecklistMask(count);
                return;
            }

            Progress = GetMaxValue();
        }
    }
}
