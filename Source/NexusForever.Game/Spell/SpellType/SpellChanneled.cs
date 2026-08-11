using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Message.Static;
using NLog;

namespace NexusForever.Game.Spell.SpellType
{
    [SpellType(CastMethod.Channeled)]
    public class SpellChanneled : Spell
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public SpellChanneled(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }

        public override void Cast()
        {
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
                    player.SpellManager.SetGlobalSpellCooldown(
                        Parameters.SpellInfo.Entry.GlobalCooldownEnum,
                        Parameters.SpellInfo.GlobalCooldown.CooldownTime / 1000d);

            if (Caster is not IPlayer)
                InitialiseTelegraphs();

            SendSpellStart();

            uint channelInitialDelay = Parameters.SpellInfo.Entry.ChannelInitialDelay;
            uint channelMaxTime = Parameters.SpellInfo.Entry.ChannelMaxTime;
            uint channelPulseTime = Parameters.SpellInfo.Entry.ChannelPulseTime;

            // Initial pulse after the initial delay
            events.EnqueueEvent(new SpellEvent(channelInitialDelay / 1000d, () =>
            {
                Execute();
            }));

            // Subsequent pulses at each pulse interval
            if (channelPulseTime > 0 && channelMaxTime > 0)
            {
                uint numberOfPulses = channelMaxTime / channelPulseTime;
                for (uint i = 1; i <= numberOfPulses; i++)
                {
                    double delay = (channelInitialDelay + channelPulseTime * i) / 1000d;
                    events.EnqueueEvent(new SpellEvent(delay, () =>
                    {
                        if (status == SpellStatus.Finishing || status == SpellStatus.Finished)
                            return;

                        targets.Clear();
                        Execute();
                    }));
                }
            }

            // End channel at max time
            if (channelMaxTime > 0)
                events.EnqueueEvent(new SpellEvent(channelMaxTime / 1000d, Finish));

            status = SpellStatus.Casting;
            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started channeling.");
        }

        protected override bool IsCastingInternal()
        {
            return status == SpellStatus.Casting || status == SpellStatus.Executing;
        }
    }
}
