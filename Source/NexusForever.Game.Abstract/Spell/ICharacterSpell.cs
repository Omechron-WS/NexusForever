using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Spell
{
    public interface ICharacterSpell : IDatabaseCharacter, IUpdate
    {
        IPlayer Owner { get; }
        ISpellBaseInfo BaseInfo { get; }
        ISpellInfo SpellInfo { get; }
        IItem Item { get; }
        byte Tier { get; set; }
        uint AbilityCharges { get; }
        uint MaxAbilityCharges { get; }

        /// <summary>
        /// Stage character spell changes and register their successful-commit acknowledgements.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        /// <summary>
        /// Used for when the client does not have continuous casting enabled
        /// </summary>
        void Cast();

        /// <summary>
        /// Used for continuous casting when the client has it enabled, or spells with Cast Methods like ChargeRelease
        /// </summary>
        void Cast(bool buttonPressed);

        void UseCharge();
    }
}
