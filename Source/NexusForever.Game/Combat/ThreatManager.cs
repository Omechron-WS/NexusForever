using System.Collections;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NLog;

namespace NexusForever.Game.Combat
{
    public class ThreatManager : IThreatManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<uint /*unitId*/, IHostileEntity> hostiles = new();

        private readonly IUnitEntity owner;

        /// <summary>
        /// Initialise <see cref="IThreatManager"/> for a <see cref="IUnitEntity"/>.
        /// </summary>
        public ThreatManager(IUnitEntity owner)
        {
            ArgumentNullException.ThrowIfNull(owner);

            this.owner = owner;
        }

        /// <inheritdoc />
        public void Update(double lastTick)
        {
            foreach (IHostileEntity hostile in hostiles.Values.ToArray())
            {
                hostile.Update(lastTick);
                if (hostile.IsExpired && ReferenceEquals(GetHostile(hostile.HatedUnitId), hostile))
                    RemoveHostile(hostile.HatedUnitId);
            }
        }

        /// <summary>
        /// Returns if any <see cref="IHostileEntity"/> exists in the threat list.
        /// </summary>
        public bool IsThreatened => hostiles.Count > 0;

        /// <summary>
        /// Returns <see cref="IHostileEntity"/> for supplied target if it exists in the threat list.
        /// </summary>
        public IHostileEntity GetHostile(uint target)
        {
            return hostiles.TryGetValue(target, out IHostileEntity hostile) ? hostile : null;
        }

        /// <summary>
        /// Return the <see cref="IHostileEntity"/> with the highest threat in the threat list.
        /// </summary>
        public IHostileEntity GetTopHostile()
        {
            return hostiles.Values
                .OrderByDescending(x => x.Threat)
                .ThenBy(x => x.HatedUnitId)
                .FirstOrDefault();
        }

        /// <summary>
        /// Add threat for the provided <see cref="IUnitEntity"/>.
        /// </summary>
        public void UpdateThreat(IUnitEntity target, int threat)
        {
            if (hostiles.TryGetValue(target.Guid, out IHostileEntity hostile))
            {
                UpdateThreat(hostile, threat);
                if (hostile.IsPvP)
                    target.ThreatManager.GetHostile(owner.Guid)?.Refresh();
            }
            else
                CreateHostile(target, threat);
        }

        /// <summary>
        /// Instantiates a <see cref="IHostileEntity"/> for the given <see cref="IUnitEntity"/>.
        /// </summary>
        private void CreateHostile(IUnitEntity target, int threat)
        {
            IHostileEntity hostile = new HostileEntity(owner, target);
            hostile.UpdateThreat(threat);
            hostiles.Add(hostile.HatedUnitId, hostile);

            owner.OnThreatAddTarget(hostile);

            log.Trace($"Added {hostile.HatedUnitId} to threat list for {owner.Guid}.");
            
            // place owner on threat list of new hostile
            if (target.ThreatManager.GetHostile(owner.Guid) == null)
                target.ThreatManager.UpdateThreat(owner, 1);
        }

        /// <summary>
        /// Internal method to adjust threat for the given <see cref="IHostileEntity"/>.
        /// </summary>
        private void UpdateThreat(IHostileEntity hostile, int threat)
        {
            hostile.UpdateThreat(threat);

            if (hostile.Threat != 0u)
                owner.OnThreatChange(hostile);
            else
                RemoveHostile(hostile.HatedUnitId);
        }

        /// <summary>
        /// Clear the threat list and alert all entities that this <see cref="IUnitEntity"/> has forgotten them.
        /// </summary>
        public void ClearThreatList()
        {
            log.Trace($"Clearing threat list for {owner.Guid}.");

            foreach (IHostileEntity hostile in hostiles.Values.ToList())
                RemoveHostile(hostile.HatedUnitId);
        }

        /// <summary>
        /// Remove the target with the given unit id from this threat list, if they exist. This will alert the entity that they have been forgotten by this <see cref="IUnitEntity"/>.
        /// </summary>
        public void RemoveHostile(uint unitId)
        {
            if (!hostiles.Remove(unitId, out IHostileEntity hostileEntity))
                return;

            IThreatManager reciprocalThreatManager = null;
            ExecuteRemovalAction(() => reciprocalThreatManager = owner.GetVisible<IUnitEntity>(unitId)?.ThreatManager,
                "resolve reciprocal threat relationship", unitId);

            // Despite being an update it looks like this is only sent on remove to set threat to 0.
            // More research is needed to determine whether this should be sent on every threat change.
            ExecuteRemovalAction(() => owner.EnqueueToVisible(new ServerEntityThreatUpdate
            {
                UnitId      = owner.Guid,
                TargetId    = hostileEntity.HatedUnitId,
                ThreatLevel = 0
            }), "publish threat removal", unitId);

            ExecuteRemovalAction(() => owner.OnThreatRemoveTarget(hostileEntity),
                "process threat removal", unitId);

            ExecuteRemovalAction(() => reciprocalThreatManager?.RemoveHostile(owner.Guid),
                "remove reciprocal threat relationship", unitId);

            log.Trace($"Removed hostile {hostileEntity.HatedUnitId} from {owner.Guid}'s threat list.");
        }

        private void ExecuteRemovalAction(Action action, string description, uint unitId)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to {description} between {owner.Guid} and {unitId}.");
            }
        }

        /// <summary>
        /// Broadcast threat list.
        /// </summary>
        /// <remarks>
        /// Threat list will be sent to all <see cref="IPlayer"/>s targeting owner <see cref="IUnitEntity"/>.
        /// </remarks>
        public void BroadcastThreatList()
        {
            ServerEntityThreatListUpdate message = BuildServerThreatListUpdate();
            foreach (uint guid in owner.TargetingGuids)
            {
                IUnitEntity unit = owner.GetVisible<IUnitEntity>(guid);
                if (unit is not IPlayer player)
                    continue;

                player.Session.EnqueueMessageEncrypted(message);
            }
        }

        /// <summary>
        /// Send threat list to supplied <see cref="IGameSession"/>.
        /// </summary>
        public void SendThreatList(IGameSession session)
        {
            session.EnqueueMessageEncrypted(BuildServerThreatListUpdate());
        }

        private ServerEntityThreatListUpdate BuildServerThreatListUpdate()
        {
            var threatUpdate = new ServerEntityThreatListUpdate
            {
                SrcUnitId = owner.Guid,
            };

            // TODO: should this be target plus top 4?
            IHostileEntity[] hostileEntities = hostiles.Values
                .OrderByDescending(h => h.Threat)
                .ThenBy(h => h.HatedUnitId)
                .Take(5)
                .ToArray();

            for (uint i = 0u; i < hostileEntities.Length; i++)
            {
                threatUpdate.ThreatUnitIds[i] = hostileEntities[i].HatedUnitId;
                threatUpdate.ThreatLevels[i] = hostileEntities[i].Threat;
            }

            return threatUpdate;
        }

        public IEnumerator<IHostileEntity> GetEnumerator()
        {
            return hostiles.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
