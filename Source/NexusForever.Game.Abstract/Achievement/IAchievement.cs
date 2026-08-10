using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Network.Message;

namespace NexusForever.Game.Abstract.Achievement
{
    public interface IAchievement : IDatabaseCharacter, INetworkBuildable<Network.World.Message.Model.Achievement.Achievement>
    {
        IAchievementInfo Info { get; }
        ushort Id { get; }
        uint Data0 { get; set; }
        uint Data1 { get; set; }
        DateTime? DateCompleted { get; set; }

        /// <summary>
        /// Stage achievement changes and acknowledge them after the character database commits.
        /// </summary>
        /// <param name="context">Character database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        /// <summary>
        /// Returns if <see cref="IAchievement"/> has been completed.
        /// </summary>
        bool IsComplete();
    }
}
