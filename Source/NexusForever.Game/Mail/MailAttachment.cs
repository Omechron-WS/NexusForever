using Microsoft.EntityFrameworkCore;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Mail;
using NexusForever.Game.Entity;
using NexusForever.Network.World.Message.Model.Mail;

namespace NexusForever.Game.Mail
{
    public class MailAttachment : IMailAttachment
    {
        /// <summary>
        /// Determines which fields need saving for <see cref="IMailAttachment"/> when being saved to the database.
        /// </summary>
        [Flags]
        public enum MailAttachmentSaveMask
        {
            None   = 0x0000,
            Create = 0x0001,
            Delete = 0x0002
        }

        public ulong Id { get; }
        public uint Index { get; }
        public IItem Item { get; }

        private readonly VersionedSaveMask<MailAttachmentSaveMask> saveMask;

        /// <summary>
        /// Create a new <see cref="IMailAttachment"/> from an existing <see cref="CharacterMailAttachmentModel"/> model.
        /// </summary>
        /// <param name="model"></param>
        public MailAttachment(CharacterMailAttachmentModel model)
        {
            Id       = model.Id;
            Index    = model.Index;
            Item     = new Item(model.Item);

            saveMask = new VersionedSaveMask<MailAttachmentSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="IMailAttachment"/>.
        /// </summary>
        public MailAttachment(ulong mailId, uint index, IItem item)
        {
            Id       = mailId;
            Index    = index;
            Item     = item;

            saveMask = new VersionedSaveMask<MailAttachmentSaveMask>(MailAttachmentSaveMask.Create);
        }

        /// <summary>
        /// Enqueue <see cref="IMailAttachment"/> to be deleted from the database.
        /// </summary>
        public void EnqueueDelete()
        {
            saveMask.Mark(MailAttachmentSaveMask.Delete);
        }

        public void Save(CharacterContext context)
        {
            VersionedSaveMaskSnapshot<MailAttachmentSaveMask> snapshot = saveMask.Capture();
            StageSave(context, snapshot.Mask);
            saveMask.Acknowledge(snapshot);

            SaveItem(context, null, snapshot.Mask);
        }

        /// <summary>
        /// Stage attachment changes and defer clearing their dirty state until the database commit is acknowledged.
        /// </summary>
        /// <param name="context">Character database context receiving the staged changes.</param>
        /// <param name="commitScope">Scope that acknowledges the staged changes after a successful commit.</param>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<MailAttachmentSaveMask> snapshot = saveMask.Capture();
            StageSave(context, snapshot.Mask);
            if (snapshot.Mask != MailAttachmentSaveMask.None)
                commitScope.Register(() => saveMask.Acknowledge(snapshot));

            SaveItem(context, commitScope, snapshot.Mask);
        }

        private void StageSave(CharacterContext context, MailAttachmentSaveMask stagedMask)
        {
            bool isCreate = (stagedMask & MailAttachmentSaveMask.Create) != 0;
            bool isDelete = (stagedMask & MailAttachmentSaveMask.Delete) != 0;
            if (isCreate && isDelete)
                return;

            if (isCreate)
            {
                context.Add(new CharacterMailAttachmentModel
                {
                    Id       = Id,
                    Index    = Index,
                    ItemGuid = Item.Guid
                });
            }
            else if (isDelete)
            {
                var model = new CharacterMailAttachmentModel
                {
                    Id    = Id,
                    Index = Index
                };

                context.Entry(model).State = EntityState.Deleted;
            }
        }

        private void SaveItem(CharacterContext context, ISaveCommitScope commitScope, MailAttachmentSaveMask stagedMask)
        {
            bool isCreate = (stagedMask & MailAttachmentSaveMask.Create) != 0;
            bool isDelete = (stagedMask & MailAttachmentSaveMask.Delete) != 0;

            // A claimed attachment is also present in Inventory. Inventory owns its item update,
            // so the attachment tombstone must not stage the same item entity a second time.
            bool shouldSaveItem = !isDelete || Item.PendingDelete;
            if (isCreate && isDelete && Item.PendingCreate)
                shouldSaveItem = false;

            if (!shouldSaveItem)
                return;

            if (commitScope == null)
                Item.Save(context);
            else
                Item.Save(context, commitScope);
        }

        public ServerMailAvailable.Attachment Build()
        {
            return new ServerMailAvailable.Attachment
            {
                Item2Id     = Item.Id,
                StackCount  = Item.StackCount
            };
        }
    }
}
