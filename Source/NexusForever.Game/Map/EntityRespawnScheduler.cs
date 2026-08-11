using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NLog;

namespace NexusForever.Game.Map
{
    /// <summary>
    /// Tracks map-owned respawn reservations for persistent non-player entities.
    /// </summary>
    internal sealed class EntityRespawnScheduler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Default delay between death and a database entity respawn.
        /// </summary>
        internal const double RespawnDelay = 30d;

        private const double FailedRespawnRetryDelay = 1d;

        private readonly Dictionary<uint, PendingRespawn> pendingRespawns = [];

        /// <summary>
        /// Create a respawn reservation for a database-backed non-player entity.
        /// </summary>
        public bool TrySchedule(IWorldEntity entity, EntityModel model)
        {
            if (entity == null
                || model == null
                || entity is IPlayer
                || entity.EntityId == 0u
                || model.Id == 0u
                || entity.EntityId != model.Id
                || entity.Type != model.Type)
                return false;

            return pendingRespawns.TryAdd(model.Id, new PendingRespawn(model));
        }

        /// <summary>
        /// Return whether the persistent entity has a pending or enqueued respawn reservation.
        /// </summary>
        public bool IsPending(uint entityId)
        {
            return entityId != 0u && pendingRespawns.ContainsKey(entityId);
        }

        /// <summary>
        /// Advance pending timers and enqueue due entities only when their spawn grid is active.
        /// </summary>
        public void Update(
            double elapsed,
            Func<EntityModel, bool> canSpawn,
            Func<EntityModel, IWorldEntity> enqueueSpawn)
        {
            ArgumentNullException.ThrowIfNull(canSpawn);
            ArgumentNullException.ThrowIfNull(enqueueSpawn);

            if (!double.IsFinite(elapsed) || elapsed <= 0d)
                return;

            foreach (PendingRespawn pendingRespawn in pendingRespawns.Values.ToArray())
            {
                if (pendingRespawn.EnqueuedEntity != null)
                    continue;

                pendingRespawn.Remaining = Math.Max(0d, pendingRespawn.Remaining - elapsed);
                if (pendingRespawn.Remaining > 0d)
                    continue;

                try
                {
                    if (!canSpawn(pendingRespawn.Model))
                        continue;

                    pendingRespawn.EnqueuedEntity = enqueueSpawn(pendingRespawn.Model);
                    if (pendingRespawn.EnqueuedEntity == null)
                        pendingRespawn.Remaining = FailedRespawnRetryDelay;
                }
                catch (Exception exception)
                {
                    pendingRespawn.Remaining = FailedRespawnRetryDelay;
                    log.Error(exception, $"Failed to create respawn entity {pendingRespawn.Model.Id}; retrying.");
                }
            }
        }

        /// <summary>
        /// Complete the reservation when the exact scheduler-created entity reaches the map.
        /// </summary>
        public bool Acknowledge(IWorldEntity entity)
        {
            if (entity == null
                || !pendingRespawns.TryGetValue(entity.EntityId, out PendingRespawn pendingRespawn)
                || !ReferenceEquals(pendingRespawn.EnqueuedEntity, entity))
                return false;

            return pendingRespawns.Remove(entity.EntityId);
        }

        /// <summary>
        /// Release an unsuccessful enqueue so the exact scheduler-created entity can be retried.
        /// </summary>
        public bool Fail(IWorldEntity entity)
        {
            if (entity == null
                || !pendingRespawns.TryGetValue(entity.EntityId, out PendingRespawn pendingRespawn)
                || !ReferenceEquals(pendingRespawn.EnqueuedEntity, entity))
                return false;

            pendingRespawn.EnqueuedEntity = null;
            pendingRespawn.Remaining = FailedRespawnRetryDelay;
            return true;
        }

        private sealed class PendingRespawn
        {
            public EntityModel Model { get; }
            public double Remaining { get; set; } = RespawnDelay;
            public IWorldEntity EnqueuedEntity { get; set; }

            public PendingRespawn(EntityModel model)
            {
                Model = model;
            }
        }
    }
}
