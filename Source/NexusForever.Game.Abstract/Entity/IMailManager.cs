using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Abstract.Mail;
using NexusForever.Game.Static.Mail;
using NexusForever.Network.World.Message.Model.Mail;
using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Entity
{
    public interface IMailManager : IUpdate, IDatabaseCharacter
    {
        /// <summary>
        /// Stage mail changes and defer queue, tombstone, and dirty-state acknowledgement until the database commit succeeds.
        /// </summary>
        /// <param name="context">Character database context receiving the staged changes.</param>
        /// <param name="commitScope">Scope that acknowledges the staged changes after a successful commit.</param>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        void SendInitialPackets();

        /// <summary>
        /// Enqueue new incoming <see cref="IMailItem"/> to be processed.
        /// </summary>
        void EnqueueMail(IMailItem mail);

        void SendMail(ClientMailSend mailSend);

        /// <summary>
        /// Send mail to self from a creature.
        /// </summary>
        void SendMail(uint creatureId, DeliverySpeed speed, uint subject, uint body, IEnumerable<uint> itemIds);

        /// <summary>
        /// Delete a <see cref="IMailItem"/> with supplied id.
        /// </summary>
        void MailDelete(ulong mailId);

        /// <summary>
        /// Mark a <see cref="IMailItem"/> as read with supplied id.
        /// </summary>
        void MailMarkAsRead(ulong mailId);

        /// <summary>
        /// Pay cash on delivery for a <see cref="IMailItem"/> with supplied id.
        /// </summary>
        void MailPayCod(ulong mailId);

        // <summary>
        /// Return <see cref="IMailItem"/> to original sender with supplied id.
        /// </summary>
        void ReturnMail(ulong mailId);

        /// <summary>
        /// Take attachment from <see cref="IMailItem"/> with supplied id and index.
        /// </summary>
        void MailTakeAttachment(ulong mailId, uint attachmentIndex, uint unitId);

        /// <summary>
        /// Take cash from <see cref="IMailItem"/> with supplied id.
        /// </summary>
        void MailTakeCash(ulong mailId, uint unitId);
    }
}
