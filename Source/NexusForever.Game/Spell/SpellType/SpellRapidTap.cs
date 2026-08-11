using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Message.Static;
using NLog;

namespace NexusForever.Game.Spell.SpellType
{
    /// <summary>
    /// Rapid button-press spell mechanic.
    /// Each press increments the threshold; effects scale based on tap count.
    /// </summary>
    [SpellType(CastMethod.RapidTap)]
    public class SpellRapidTap : Spell
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private uint thresholdValue;

        public SpellRapidTap(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }

        public override void Cast()
        {
            if (status == SpellStatus.Waiting)
            {
                // Subsequent tap — increment threshold
                thresholdValue++;
                targets.Clear();
                Execute();
                return;
            }

            if (status != SpellStatus.Initiating)
                throw new InvalidOperationException();

            CastResult result = CheckCast();
            if (result != CastResult.Ok)
            {
                FailCast(result);
                return;
            }

            if (Caster is IPlayer player)
                if (Parameters.SpellInfo.GlobalCooldown != null)
                    player.SpellManager.SetGlobalSpellCooldown(Parameters.SpellInfo.GlobalCooldown.CooldownTime / 1000d);

            if (Caster is not IPlayer)
                InitialiseTelegraphs();

            SendSpellStart();

            // Schedule finish at cast time + threshold time
            double finishDelay = (Parameters.SpellInfo.Entry.CastTime + Parameters.SpellInfo.Entry.ThresholdTime) / 1000d;
            events.EnqueueEvent(new SpellEvent(finishDelay, Finish));

            events.EnqueueEvent(new SpellEvent(Parameters.SpellInfo.Entry.CastTime / 1000d, () =>
            {
                Execute();
                status = SpellStatus.Waiting;
            }));

            status = SpellStatus.Casting;
            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started rapid-tap casting.");
        }

        protected override bool IsCastingInternal()
        {
            return status == SpellStatus.Casting || status == SpellStatus.Waiting;
        }

        protected override bool CanFinish()
        {
            if (status == SpellStatus.Waiting)
                return false;

            return base.CanFinish();
        }
    }
}
