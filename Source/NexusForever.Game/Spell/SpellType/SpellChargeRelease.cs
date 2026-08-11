using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.Game.Spell.SpellType
{
    /// <summary>
    /// Hold-to-charge threshold spell. Release or timeout dispatches exactly one selected child.
    /// </summary>
    [SpellType(CastMethod.ChargeRelease)]
    public class SpellChargeRelease : SpellThreshold
    {
        private int selectedThresholdIndex;
        private bool releaseRequested;
        private bool advancingThreshold;

        public SpellChargeRelease(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters, CastMethod.ChargeRelease)
        {
        }

        protected override void HandleThresholdInput(bool buttonPressed)
        {
            // Additional presses never create another root. A release which arrives during cast time or
            // a packet callback is latched and resolved after the root is ready/current advancement ends.
            if (buttonPressed || InputClosed)
                return;

            if (!RootReady || status is SpellStatus.Casting or SpellStatus.Executing || advancingThreshold)
            {
                releaseRequested = true;
                return;
            }

            if (status == SpellStatus.Waiting)
                ReleaseThreshold();
        }

        protected override void OnThresholdReady()
        {
            if (releaseRequested && !InputClosed)
                ReleaseThreshold();
        }

        protected override void AdvanceThreshold(double elapsedSeconds)
        {
            advancingThreshold = true;
            try
            {
                while (selectedThresholdIndex + 1 < ThresholdRows.Count
                    && ThresholdElapsedMilliseconds >= GetCumulativeThresholdDuration(selectedThresholdIndex + 1))
                {
                    selectedThresholdIndex++;
                    // Promotion packets are monotonic and best-effort. Re-entrant release is latched by
                    // advancingThreshold and is handled after all due boundaries have been committed.
                    TryPublishSelectedThreshold();
                }
            }
            finally
            {
                advancingThreshold = false;
            }

            if (InputClosed)
                return;

            if (releaseRequested
                || ThresholdElapsedMilliseconds >= ThresholdWindowMilliseconds)
                ReleaseThreshold();
        }

        private void ReleaseThreshold()
        {
            if (InputClosed || DispatchInProgress || status != SpellStatus.Waiting)
                return;

            // Commit the terminal selection before any cost, child-start, or packet callback can re-enter.
            BeginThresholdClose();

            if (!TryActivateThreshold(
                    selectedThresholdIndex,
                    consumeCumulativeThresholdCosts: true,
                    out CastResult failure))
            {
                FailThreshold(failure);
                return;
            }

            TryPublishSelectedThreshold();
            PublishThresholdClear();
        }

        private void TryPublishSelectedThreshold()
        {
            // TryActivateThreshold publishes the same one-based value on release. Promotions reach this
            // helper first, so the shared monotonic packet guard suppresses any duplicate final value.
            PublishThresholdValue(checked((byte)(selectedThresholdIndex + 1)));
        }
    }
}
