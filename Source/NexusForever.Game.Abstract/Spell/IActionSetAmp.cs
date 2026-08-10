using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Abstract.Spell
{
    public interface IActionSetAmp : IDatabaseCharacter, IDatabaseState
    {
        EldanAugmentationEntry Entry { get; set; }

        /// <summary>
        /// Stage AMP changes and register their successful-commit acknowledgements.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        /// <summary>
        /// Stage AMP changes and invoke the supplied action when a requested deletion commits.
        /// </summary>
        void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged);
    }
}
