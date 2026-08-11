using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement.Command.Position;

namespace NexusForever.Script.Template
{
    public interface IWorldEntityScript : IGridEntityScript
    {
        /// <summary>
        /// Invoked when <see cref="IPositionCommand"/> is finalised.
        /// </summary>
        void OnPositionEntityCommandFinalise(IPositionCommand command)
        {
        }

        /// <summary>
        /// Invoked when <see cref="IWorldEntity"/> enters a zone.
        /// </summary>
        void OnEnterZone(IWorldEntity entity, uint zone)
        {
        }

        /// <summary>
        /// Invoked after a client-side interaction succeeds.
        /// </summary>
        void OnActivateSuccess(IWorldEntity entity, IPlayer activator)
        {
        }

        /// <summary>
        /// Invoked after a client-side interaction fails.
        /// </summary>
        void OnActivateFail(IWorldEntity entity, IPlayer activator)
        {
        }
    }
}
