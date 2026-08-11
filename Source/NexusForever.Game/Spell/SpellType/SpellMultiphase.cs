using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;
using NLog;

namespace NexusForever.Game.Spell.SpellType
{
    [SpellType(CastMethod.Multiphase)]
    public class SpellMultiphase : Spell
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public SpellMultiphase(IUnitEntity caster, ISpellParameters parameters)
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

            List<SpellPhaseEntry> phases = GameTableManager.Instance.SpellPhase.Entries
                .Where(p => p.Spell4IdOwner == Parameters.SpellInfo.Entry.Id)
                .OrderBy(p => p.OrderIndex)
                .ToList();

            uint spellDelay = 0;
            for (int i = 0; i < phases.Count; i++)
            {
                SpellPhaseEntry phase = phases[i];
                spellDelay += phase.PhaseDelay;
                int phaseIndex = i;

                events.EnqueueEvent(new SpellEvent(spellDelay / 1000d, () =>
                {
                    currentPhase = (byte)phase.OrderIndex;
                    targets.Clear();
                    Execute(phaseIndex == 0);

                    if (phaseIndex == phases.Count - 1)
                        status = SpellStatus.Finishing;
                }));
            }

            // If no phases found, execute once and finish
            if (phases.Count == 0)
            {
                events.EnqueueEvent(new SpellEvent(Parameters.SpellInfo.Entry.CastTime / 1000d, () =>
                {
                    Execute();
                    status = SpellStatus.Finishing;
                }));
            }

            status = SpellStatus.Casting;
            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started multiphase casting with {phases.Count} phases.");
        }

        protected override bool IsCastingInternal()
        {
            return status == SpellStatus.Casting || status == SpellStatus.Executing;
        }
    }
}
