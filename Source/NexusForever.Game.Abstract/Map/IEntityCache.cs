using NexusForever.Database.World.Model;

namespace NexusForever.Game.Abstract.Map
{
    public interface IEntityCache
    {
        uint GridCount { get; }
        uint EntityCount { get; }

        /// <summary>
        /// Add <see cref="EntityModel"/> to be spawned when parent <see cref="IMapGrid"/> is activated.
        /// </summary>
        void AddEntity(EntityModel model);

        /// <summary>
        /// Return the cached persistent entity model with the supplied identifier.
        /// </summary>
        EntityModel GetEntity(uint entityId);

        /// <summary>
        /// Return all <see cref="EntityModel"/>'s to be spawned for parent <see cref="IMapGrid"/>.
        /// </summary>
        IEnumerable<EntityModel> GetEntities(uint gridX, uint gridZ);
    }
}
