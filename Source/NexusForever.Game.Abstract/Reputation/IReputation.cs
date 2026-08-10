using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Static.Reputation;

namespace NexusForever.Game.Abstract.Reputation
{
    public interface IReputation : IDatabaseCharacter
    {
        /// <summary>
        /// Stage reputation changes and register acknowledgements for a successful commit.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        IFactionNode Entry { get; }
        Faction Id { get; }
        float Amount { get; set; }
    }
}
