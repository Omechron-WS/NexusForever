using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Message.Static;
using NLog;

namespace NexusForever.Game.Spell.SpellType
{
    /// <summary>
    /// Spell type for client-side interaction (CSI) mini-games.
    /// The client drives success/failure; the server waits for the callback.
    /// </summary>
    [SpellType(CastMethod.ClientSideInteraction)]
    public class SpellClientSideInteraction : Spell
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public SpellClientSideInteraction(IUnitEntity caster, ISpellParameters parameters)
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
                SendSpellCastResult(result);
                return;
            }

            if (Caster is IPlayer player)
                if (Parameters.SpellInfo.GlobalCooldown != null)
                    player.SpellManager.SetGlobalSpellCooldown(Parameters.SpellInfo.GlobalCooldown.CooldownTime / 1000d);

            SendSpellStart();

            // If this spell isn't natively a CSI (proxy-cast as CSI), use normal cast timing
            if ((CastMethod)Parameters.SpellInfo.BaseInfo.Entry.CastMethod != CastMethod.ClientSideInteraction)
            {
                double castTime = Parameters.SpellInfo.Entry.CastTime / 1000d;
                events.EnqueueEvent(new SpellEvent(castTime, () => SucceedClientInteraction()));
            }

            // Otherwise, wait for client to report success/failure
            status = SpellStatus.Casting;
            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started CSI casting.");
        }

        /// <summary>
        /// Called when the client reports CSI success.
        /// </summary>
        public void SucceedClientInteraction()
        {
            Execute();

            // TODO: integrate with CSI system (Phase 3) to call TriggerSuccess on the activating entity
        }

        /// <summary>
        /// Called when the client reports CSI failure.
        /// </summary>
        public void FailClientInteraction()
        {
            // TODO: integrate with CSI system (Phase 3) to call TriggerFail on the activating entity
            CancelCast(CastResult.ClientSideInteractionFail);
        }
    }
}
