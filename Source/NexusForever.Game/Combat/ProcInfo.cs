using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Combat;
using NexusForever.GameTable.Model;
using NexusForever.Shared.Game;
using NLog;

namespace NexusForever.Game.Combat
{
    public class ProcInfo : IProcInfo
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public uint ApplicatorSpell4Id { get; }
        public ProcType Type { get; }
        public uint TriggerSpell4Id { get; }

        private readonly IUnitEntity owner;
        private readonly UpdateTimer cooldownTimer;
        private bool pendingTrigger;

        public ProcInfo(IUnitEntity owner, Spell4EffectsEntry entry)
        {
            this.owner         = owner;
            ApplicatorSpell4Id = entry.SpellId;
            Type               = (ProcType)entry.DataBits00;
            TriggerSpell4Id    = entry.DataBits01;

            double cooldown = entry.DataBits04 > 0 ? entry.DataBits04 / 1000d : 0d;
            cooldownTimer = new UpdateTimer(cooldown, false);
        }

        public void Update(double lastTick)
        {
            if (!pendingTrigger)
                return;

            cooldownTimer.Update(lastTick);
            if (cooldownTimer.HasElapsed)
            {
                log.Trace($"Proc {Type} firing trigger spell {TriggerSpell4Id}.");

                owner.CastSpell(TriggerSpell4Id, new SpellParameters
                {
                    UserInitiatedSpellCast = false
                });

                pendingTrigger = false;
            }
        }

        /// <summary>
        /// Attempt to trigger the proc. Returns false if on cooldown.
        /// </summary>
        public bool Trigger()
        {
            if (pendingTrigger)
                return false;

            pendingTrigger = true;
            cooldownTimer.Reset(true);
            return true;
        }
    }
}
