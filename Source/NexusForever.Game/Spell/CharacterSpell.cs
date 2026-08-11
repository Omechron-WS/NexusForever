using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Persistence;
using NexusForever.Network.World.Message.Model;
using NexusForever.Shared.Game;

namespace NexusForever.Game.Spell
{
    public class CharacterSpell : ICharacterSpell
    {
        [Flags]
        public enum UnlockedSpellSaveMask
        {
            None   = 0x0000,
            Create = 0x0001,
            Tier   = 0x0002
        }

        public IPlayer Owner { get; }
        public ISpellBaseInfo BaseInfo { get; }
        public ISpellInfo SpellInfo { get; private set; }
        public IItem Item { get; }

        public byte Tier
        {
            get => tier;
            set
            {
                if (tier == value)
                    return;

                SpellInfo = BaseInfo.GetSpellInfo(value) ?? throw new ArgumentOutOfRangeException(nameof(value));
                tier      = value;
                saveMask.Mark(UnlockedSpellSaveMask.Tier);
            }
        }
        private byte tier;

        public uint AbilityCharges { get; private set; }
        public uint MaxAbilityCharges => SpellInfo.Entry.AbilityChargeCount;

        private VersionedSaveMask<UnlockedSpellSaveMask> saveMask = new();

        private UpdateTimer rechargeTimer;

        /// <summary>
        /// Create a new <see cref="ICharacterSpell"/> from an existing database model.
        /// </summary>
        public CharacterSpell(IPlayer player, CharacterSpellModel model, ISpellBaseInfo baseInfo, IItem item)
        {
            Owner     = player;
            BaseInfo  = baseInfo ?? throw new ArgumentNullException(nameof(baseInfo));
            tier      = model.Tier;
            SpellInfo = baseInfo.GetSpellInfo(tier) ?? throw new ArgumentOutOfRangeException(nameof(model.Tier));
            Item      = item;

            InitialiseAbilityCharges();
        }

        /// <summary>
        /// Create a new <see cref="ICharacterSpell"/> from a <see cref="ISpellBaseInfo"/>.
        /// </summary>
        public CharacterSpell(IPlayer player, ISpellBaseInfo baseInfo, byte tier, IItem item)
        {
            Owner     = player;
            BaseInfo  = baseInfo ?? throw new ArgumentNullException(nameof(baseInfo));
            SpellInfo = baseInfo.GetSpellInfo(tier) ?? throw new ArgumentOutOfRangeException(nameof(tier));
            Item      = item;
            this.tier = tier;

            InitialiseAbilityCharges();

            saveMask = new VersionedSaveMask<UnlockedSpellSaveMask>(UnlockedSpellSaveMask.Create);
        }

        private void InitialiseAbilityCharges()
        {
            if (MaxAbilityCharges == 0u)
                return;

            rechargeTimer  = new UpdateTimer(SpellInfo.Entry.AbilityRechargeTime / 1000d);
            AbilityCharges = MaxAbilityCharges;
            SendChargeUpdate();
        }

        public void Update(double lastTick)
        {
            if (MaxAbilityCharges > 0 && AbilityCharges < MaxAbilityCharges)
            {
                rechargeTimer.Update(lastTick);
                if (rechargeTimer.HasElapsed)
                {
                    AbilityCharges = Math.Clamp(AbilityCharges + SpellInfo.Entry.AbilityRechargeCount, 0u, MaxAbilityCharges);
                    SendChargeUpdate();
                    rechargeTimer.Reset();
                }
            }
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage character spell changes and register their successful-commit acknowledgements.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<UnlockedSpellSaveMask> snapshot = saveMask.Capture();
            UnlockedSpellSaveMask mask = snapshot.Mask;
            if (mask == UnlockedSpellSaveMask.None)
                return;

            if ((mask & UnlockedSpellSaveMask.Create) != 0)
            {
                var model = new CharacterSpellModel
                {
                    Id           = Owner.CharacterId,
                    Spell4BaseId = BaseInfo.Entry.Id,
                    Tier         = tier
                };

                context.Add(model);
            }
            else
            {
                var model = new CharacterSpellModel
                {
                    Id           = Owner.CharacterId,
                    Spell4BaseId = BaseInfo.Entry.Id,
                };

                EntityEntry<CharacterSpellModel> entity = context.Attach(model);
                if ((mask & UnlockedSpellSaveMask.Tier) != 0)
                {
                    model.Tier = tier;
                    entity.Property(p => p.Tier).IsModified = true;
                }
            }

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }

        /// <summary>
        /// Used for when the client does not have continuous casting enabled
        /// </summary>
        public void Cast()
        {
            Cast(buttonPressed: true);
        }

        /// <summary>
        /// Used for continuous casting when the client has it enabled, or spells with Cast Methods like ChargeRelease
        /// </summary>
        public void Cast(bool buttonPressed)
        {
            // TODO: Handle continuous casting of spell for Player if button remains depressed

            ISpell activeThresholdRoot = Owner.GetActiveSpell(IsOwnedThresholdRoot);
            if (activeThresholdRoot is IThresholdSpell thresholdSpell)
            {
                thresholdSpell.TryHandleThresholdInput(buttonPressed);
                return;
            }

            // If the player depresses button after the spell had exceeded its threshold, don't try and recast the spell until button is pressed down again.
            if (!buttonPressed)
                return;

            CastSpell();
        }

        private bool IsOwnedThresholdRoot(ISpell spell)
        {
            return spell is IThresholdSpell
                && !spell.IsFinished
                && ReferenceEquals(spell.Parameters.CharacterSpell, this)
                && !spell.Parameters.IsThresholdChild
                && spell.Parameters.ThresholdParent == null
                && spell.Parameters.ParentSpellInfo == null
                && ReferenceEquals(spell.Parameters.RootSpellInfo, spell.Parameters.SpellInfo);
        }

        private void CastSpell()
        {
            Owner.CastSpell(new SpellParameters
            {
                CharacterSpell         = this,
                SpellInfo              = SpellInfo,
                UserInitiatedSpellCast = true
            });
        }

        public void UseCharge()
        {
            if (AbilityCharges == 0)
                throw new SpellException("No charges available.");

            AbilityCharges -= 1;
            SendChargeUpdate();
        }

        private void SendChargeUpdate()
        {
            Owner.Session.EnqueueMessageEncrypted(new ServerSpellAbilityCharges
            {
                SpellId            = Item.Id,
                AbilityChargeCount = AbilityCharges
            });
        }
    }
}
