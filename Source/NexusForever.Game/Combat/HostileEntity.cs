using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Game.Combat
{
    public class HostileEntity : IHostileEntity
    {
        private const double PvPInactivityTimeout = 10d;

        public uint HatedUnitId { get; }
        public uint Threat { get; private set; }

        /// <inheritdoc />
        public bool IsPvP { get; }

        /// <inheritdoc />
        public bool IsExpired => IsPvP && inactivityElapsed >= PvPInactivityTimeout;

        private double inactivityElapsed;

        /// <summary>
        /// Create a new <see cref="IHostileEntity"/> between the supplied owner and target.
        /// </summary>
        public HostileEntity(IUnitEntity owner, IUnitEntity target)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(target);

            HatedUnitId = target.Guid;
            IsPvP = owner is IPlayer && target is IPlayer;
        }

        /// <inheritdoc />
        public void Update(double lastTick)
        {
            if (!IsPvP || !double.IsFinite(lastTick) || lastTick <= 0d)
                return;

            inactivityElapsed = Math.Min(inactivityElapsed + lastTick, PvPInactivityTimeout);
        }

        /// <summary>
        /// Modify this <see cref="IHostileEntity"/> threat by the given amount.
        /// </summary>
        /// <remarks>
        /// Value is a delta, if a negative value is supplied it will be deducted from the existing threat if any.
        /// </remarks>
        public void UpdateThreat(int threat)
        {
            Threat = (uint)Math.Clamp(Threat + threat, 0u, uint.MaxValue);
            Refresh();
        }

        /// <inheritdoc />
        public void Refresh()
        {
            inactivityElapsed = 0d;
        }
    }
}
