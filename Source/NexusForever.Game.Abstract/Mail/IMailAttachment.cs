using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Mail;

namespace NexusForever.Game.Abstract.Mail
{
    public interface IMailAttachment : IDatabaseCharacter, INetworkBuildable<ServerMailAvailable.Attachment>
    {
        ulong Id { get; }
        uint Index { get; }
        IItem Item { get; }

        /// <summary>
        /// Stage attachment changes and defer clearing their dirty state until the database commit is acknowledged.
        /// </summary>
        /// <param name="context">Character database context receiving the staged changes.</param>
        /// <param name="commitScope">Scope that acknowledges the staged changes after a successful commit.</param>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        /// <summary>
        /// Enqueue <see cref="IMailAttachment"/> to be deleted from the database.
        /// </summary>
        void EnqueueDelete();
    }
}
