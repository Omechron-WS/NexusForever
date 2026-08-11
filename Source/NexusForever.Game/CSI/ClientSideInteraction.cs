using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.CSI;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.CSI;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NLog;

namespace NexusForever.Game.CSI
{
    public class ClientSideInteraction : IClientSideInteraction
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public uint ClientUniqueId { get; }
        public IWorldEntity ActivateUnit { get; }
        public CSIType CsiType { get; }
        public ClientSideInteractionEntry Entry { get; }

        private readonly IPlayer owner;
        private readonly IAssetManager assetManager;
        private readonly HashSet<uint> suppressedActivateEntityObjectiveIds = [];
        private int terminalState;

        /// <summary>
        /// Create a new CSI for the given player activating the given entity.
        /// Optionally loads the CSI entry from the GameTable via the spell's ClientSideInteractionId.
        /// </summary>
        public ClientSideInteraction(
            IPlayer owner,
            IWorldEntity activateUnit,
            uint clientUniqueId,
            uint clientSideInteractionId = 0,
            IAssetManager assetManager = null)
        {
            this.owner        = owner ?? throw new ArgumentNullException(nameof(owner));
            this.assetManager = assetManager;
            ActivateUnit      = activateUnit ?? throw new ArgumentNullException(nameof(activateUnit));
            ClientUniqueId    = clientUniqueId;

            if (clientSideInteractionId > 0)
                Entry = GameTableManager.Instance.ClientSideInteraction.GetEntry(clientSideInteractionId);

            CsiType = Entry != null ? (CSIType)Entry.InteractionType : CSIType.Interaction;
        }

        /// <inheritdoc />
        public bool IsValid()
        {
            return ClientSideInteractionValidator.IsValid(owner, ActivateUnit);
        }

        /// <inheritdoc />
        public bool TrySuppressActivateEntityObjective(uint questObjectiveId)
        {
            if (questObjectiveId == 0u || Volatile.Read(ref terminalState) != 0)
                return false;

            lock (suppressedActivateEntityObjectiveIds)
            {
                if (Volatile.Read(ref terminalState) != 0)
                    return false;

                return suppressedActivateEntityObjectiveIds.Add(questObjectiveId);
            }
        }

        /// <summary>
        /// Called when the client reports CSI success.
        /// </summary>
        public bool TriggerSuccess()
        {
            if (!IsValid())
                return TriggerFail();

            return CompleteSuccess();
        }

        /// <inheritdoc />
        public bool CompleteSuccess()
        {
            if (Interlocked.CompareExchange(ref terminalState, 1, 0) != 0)
                return false;

            IReadOnlySet<uint> excludedObjectiveIds = CaptureSuppressedActivateEntityObjectives();
            UpdateObjective(
                QuestObjectiveType.ActivateEntity,
                ActivateUnit.CreatureId,
                excludedObjectiveIds);
            UpdateObjective(QuestObjectiveType.SucceedCSI, ActivateUnit.CreatureId);

            try
            {
                IEnumerable<uint> targetGroups = assetManager?.GetTargetGroupsForCreatureId(ActivateUnit.CreatureId);
                if (targetGroups != null)
                {
                    foreach (uint targetGroupId in targetGroups)
                        UpdateObjective(QuestObjectiveType.ActivateTargetGroup, targetGroupId);
                }
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to resolve target groups for client-side interaction target {ActivateUnit.CreatureId}.");
            }

            try
            {
                ActivateUnit.OnActivateSuccess(owner);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Client-side interaction success callback failed for entity {ActivateUnit.Guid}.");
            }

            return true;
        }

        /// <summary>
        /// Called when the client reports CSI failure.
        /// </summary>
        public bool TriggerFail()
        {
            if (Interlocked.CompareExchange(ref terminalState, 2, 0) != 0)
                return false;

            try
            {
                ActivateUnit.OnActivateFail(owner);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Client-side interaction failure callback failed for entity {ActivateUnit.Guid}.");
            }

            return true;
        }

        private IReadOnlySet<uint> CaptureSuppressedActivateEntityObjectives()
        {
            lock (suppressedActivateEntityObjectiveIds)
            {
                return suppressedActivateEntityObjectiveIds.Count == 0
                    ? null
                    : suppressedActivateEntityObjectiveIds.ToHashSet();
            }
        }

        private void UpdateObjective(
            QuestObjectiveType type,
            uint data,
            IReadOnlySet<uint> excludedObjectiveIds = null)
        {
            try
            {
                if (excludedObjectiveIds is { Count: > 0 })
                    owner.QuestManager?.ObjectiveUpdate(type, data, 1u, excludedObjectiveIds);
                else
                    owner.QuestManager?.ObjectiveUpdate(type, data, 1u);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to update {type} for client-side interaction target {ActivateUnit.CreatureId}.");
            }
        }
    }
}
