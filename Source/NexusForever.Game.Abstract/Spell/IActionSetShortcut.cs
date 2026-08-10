using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Abstract.Spell
{
    public interface IActionSetShortcut : IDatabaseCharacter, IDatabaseState
    {
        UILocation Location { get; }
        ShortcutType ShortcutType { get; set; }
        uint ObjectId { get; set; }
        byte Tier { get; set; }

        /// <summary>
        /// Stage shortcut changes and register their successful-commit acknowledgements.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        /// <summary>
        /// Stage shortcut changes and invoke the supplied action when a requested deletion commits.
        /// </summary>
        void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged);
    }
}
