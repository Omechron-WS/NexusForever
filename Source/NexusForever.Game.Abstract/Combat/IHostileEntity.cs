using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Combat
{
    public interface IHostileEntity : IUpdate
    {
        uint HatedUnitId { get; }
        uint Threat { get; }

        /// <summary>
        /// Returns whether this relationship is between two players.
        /// </summary>
        bool IsPvP { get; }

        /// <summary>
        /// Returns whether this relationship has exceeded its inactivity timeout.
        /// </summary>
        bool IsExpired { get; }

        /// <summary>
        /// Modify this <see cref="IHostileEntity"/> threat by the given amount.
        /// </summary>
        /// <remarks>
        /// Value is a delta, if a negative value is supplied it will be deducted from the existing threat if any.
        /// </remarks>
        void UpdateThreat(int threatDelta);

        /// <summary>
        /// Reset the inactivity timeout for this relationship.
        /// </summary>
        void Refresh();
    }
}
