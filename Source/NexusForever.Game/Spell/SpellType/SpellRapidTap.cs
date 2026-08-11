using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.Game.Spell.SpellType
{
    /// <summary>
    /// Rapid button-press threshold spell. The root executes once; later presses dispatch exact children.
    /// </summary>
    [SpellType(CastMethod.RapidTap)]
    public class SpellRapidTap : SpellThreshold
    {
        private int nextThresholdIndex;

        public SpellRapidTap(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters, CastMethod.RapidTap)
        {
        }

        protected override void HandleThresholdInput(bool buttonPressed)
        {
            // Releases and presses received before the root is ready are deliberately consumed without
            // creating another root. An in-progress dispatch is likewise not recursively advanced.
            if (!buttonPressed
                || InputClosed
                || !RootReady
                || status != SpellStatus.Waiting
                || DispatchInProgress)
                return;

            if (nextThresholdIndex >= ThresholdRows.Count)
            {
                CloseThresholdWindow();
                return;
            }

            int selectedIndex = nextThresholdIndex++;
            if (!TryActivateThreshold(
                    selectedIndex,
                    consumeCumulativeThresholdCosts: false,
                    out CastResult failure))
            {
                FailThreshold(failure);
                return;
            }

            if (nextThresholdIndex == ThresholdRows.Count)
                CloseThresholdWindow();
        }

        protected override void AdvanceThreshold(double elapsedSeconds)
        {
            if (ThresholdElapsedMilliseconds >= ThresholdWindowMilliseconds)
                CloseThresholdWindow();
        }
    }
}
