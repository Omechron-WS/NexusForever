using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Combat;
using NexusForever.GameTable.Model;
using NexusForever.Shared.Game;
using NLog;

namespace NexusForever.Game.Combat
{
    // TODO: Wire ProcInfo into the game loop:
    // - Add HandleEffectProc to SpellEffectHandler that calls IUnitEntity.ApplyProc()
    // - Add proc collection + FireProc(ProcType) + Update loop to IUnitEntity/UnitEntity
    // - Fire ProcType.CriticalDamage from HandleEffectDamage on crit result

    public class ProcInfo : IProcInfo
    {
        // NLog used here because ProcInfo is constructed per-entity, not via DI container.
        // TODO: Consider ILogger<ProcInfo> if proc management moves to a DI-managed service.
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public uint ApplicatorSpell4Id { get; }
        public ProcType Type { get; }
        public uint TriggerSpell4Id { get; }

        private readonly IUnitEntity owner;
        private readonly UpdateTimer cooldownTimer;
        private bool onCooldown;

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
            if (!onCooldown)
                return;

            cooldownTimer.Update(lastTick);
            if (cooldownTimer.HasElapsed)
                onCooldown = false;
        }

        /// <summary>
        /// Attempt to trigger the proc. Fires the trigger spell immediately
        /// and starts the internal cooldown. Returns false if on cooldown.
        /// </summary>
        public bool Trigger()
        {
            if (onCooldown)
                return false;

            log.Trace("Proc {0} firing trigger spell {1}.", Type, TriggerSpell4Id);

            owner.CastSpell(TriggerSpell4Id, new SpellParameters
            {
                UserInitiatedSpellCast = false
            });

            onCooldown = true;
            cooldownTimer.Reset(true);
            return true;
        }
    }
}
