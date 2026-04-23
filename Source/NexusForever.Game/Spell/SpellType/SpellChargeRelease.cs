using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Message.Static;
using NLog;

namespace NexusForever.Game.Spell.SpellType
{
    /// <summary>
    /// Hold-to-charge, release-to-cast spell mechanic.
    /// Charge builds over the hold duration, then releases at the current threshold.
    /// </summary>
    [SpellType(CastMethod.ChargeRelease)]
    public class SpellChargeRelease : Spell
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private double holdDuration;
        private double totalThresholdTimer;

        public SpellChargeRelease(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }

        public override void Cast()
        {
            if (status == SpellStatus.Waiting)
            {
                // Release — execute at current threshold
                Execute();
                status = SpellStatus.Finishing;
                return;
            }

            if (status != SpellStatus.Initiating)
                throw new InvalidOperationException();

            CastResult result = CheckCast();
            if (result != CastResult.Ok)
            {
                SendSpellCastResult(result);
                return;
            }

            if (Caster is IPlayer player)
                if (Parameters.SpellInfo.GlobalCooldown != null)
                    player.SpellManager.SetGlobalSpellCooldown(Parameters.SpellInfo.GlobalCooldown.CooldownTime / 1000d);

            if (Caster is not IPlayer)
                InitialiseTelegraphs();

            SendSpellStart();

            totalThresholdTimer = Parameters.SpellInfo.Entry.ThresholdTime / 1000d;

            events.EnqueueEvent(new SpellEvent(Parameters.SpellInfo.Entry.CastTime / 1000d, () =>
            {
                Execute();
                status = SpellStatus.Waiting;
            }));

            status = SpellStatus.Casting;
            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started charge-release casting.");
        }

        public override void Update(double lastTick)
        {
            base.Update(lastTick);

            if (status == SpellStatus.Waiting)
            {
                holdDuration += lastTick;
                if (totalThresholdTimer > 0 && holdDuration >= totalThresholdTimer)
                {
                    // Auto-release at max charge
                    Execute();
                    status = SpellStatus.Finishing;
                }
            }
        }

        protected override bool IsCastingInternal()
        {
            return status == SpellStatus.Casting || status == SpellStatus.Executing || status == SpellStatus.Waiting;
        }

        protected override bool CanFinish()
        {
            if (status == SpellStatus.Waiting)
                return false;

            return base.CanFinish();
        }
    }
}
