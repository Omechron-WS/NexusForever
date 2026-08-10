using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Mail;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Mail;
using NexusForever.Network.World.Message.Model.Mail;
using NetworkIdentity = NexusForever.Network.World.Message.Model.Shared.Identity;

namespace NexusForever.Game.Mail
{
    public class MailItem : IMailItem
    {
        /// <summary>
        /// Determines which fields need saving for <see cref="IMailItem"/> when being saved to the database.
        /// </summary>
        [Flags]
        public enum MailSaveMask
        {
            None                = 0x0000,
            Create              = 0x0001,
            Flags               = 0x0002,
            CurrencyChange      = 0x0004,
            RecipientChange     = 0x0008,
            Delete              = 0x0010
        }

        public ulong Id { get; }

        public ulong RecipientId
        {
            get => recipientId;
            private set
            {
                if (value == recipientId)
                    throw new ArgumentException("Recipient ID must be different than current recipient.");
                recipientId = value;
                saveMask.Mark(MailSaveMask.RecipientChange);
            }
        }
        private ulong recipientId;

        public SenderType SenderType { get; }
        public ulong SenderId { get; }
        public uint CreatureId { get; }

        public string Subject { get; private set; } = "";
        public string Message { get; } = "";
        public uint TextEntrySubject { get; }
        public uint TextEntryMessage { get; }

        public CurrencyType CurrencyType { get; }
        public ulong CurrencyAmount { get; }
        public bool IsCashOnDelivery { get; }

        public bool HasPaidOrCollectedCurrency
        {
            get => hasPaidOrCollectedCurrency;
            private set
            {
                hasPaidOrCollectedCurrency = value;
                saveMask.Mark(MailSaveMask.CurrencyChange);
            }
        }
        private bool hasPaidOrCollectedCurrency;

        public MailFlag Flags
        {
            get => flags;
            private set
            {
                flags = value;
                saveMask.Mark(MailSaveMask.Flags);
            }
        }
        private MailFlag flags;

        public DeliverySpeed DeliverySpeed { get; }
        public DateTime CreateTime { get; }
        public float ExpiryTime => 30f; // TODO: Make this configurable

        public bool PendingCreate => (saveMask.Current & MailSaveMask.Create) != 0;

        /// <summary>
        /// Returns if <see cref="IMailItem"/> is enqueued to be deleted from the database.
        /// </summary>
        public bool PendingDelete => (saveMask.Current & MailSaveMask.Delete) != 0;

        private readonly VersionedSaveMask<MailSaveMask> saveMask;

        private readonly List<IMailAttachment> mailAttachments = new();
        private readonly HashSet<IMailAttachment> deletedAttachments = new();

        /// <summary>
        /// Create a new <see cref="IMailItem"/> from an existing <see cref="CharacterMailModel"/>.
        /// </summary>
        public MailItem(CharacterMailModel model)
        {
            Id                         = model.Id;
            recipientId                = model.RecipientId;
            SenderType                 = (SenderType)model.SenderType;
            SenderId                   = model.SenderId;
            Subject                    = model.Subject;
            Message                    = model.Message;
            TextEntrySubject           = model.TextEntrySubject;
            TextEntryMessage           = model.TextEntryMessage;
            CreatureId                 = model.CreatureId;
            CurrencyType               = (CurrencyType)model.CurrencyType;
            CurrencyAmount             = model.CurrencyAmount;
            IsCashOnDelivery           = Convert.ToBoolean(model.IsCashOnDelivery);
            hasPaidOrCollectedCurrency = Convert.ToBoolean(model.HasPaidOrCollectedCurrency);
            flags                      = (MailFlag)model.Flags;
            DeliverySpeed               = (DeliverySpeed)model.DeliveryTime;
            CreateTime                 = model.CreateTime;

            foreach (CharacterMailAttachmentModel mailAttachment in model.Attachment)
                mailAttachments.Add(new MailAttachment(mailAttachment));

            saveMask = new VersionedSaveMask<MailSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="IMailItem"/> from supplied <see cref="IMailParameters"/>.
        /// </summary>
        public MailItem(MailParameters parameters)
        {
            Id          = AssetManager.Instance.NextMailId;
            recipientId = parameters.RecipientCharacterId;
            SenderType  = parameters.MessageType;

            if (SenderType == SenderType.Player || SenderType == SenderType.GM)
                SenderId = parameters.SenderCharacterId;
            else
                CreatureId = parameters.CreatureId;

            if (parameters.SubjectStringId != 0u)
                TextEntrySubject = parameters.SubjectStringId;
            else
                Subject = parameters.Subject;

            if (parameters.BodyStringId != 0u)
                TextEntryMessage = parameters.BodyStringId;
            else
                Message = parameters.Body;

            CurrencyType = CurrencyType.Credits;

            if (parameters.CodAmount > 0ul)
            {
                CurrencyAmount = parameters.CodAmount;
                IsCashOnDelivery = true;
            }
            else
                CurrencyAmount = parameters.MoneyToGive;

            DeliverySpeed = parameters.DeliverySpeed;
            CreateTime   = DateTime.Now;

            saveMask     = new VersionedSaveMask<MailSaveMask>(MailSaveMask.Create);
        }

        /// <summary>
        /// Enqueue <see cref="IMailItem"/> to be deleted from the database.
        /// </summary>
        public void EnqueueDelete(bool state)
        {
            if (state)
                saveMask.Mark(MailSaveMask.Delete);
            else
                saveMask.Clear(MailSaveMask.Delete);
        }

        public void Save(CharacterContext context)
        {
            VersionedSaveMaskSnapshot<MailSaveMask> snapshot = saveMask.Capture();
            PrepareAttachmentDeletes(snapshot.Mask);
            StageSave(context, snapshot.Mask);
            saveMask.Acknowledge(snapshot);

            if (IsCancelledCreate(snapshot.Mask))
                return;

            foreach (IMailAttachment mailAttachment in mailAttachments)
                mailAttachment.Save(context);

            foreach (IMailAttachment mailAttachment in deletedAttachments)
                mailAttachment.Save(context);

            deletedAttachments.Clear();
        }

        /// <summary>
        /// Stage mail and attachment changes and defer clearing their dirty state until the database commit is acknowledged.
        /// </summary>
        /// <param name="context">Character database context receiving the staged changes.</param>
        /// <param name="commitScope">Scope that acknowledges the staged changes after a successful commit.</param>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<MailSaveMask> snapshot = saveMask.Capture();
            PrepareAttachmentDeletes(snapshot.Mask);
            StageSave(context, snapshot.Mask);
            if (snapshot.Mask != MailSaveMask.None)
                commitScope.Register(() => saveMask.Acknowledge(snapshot));

            if (IsCancelledCreate(snapshot.Mask))
                return;

            foreach (IMailAttachment mailAttachment in mailAttachments.ToArray())
                mailAttachment.Save(context, commitScope);

            IMailAttachment[] stagedDeletedAttachments = deletedAttachments.ToArray();
            foreach (IMailAttachment mailAttachment in stagedDeletedAttachments)
                mailAttachment.Save(context, commitScope);

            if (stagedDeletedAttachments.Length != 0)
            {
                commitScope.Register(() =>
                {
                    foreach (IMailAttachment mailAttachment in stagedDeletedAttachments)
                        deletedAttachments.Remove(mailAttachment);
                });
            }
        }

        private void PrepareAttachmentDeletes(MailSaveMask stagedMask)
        {
            bool isCreate = (stagedMask & MailSaveMask.Create) != 0;
            bool isDelete = (stagedMask & MailSaveMask.Delete) != 0;
            if (!isDelete || isCreate)
                return;

            foreach (IMailAttachment mailAttachment in mailAttachments)
            {
                mailAttachment.EnqueueDelete();
                mailAttachment.Item.EnqueueDelete(true);
            }
        }

        private static bool IsCancelledCreate(MailSaveMask stagedMask)
        {
            return (stagedMask & (MailSaveMask.Create | MailSaveMask.Delete))
                == (MailSaveMask.Create | MailSaveMask.Delete);
        }

        private void StageSave(CharacterContext context, MailSaveMask stagedMask)
        {
            bool isCreate = (stagedMask & MailSaveMask.Create) != 0;
            bool isDelete = (stagedMask & MailSaveMask.Delete) != 0;
            if (IsCancelledCreate(stagedMask))
                return;

            if (isCreate)
            {
                context.Add(new CharacterMailModel
                {
                    Id                         = Id,
                    RecipientId                = RecipientId,
                    SenderType                 = (byte)SenderType,
                    SenderId                   = SenderId,
                    Subject                    = Subject,
                    Message                    = Message,
                    TextEntrySubject           = TextEntrySubject,
                    TextEntryMessage           = TextEntryMessage,
                    CreatureId                 = CreatureId,
                    CurrencyType               = (byte)CurrencyType,
                    CurrencyAmount             = CurrencyAmount,
                    IsCashOnDelivery           = Convert.ToByte(IsCashOnDelivery),
                    HasPaidOrCollectedCurrency = Convert.ToByte(HasPaidOrCollectedCurrency),
                    Flags                      = (byte)Flags,
                    DeliveryTime               = (byte)DeliverySpeed,
                    CreateTime                 = CreateTime
                });
            }
            else if (isDelete)
            {
                var model = new CharacterMailModel
                {
                    Id = Id
                };

                context.Entry(model).State = EntityState.Deleted;
            }
            else if (stagedMask != MailSaveMask.None)
            {
                var model = new CharacterMailModel
                {
                    Id = Id
                };

                EntityEntry<CharacterMailModel> entity = context.Attach(model);
                if ((stagedMask & MailSaveMask.Flags) != 0)
                {
                    model.Flags = Convert.ToByte(Flags);
                    entity.Property(p => p.Flags).IsModified = true;
                }

                if ((stagedMask & MailSaveMask.CurrencyChange) != 0)
                {
                    model.HasPaidOrCollectedCurrency = Convert.ToByte(HasPaidOrCollectedCurrency);
                    entity.Property(p => p.HasPaidOrCollectedCurrency).IsModified = true;
                }

                if ((stagedMask & MailSaveMask.RecipientChange) != 0)
                {
                    model.RecipientId = RecipientId;
                    entity.Property(p => p.RecipientId).IsModified = true;

                    model.Subject = Subject;
                    entity.Property(p => p.Subject).IsModified = true;
                }
            }
        }

        /// <summary>
        /// Returns the specific <see cref="IMailAttachment"/> based on its index.
        /// </summary>
        public IMailAttachment GetAttachment(uint index)
        {
            return mailAttachments.SingleOrDefault(attachment => attachment.Index == index);
        }

        /// <summary>
        /// Mark this <see cref="IMailItem"/> as read by the player.
        /// </summary>
        public void MarkAsRead()
        {
            Flags |= MailFlag.IsRead;
        }

        /// <summary>
        /// Mark this <see cref="IMailItem"/> as paid, or taken the currency attached.
        /// </summary>
        public void PayOrTakeCash()
        {
            HasPaidOrCollectedCurrency = true;
            MarkAsNotReturnable();
        }

        /// <summary>
        /// Mark this <see cref="IMailItem"/> as not returnable.
        /// </summary>
        public void MarkAsNotReturnable()
        {
            Flags |= MailFlag.NotReturnable;
        }

        /// <summary>
        /// Return this <see cref="IMailItem"/> to sender.
        /// </summary>
        public void ReturnMail()
        {
            RecipientId = SenderId;
            Subject = $"Returned: {Subject}";
            MarkAsNotReturnable();
        }

        /// <summary>
        /// Returns whether this item is ready to be delivered based on <see cref="DeliverySpeed"/>.
        /// </summary>
        public bool IsReadyToDeliver()
        {
            if (DeliverySpeed == DeliverySpeed.Instant)
                return true;

            if (DeliverySpeed == DeliverySpeed.Hour)
                return DateTime.Now
                    .Subtract(CreateTime)
                    .TotalHours > 1;

            if (DeliverySpeed == DeliverySpeed.Day)
                return DateTime.Now
                    .Subtract(CreateTime)
                    .TotalDays > 1;

            return false;
        }

        /// <summary>
        /// Add a <see cref="IMailAttachment"/> to this <see cref="IMailItem"/>.
        /// </summary>
        public void AttachmentAdd(IMailAttachment mailAttachment)
        {
            mailAttachments.Add(mailAttachment);
        }

        /// <summary>
        /// Remove a <see cref="IMailAttachment"/> from this <see cref="IMailItem"/>.
        /// </summary>
        public void AttachmentDelete(IMailAttachment mailAttachment, uint index)
        {
            ArgumentNullException.ThrowIfNull(mailAttachment);
            if (mailAttachment.Index != index || !mailAttachments.Remove(mailAttachment))
                throw new ArgumentException("The attachment does not belong to this mail item.", nameof(mailAttachment));

            mailAttachment.EnqueueDelete();
            deletedAttachments.Add(mailAttachment);
        }

        public ServerMailAvailable.Mail Build()
        {
            bool isPlayer = SenderType == SenderType.Player || SenderType == SenderType.GM;

            var serverMailItem = new ServerMailAvailable.Mail
            {
                MailId               = Id,
                SenderType           = SenderType,
                Subject              = Subject,
                Message              = Message,
                SubjectStringId     = TextEntrySubject,
                BodyStringId     = TextEntryMessage,
                FromCreatureId           = !isPlayer ? CreatureId : 0,
                CurrencySentType     = 0,
                CurrencySentAmount   = !IsCashOnDelivery && !HasPaidOrCollectedCurrency ? CurrencyAmount : 0,
                CostOnDeliveryAmount = IsCashOnDelivery && !HasPaidOrCollectedCurrency ? CurrencyAmount : 0,
                ExpiryTimeInDays     = ExpiryTime,
                Flags                = Flags,
                Sender               = new NetworkIdentity
                {
                    RealmId = isPlayer ? RealmContext.Instance.RealmId : (ushort)0,
                    Id      = isPlayer ? SenderId : 0ul
                },
            };

            foreach (IMailAttachment attachment in mailAttachments)
                serverMailItem.Attachments.Add(attachment.Build());

            return serverMailItem;
        }

        public IEnumerator<IMailAttachment> GetEnumerator()
        {
            return mailAttachments.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
