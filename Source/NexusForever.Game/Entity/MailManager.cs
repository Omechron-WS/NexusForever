using System.Collections.Concurrent;
using System.Numerics;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Character;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Mail;
using NexusForever.Game.Character;
using NexusForever.Game.Mail;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Mail;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Mail;
using NexusForever.Network.World.Message.Static;
using NexusForever.Shared.Game;
using NLog;

namespace NexusForever.Game.Entity
{
    public class MailManager : IMailManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly IPlayer player;
        private readonly IPlayerManager playerManager;
        private readonly object outgoingMailSyncRoot = new();
        private readonly Queue<IMailItem> outgoingMail = new();
        private readonly ConcurrentQueue<IMailItem> incomingMail = new();
        private readonly List<IMailItem> pendingMail = new();
        private readonly Dictionary<ulong, IMailItem> availableMail = new();

        // timer to check pending mail every second
        private readonly UpdateTimer mailTimer = new(TimeSpan.FromSeconds(1));

        /// <summary>
        /// Create a new <see cref="IMailManager"/> from existing <see cref="CharacterModel"/> database model.
        /// </summary>
        public MailManager(IPlayer owner, CharacterModel model)
            : this(owner, model, null)
        {
        }

        /// <summary>
        /// Create a new <see cref="IMailManager"/> with an explicit online-player resolver.
        /// </summary>
        /// <param name="owner">Player that owns this mail manager.</param>
        /// <param name="model">Character database model containing existing mail.</param>
        /// <param name="playerManager">Online-player resolver used for post-commit delivery.</param>
        public MailManager(IPlayer owner, CharacterModel model, IPlayerManager playerManager)
        {
            player             = owner;
            this.playerManager = playerManager;
            foreach (CharacterMailModel mailModel in model.Mail)
            {
                var mail = new MailItem(mailModel);
                if (mail.IsReadyToDeliver())
                    availableMail.Add(mail.Id, mail);
                else
                    pendingMail.Add(mail);
            }
        }

        public void Update(double lastTick)
        {
            bool sendAvailableMail = DrainIncomingMail();

            mailTimer.Update(lastTick);
            if (mailTimer.HasElapsed)
            {
                foreach (IMailItem mail in pendingMail.ToArray())
                {
                    if (!mail.IsReadyToDeliver())
                        continue;

                    pendingMail.Remove(mail);
                    if (availableMail.TryAdd(mail.Id, mail))
                        sendAvailableMail = true;
                }

                // TODO: remove expired mail

                mailTimer.Reset();
            }

            // prevent sending multiple mail packets in one update
            if (sendAvailableMail)
                SendAvailableMail();
        }

        private bool DrainIncomingMail()
        {
            bool sendAvailableMail = false;
            while (incomingMail.TryDequeue(out IMailItem mail))
            {
                if (availableMail.ContainsKey(mail.Id) || pendingMail.Any(pending => pending.Id == mail.Id))
                    continue;

                if (mail.IsReadyToDeliver())
                {
                    availableMail.Add(mail.Id, mail);
                    sendAvailableMail = true;
                }
                else
                    pendingMail.Add(mail);
            }

            return sendAvailableMail;
        }

        public void Save(CharacterContext context)
        {
            lock (outgoingMailSyncRoot)
            {
                while (outgoingMail.TryDequeue(out IMailItem mail))
                    mail.Save(context);
            }

            foreach (IMailItem mail in availableMail.Values.ToList())
            {
                if (mail.PendingDelete)
                    availableMail.Remove(mail.Id);

                mail.Save(context);
            }
        }

        /// <summary>
        /// Stage mail changes and defer queue, tombstone, and dirty-state acknowledgement until the database commit succeeds.
        /// </summary>
        /// <param name="context">Character database context receiving the staged changes.</param>
        /// <param name="commitScope">Scope that acknowledges the staged changes after a successful commit.</param>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            IMailItem[] stagedOutgoingMail;
            lock (outgoingMailSyncRoot)
                stagedOutgoingMail = outgoingMail.ToArray();

            foreach (IMailItem mail in stagedOutgoingMail)
                mail.Save(context, commitScope);

            IMailItem[] stagedAvailableMail = availableMail.Values.ToArray();
            IMailItem[] stagedDeletedMail = stagedAvailableMail
                .Where(mail => mail.PendingDelete)
                .ToArray();
            foreach (IMailItem mail in stagedAvailableMail)
                mail.Save(context, commitScope);

            if (stagedOutgoingMail.Length == 0 && stagedDeletedMail.Length == 0)
                return;

            commitScope.Register(() =>
            {
                IReadOnlyList<IMailItem> committedOutgoingMail = RemoveCommittedOutgoingMail(stagedOutgoingMail);
                foreach (IMailItem mail in stagedDeletedMail)
                {
                    if (availableMail.TryGetValue(mail.Id, out IMailItem current) && ReferenceEquals(current, mail))
                        availableMail.Remove(mail.Id);
                }

                foreach (IMailItem mail in committedOutgoingMail)
                    DeliverCommittedMail(mail);
            });
        }

        private IReadOnlyList<IMailItem> RemoveCommittedOutgoingMail(IReadOnlyList<IMailItem> stagedMail)
        {
            var committedMail = new List<IMailItem>(stagedMail.Count);
            lock (outgoingMailSyncRoot)
            {
                foreach (IMailItem expected in stagedMail)
                {
                    if (!outgoingMail.TryPeek(out IMailItem current) || !ReferenceEquals(current, expected))
                    {
                        log.Error("Outgoing mail queue changed before commit acknowledgement.");
                        break;
                    }

                    outgoingMail.Dequeue();
                    committedMail.Add(expected);
                }
            }

            return committedMail;
        }

        private void DeliverCommittedMail(IMailItem mail)
        {
            try
            {
                if (mail.RecipientId == player.CharacterId)
                {
                    EnqueueMail(mail);
                    return;
                }

                IPlayer recipient = playerManager?.GetPlayer(mail.RecipientId)
                    ?? (playerManager == null ? PlayerManager.Instance.GetPlayer(mail.RecipientId) : null);
                recipient?.MailManager.EnqueueMail(mail);
            }
            catch (Exception exception)
            {
                // Persistence is authoritative. A missed online notification is recovered on the next login.
                log.Error(exception, $"Failed to notify online recipient {mail.RecipientId} about committed mail {mail.Id}.");
            }
        }

        /// <summary>
        /// Called by <see cref="IPlayer"/> to send available mail on entering map.
        /// </summary>
        public void SendInitialPackets()
        {
            SendAvailableMail();
        }

        private void SendAvailableMail()
        {
            var mailAvailable = new ServerMailAvailable
            {
                NewMail = true
            };

            foreach (IMailItem mail in availableMail.Values.Where(m => !m.PendingDelete))
                mailAvailable.MailList.Add(mail.Build());

            player.Session.EnqueueMessageEncrypted(mailAvailable);
        }

        /// <summary>
        /// Enqueue new incoming <see cref="IMailItem"/> to be processed.
        /// </summary>
        public void EnqueueMail(IMailItem mail)
        {
            ArgumentNullException.ThrowIfNull(mail);
            incomingMail.Enqueue(mail);
        }

        /// <summary>
        /// Send mail to another <see cref="IPlayer"/>.
        /// </summary>
        public void SendMail(ClientMailSend mailSend)
        {
            ICharacter targetCharacter = CharacterManager.Instance.GetCharacter(mailSend.Name);

            var items = new List<IItem>();
            GenericError GetResult()
            {
                if (targetCharacter == null)
                    return GenericError.MailCannotFindPlayer;

                if (targetCharacter.CharacterId == player.CharacterId)
                    return GenericError.MailCannotMailSelf;

                // TODO: Check that the player is not blocked

                if (mailSend.CashOnDeliveryAmount > 0ul && mailSend.CreditsSent > 0ul)
                    return GenericError.MailCanNotHaveCoDAndGift;

                if (mailSend.CashOnDeliveryAmount > 0ul && mailSend.Items.All(i => i == 0ul))
                    return GenericError.MailFailedToCreate;

                if (mailSend.Items.Any(i => i != 0ul))
                {
                    if (!IsTargetMailBoxInRange(mailSend.MailboxUnitId))
                        return GenericError.MailMailBoxOutOfRange;

                    var itemGuids = new HashSet<ulong>();
                    foreach (ulong itemGuid in mailSend.Items.Where(i => i != 0ul))
                    {
                        if (!itemGuids.Add(itemGuid))
                            return GenericError.MailInvalidInventorySlot;

                        IItem item = player.Inventory.GetItem(itemGuid);
                        if (item == null
                            || item.Guid != itemGuid
                            || item.CharacterId != player.CharacterId
                            || item.Location != InventoryLocation.Inventory
                            || item.PendingDelete
                            || item.StackCount == 0u
                            || item.Info?.Entry == null)
                            return GenericError.MailInvalidInventorySlot;

                        // TODO: Check the Item can be traded.
                        items.Add(item);
                    }
                }

                uint cost = CalculateMailCost(mailSend.DeliverySpeed, items);
                if (!player.CurrencyManager.CanAfford(CurrencyType.Credits, cost))
                    return GenericError.MailInsufficientFunds;

                if (!player.CurrencyManager.CanAfford(CurrencyType.Credits, mailSend.CreditsSent))
                    return GenericError.MailInsufficientFunds;

                return GenericError.Ok;
            }

            GenericError result = GetResult();
            if (result == GenericError.Ok)
            {
                var parameters = new MailParameters
                {
                    RecipientCharacterId = targetCharacter.CharacterId,
                    SenderCharacterId    = player.CharacterId,
                    MessageType          = SenderType.Player,
                    Subject              = mailSend.Subject,
                    Body                 = mailSend.Message,
                    MoneyToGive          = mailSend.CreditsSent,
                    CodAmount            = mailSend.CashOnDeliveryAmount,
                    DeliverySpeed         = mailSend.DeliverySpeed
                };

                foreach (IItem item in items)
                    player.Inventory.ItemRemove(item);

                SendMail(parameters, items);

                uint cost = CalculateMailCost(mailSend.DeliverySpeed, items);
                player.CurrencyManager.CurrencySubtractAmount(CurrencyType.Credits, cost);

                if (mailSend.CreditsSent > 0ul)
                    player.CurrencyManager.CurrencySubtractAmount(CurrencyType.Credits, mailSend.CreditsSent);
            }

            player.Session.EnqueueMessageEncrypted(new ServerMailResult
            {
                Action = 1,
                MailId = 0,
                Result = result
            });
        }

        /// <summary>
        /// Send mail to self from a creature.
        /// </summary>
        public void SendMail(uint creatureId, DeliverySpeed speed, uint subject, uint body, IEnumerable<uint> itemIds)
        {
            if (GameTableManager.Instance.Creature2.GetEntry(creatureId) == null)
                throw new ArgumentException($"Invalid creature {creatureId} for mail sender!");

            if (GameTableManager.Instance.LocalizedText.GetEntry(subject) == null)
                throw new ArgumentException($"Invalid localised text {subject} for mail subject!");

            if (GameTableManager.Instance.LocalizedText.GetEntry(body) == null)
                throw new ArgumentException($"Invalid localised text {body} for mail body!");

            var parameters = new MailParameters
            {
                MessageType          = SenderType.Creature,
                RecipientCharacterId = player.CharacterId,
                CreatureId           = creatureId,
                SubjectStringId      = subject,
                BodyStringId         = body,
                DeliverySpeed        = speed
            };

            var items = new List<IItem>();
            foreach (uint itemId in itemIds)
            {
                IItemInfo info = ItemManager.Instance.GetItemInfo(itemId);
                if (info == null)
                    throw new ArgumentException($"Invalid item {itemId} for mail attachment!");

                var item = new Item(null, info);
                items.Add(item);
            }

            SendMail(parameters, items);
        }

        // TODO: Handle sending mail from auctions to users upon auction end
        // TODO: Handle sending mail from GMs to replace missing items
        private void SendMail(MailParameters parameters, IEnumerable<IItem> items)
        {
            var mail = new MailItem(parameters);

            uint index = 0;
            foreach (IItem item in items)
            {
                var attachment = new MailAttachment(mail.Id, index++, item);
                mail.AttachmentAdd(attachment);
            }

            // NOTE: outgoing mail is flushed on character save to prevent any issues,
            // this means that instant mail could take up to 60 seconds (by default) to actually arrive
            lock (outgoingMailSyncRoot)
                outgoingMail.Enqueue(mail);
        }

        private uint CalculateMailCost(DeliverySpeed time, List<IItem> items)
        {
            GameFormulaEntry GetMailParameters()
            {
                if (items.Count == 0)
                    return GameTableManager.Instance.GameFormula.GetEntry(860);

                return time switch
                {
                    DeliverySpeed.Instant => GameTableManager.Instance.GameFormula.GetEntry(861),
                    DeliverySpeed.Hour => GameTableManager.Instance.GameFormula.GetEntry(862),
                    DeliverySpeed.Day => GameTableManager.Instance.GameFormula.GetEntry(863),
                    _ => null
                };
            }

            GameFormulaEntry parameters = GetMailParameters();
            uint cost = parameters.Dataint0;

            foreach (IItem item in items)
            {
                cost += parameters.Dataint01;
                // only instant delivery speed takes item worth into consideration
                if (parameters.Datafloat01 > 0.0f)
                {
                    if (item.GetVendorSellCurrency(0) == CurrencyType.Credits)
                        cost += (uint)(item.GetVendorSellAmount(0) * parameters.Datafloat01);
                    if (item.GetVendorSellCurrency(1) == CurrencyType.Credits)
                        cost += (uint)(item.GetVendorSellAmount(1) * parameters.Datafloat01);
                }
            }

            return cost;
        }

        /// <summary>
        /// Delete a <see cref="IMailItem"/> with supplied id.
        /// </summary>
        public void MailDelete(ulong mailId)
        {
            GenericError result = GenericError.Ok;
            if (!availableMail.TryGetValue(mailId, out IMailItem mailItem)
                || mailItem.PendingDelete
                || mailItem.RecipientId != player.CharacterId)
                result = GenericError.MailDoesNotExist;
            else if (mailItem.Any()
                || (mailItem.CurrencyAmount > 0ul && !mailItem.HasPaidOrCollectedCurrency))
                result = GenericError.MailCannotDelete;

            if (result == GenericError.Ok)
            {
                mailItem.EnqueueDelete(true);

                player.Session.EnqueueMessageEncrypted(new ServerMailUnavailable
                {
                    MailId = mailItem.Id
                });
            }

            player.Session.EnqueueMessageEncrypted(new ServerMailResult
            {
                Action = 5,
                MailId = mailId,
                Result = result
            });
        }

        /// <summary>
        /// Mark a <see cref="IMailItem"/> as read with supplied id.
        /// </summary>
        public void MailMarkAsRead(ulong mailId)
        {
            if (!availableMail.TryGetValue(mailId, out IMailItem mailItem))
                return;

            mailItem.MarkAsRead();
        }

        /// <summary>
        /// Pay cash on delivery for a <see cref="IMailItem"/> with supplied id.
        /// </summary>
        public void MailPayCod(ulong mailId, uint unitId)
        {
            IMailItem mail;
            GenericError GetResult()
            {
                if (!availableMail.TryGetValue(mailId, out mail))
                    return GenericError.MailDoesNotExist;

                if (mail.PendingDelete || mail.RecipientId != player.CharacterId)
                    return GenericError.MailDoesNotExist;

                if (!mail.IsCashOnDelivery
                    || mail.HasPaidOrCollectedCurrency
                    || mail.CurrencyType != CurrencyType.Credits
                    || mail.CurrencyAmount == 0ul)
                    return GenericError.MailBusy;

                if (!mail.Any())
                    return GenericError.MailNoAttachment;

                if (unitId == 0u || !IsTargetMailBoxInRange(unitId))
                    return GenericError.MailMailBoxOutOfRange;

                if (!player.CurrencyManager.CanAfford(CurrencyType.Credits, mail.CurrencyAmount))
                    return GenericError.MailInsufficientFunds;

                return GenericError.Ok;
            }

            GenericError result = GetResult();
            if (result == GenericError.Ok)
            {
                player.CurrencyManager.CurrencySubtractAmount(CurrencyType.Credits, mail.CurrencyAmount);
                mail.PayOrTakeCash();

                var parameters = new MailParameters
                {
                    RecipientCharacterId = mail.SenderId,
                    SenderCharacterId    = mail.RecipientId,
                    MessageType          = SenderType.Player,
                    Subject              = $"Cash from: {mail.Subject}",
                    MoneyToGive          = mail.CurrencyAmount,
                    DeliverySpeed        = DeliverySpeed.Instant
                };

                SendMail(parameters, Enumerable.Empty<Item>());
            }

            player.Session.EnqueueMessageEncrypted(new ServerMailResult
            {
                Action = 3,
                MailId = mailId,
                Result = result
            });
        }

        /// <summary>
        /// Return <see cref="IMailItem"/> to original sender with supplied id.
        /// </summary>
        public void ReturnMail(ulong mailId)
        {
            IMailItem mailItem;
            GenericError GetResult()
            {
                if (!availableMail.TryGetValue(mailId, out mailItem)
                    || mailItem.PendingDelete
                    || mailItem.Id != mailId
                    || mailItem.RecipientId != player.CharacterId)
                    return GenericError.MailDoesNotExist;

                if (mailItem.SenderType != SenderType.Player
                    || mailItem.SenderId == 0ul
                    || mailItem.SenderId == mailItem.RecipientId
                    || (mailItem.Flags & MailFlag.NotReturnable) != 0)
                    return GenericError.MailCannotReturn;

                return GenericError.Ok;
            }

            GenericError result = GetResult();
            if (result == GenericError.Ok)
            {
                mailItem.ReturnMail();

                availableMail.Remove(mailItem.Id);
                lock (outgoingMailSyncRoot)
                    outgoingMail.Enqueue(mailItem);

                player.Session.EnqueueMessageEncrypted(new ServerMailUnavailable
                {
                    MailId = mailId
                });
            }

            player.Session.EnqueueMessageEncrypted(new ServerMailResult
            {
                Action = 1,
                MailId = mailId,
                Result = result
            });
        }

        /// <summary>
        /// Take attachment from <see cref="IMailItem"/> with supplied id and index.
        /// </summary>
        public void MailTakeAttachment(ulong mailId, uint attachmentIndex, uint unitId)
        {
            IMailItem mailItem;
            IMailAttachment mailAttachment = null;

            GenericError GetResult()
            {
                if (!availableMail.TryGetValue(mailId, out mailItem))
                    return GenericError.MailDoesNotExist;

                if (mailItem.PendingDelete || mailItem.RecipientId != player.CharacterId)
                    return GenericError.MailDoesNotExist;

                if (mailItem.IsCashOnDelivery && !mailItem.HasPaidOrCollectedCurrency)
                    return GenericError.MailBusy;

                mailAttachment = mailItem.GetAttachment(attachmentIndex);
                if (mailAttachment == null)
                    return GenericError.MailNoAttachment;

                if (unitId == 0u || !IsTargetMailBoxInRange(unitId))
                    return GenericError.MailMailBoxOutOfRange;

                if (player.Inventory.IsInventoryFull(InventoryLocation.Inventory))
                    return GenericError.ItemInventoryFull;

                return GenericError.Ok;
            }

            GenericError result = GetResult();
            if (result == GenericError.Ok)
            {
                mailAttachment.Item.CharacterId = player.CharacterId;
                player.Inventory.AddItem(mailAttachment.Item, InventoryLocation.Inventory);

                mailItem.MarkAsNotReturnable();
                mailItem.AttachmentDelete(mailAttachment, attachmentIndex);
            }

            player.Session.EnqueueMessageEncrypted(new ServerMailTakeAttachment
            {
                MailId = mailId,
                Result = result,
                Index  = attachmentIndex
            });
        }

        /// <summary>
        /// Take cash from <see cref="IMailItem"/> with supplied id.
        /// </summary>
        public void MailTakeCash(ulong mailId, uint unitId)
        {
            IMailItem mailItem;
            GenericError GetResult()
            {
                if (!availableMail.TryGetValue(mailId, out mailItem))
                    return GenericError.MailDoesNotExist;

                if (mailItem.PendingDelete || mailItem.RecipientId != player.CharacterId)
                    return GenericError.MailDoesNotExist;

                if (mailItem.IsCashOnDelivery
                    || mailItem.HasPaidOrCollectedCurrency
                    || mailItem.CurrencyType != CurrencyType.Credits
                    || mailItem.CurrencyAmount == 0ul)
                    return GenericError.MailNoAttachment;

                if (unitId == 0u || !IsTargetMailBoxInRange(unitId))
                    return GenericError.MailMailBoxOutOfRange;

                return GenericError.Ok;
            }

            GenericError result = GetResult();
            if (result == GenericError.Ok)
            {
                player.CurrencyManager.CurrencyAddAmount(mailItem.CurrencyType, mailItem.CurrencyAmount);
                mailItem.PayOrTakeCash();
            }

            player.Session.EnqueueMessageEncrypted(new ServerMailResult
            {
                Action = 2,
                MailId = mailId,
                Result = result
            });
        }

        /// <summary>
        /// Checks to see if the targeted <see cref="IMailItem"/> is in range.
        /// </summary>
        private bool IsTargetMailBoxInRange(uint unitId)
        {
            // native client function MailSystemLib.AtMailbox also uses entry 237 for distance check
            GameFormulaEntry entry = GameTableManager.Instance.GameFormula.GetEntry(237);
            if (entry == null)
                throw new InvalidOperationException();

            var entity = player.GetVisible<IWorldEntity>(unitId);
            return entity is IMailboxEntity && Vector3.DistanceSquared(player.Position, entity.Position) < entry.Datafloat0 * entry.Datafloat0; // Checking squared distance avoids a slow sqrt operation.
        }
    }
}
