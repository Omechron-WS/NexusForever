using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Combat;
using NexusForever.GameTable.Model;
using NexusForever.Shared.Game;
using NLog;

namespace NexusForever.Game.Combat
{
    /// <summary>
    /// Delayed spell trigger registered against a unit combat event.
    /// </summary>
    public class ProcInfo : IProcInfo
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public IUnitEntity Owner { get; }
        public uint ApplicatorSpell4Id { get; }
        public ProcType Type { get; }
        public uint TriggerSpell4Id { get; }
        public bool CanTrigger => !triggerPending;

        private readonly UpdateTimer triggerTimer;
        private bool triggerPending;

        /// <summary>
        /// Create a proc from a spell effect definition.
        /// </summary>
        public ProcInfo(IUnitEntity owner, Spell4EffectsEntry entry)
        {
            Owner              = owner ?? throw new ArgumentNullException(nameof(owner));
            ArgumentNullException.ThrowIfNull(entry);

            ApplicatorSpell4Id = entry.SpellId;
            Type               = (ProcType)entry.DataBits00;
            TriggerSpell4Id    = entry.DataBits01;

            double triggerDelay = entry.DataBits04 / 1000d;
            triggerTimer        = new UpdateTimer(triggerDelay, false);
        }

        /// <summary>
        /// Advance a pending trigger and cast its spell when the delay elapses.
        /// </summary>
        public void Update(double lastTick)
        {
            if (!triggerPending)
                return;

            triggerTimer.Update(lastTick);
            if (!triggerTimer.HasElapsed)
                return;

            triggerPending = false;
            log.Trace("Proc {0} firing trigger spell {1}.", Type, TriggerSpell4Id);

            Owner.CastSpell(TriggerSpell4Id, new SpellParameters
            {
                UserInitiatedSpellCast = false
            });
        }

        /// <summary>
        /// Schedule the proc's trigger spell after its configured delay.
        /// </summary>
        public bool Trigger()
        {
            if (!CanTrigger)
                return false;

            triggerPending = true;
            triggerTimer.Reset(true);
            return true;
        }
    }
}
