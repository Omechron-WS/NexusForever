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
    /// <summary>
    /// Deferred spell trigger registered against a unit combat event.
    /// </summary>
    public class ProcInfo : IProcInfo
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public IUnitEntity Owner { get; }
        public uint EffectId { get; }
        public uint ApplicatorSpell4Id { get; }
        public ProcType Type { get; }
        public uint TriggerSpell4Id { get; }
        public float Chance { get; }
        public bool CanTrigger => HasTriggerChance
            && !cancelled
            && !triggerPending
            && !cooldownTimer.IsTicking;

        private bool HasTriggerChance => float.IsFinite(Chance) && Chance > 0f && Chance <= 1f;

        private readonly UpdateTimer cooldownTimer;
        private readonly Func<double> chanceRoll;
        private readonly List<ISpell> triggeredSpells = [];
        private uint primaryTargetId;
        private bool triggerPending;
        private bool cancelled;

        /// <summary>
        /// Create a proc from a spell effect definition.
        /// </summary>
        public ProcInfo(IUnitEntity owner, Spell4EffectsEntry entry)
            : this(owner, entry, static () => Random.Shared.NextDouble())
        {
        }

        internal ProcInfo(IUnitEntity owner, Spell4EffectsEntry entry, Func<double> chanceRoll)
        {
            Owner              = owner ?? throw new ArgumentNullException(nameof(owner));
            ArgumentNullException.ThrowIfNull(entry);
            this.chanceRoll    = chanceRoll ?? throw new ArgumentNullException(nameof(chanceRoll));

            EffectId           = entry.Id;
            ApplicatorSpell4Id = entry.SpellId;
            Type               = (ProcType)entry.DataBits00;
            TriggerSpell4Id    = entry.DataBits01;
            Chance             = BitConverter.UInt32BitsToSingle(entry.DataBits02);

            // Build 16042 uses both signed and unsigned maximum values as no-cooldown sentinels.
            double cooldown = entry.DataBits04 is uint.MaxValue or int.MaxValue
                ? 0d
                : entry.DataBits04 / 1000d;
            cooldownTimer = new UpdateTimer(cooldown, false);
        }

        /// <summary>
        /// Advance the internal cooldown and cast any trigger deferred from combat dispatch.
        /// </summary>
        public void Update(double lastTick)
        {
            triggeredSpells.RemoveAll(spell => spell.IsFinished);

            if (cancelled)
                return;

            if (cooldownTimer.IsTicking && double.IsFinite(lastTick) && lastTick >= 0d)
                cooldownTimer.Update(lastTick);

            if (!triggerPending)
                return;

            triggerPending = false;
            log.Trace("Proc {0} firing trigger spell {1}.", Type, TriggerSpell4Id);

            uint targetId = primaryTargetId;
            primaryTargetId = 0u;

            ISpell spell = Owner.CastSpellTracked(TriggerSpell4Id, new SpellParameters
            {
                PrimaryTargetId        = targetId,
                UserInitiatedSpellCast = false,
                IsProcTriggered        = true
            });

            if (spell != null)
                triggeredSpells.Add(spell);
        }

        /// <summary>
        /// Schedule the proc's trigger spell after evaluating its configured chance and cooldown.
        /// </summary>
        public bool Trigger(IUnitEntity primaryTarget = null)
        {
            if (!CanTrigger)
                return false;

            if (Chance < 1f)
            {
                double roll = chanceRoll();
                if (!double.IsFinite(roll) || roll < 0d || roll >= Chance)
                    return false;
            }

            primaryTargetId = primaryTarget?.Guid ?? 0u;
            triggerPending = true;
            cooldownTimer.Reset(true);
            return true;
        }

        /// <summary>
        /// Cancel any pending trigger and finish spells previously triggered by this proc.
        /// </summary>
        public void Cancel()
        {
            if (cancelled)
                return;

            cancelled       = true;
            triggerPending  = false;
            primaryTargetId = 0u;
            cooldownTimer.Reset(false);

            ISpell[] spellSnapshot = triggeredSpells.ToArray();
            triggeredSpells.Clear();

            foreach (ISpell spell in spellSnapshot)
            {
                try
                {
                    if (!spell.IsFinished && !spell.IsFinishing)
                        spell.Finish();
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to finish a spell triggered by proc effect {EffectId}.");
                }
            }
        }
    }
}
