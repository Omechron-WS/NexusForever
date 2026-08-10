using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Mail;
using NexusForever.Game.Entity;
using NexusForever.Game.Mail;
using NexusForever.Game.Static.Mail;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Mail;

namespace NexusForever.Game.Tests.Persistence
{
    public sealed class MailPersistenceTests
    {
        [Fact]
        public void Save_OutgoingMailRemainsRetryableAndUndeliveredUntilAcknowledged()
        {
            var recipientMailManager = new Mock<IMailManager>();
            var recipient = new Mock<IPlayer>();
            recipient.SetupGet(player => player.MailManager).Returns(recipientMailManager.Object);
            var playerManager = new Mock<IPlayerManager>();
            playerManager.Setup(manager => manager.GetPlayer(20ul)).Returns(recipient.Object);

            MailManager mailManager = CreateMailManager(10ul, playerManager.Object, CreateMailModel(
                id: 100ul,
                recipientId: 10ul,
                senderId: 20ul));
            mailManager.ReturnMail(100ul);

            using TestCharacterContext failedContext = CreateContext();
            var failedScope = new SaveCommitScope();
            mailManager.Save(failedContext, failedScope);
            recipientMailManager.Verify(manager => manager.EnqueueMail(It.IsAny<IMailItem>()), Times.Never);

            using TestCharacterContext retryContext = CreateContext();
            var retryScope = new SaveCommitScope();
            mailManager.Save(retryContext, retryScope);
            recipientMailManager.Verify(manager => manager.EnqueueMail(It.IsAny<IMailItem>()), Times.Never);

            retryScope.CreateAcknowledgement().Acknowledge();
            recipientMailManager.Verify(manager => manager.EnqueueMail(
                It.Is<IMailItem>(mail => mail.Id == 100ul)), Times.Once);
        }

        [Fact]
        public void Save_OutgoingMailDeliveryOccursExactlyOnceAfterAcknowledgement()
        {
            var recipientMailManager = new Mock<IMailManager>();
            var recipient = new Mock<IPlayer>();
            recipient.SetupGet(player => player.MailManager).Returns(recipientMailManager.Object);
            var playerManager = new Mock<IPlayerManager>();
            playerManager.Setup(manager => manager.GetPlayer(20ul)).Returns(recipient.Object);

            MailManager mailManager = CreateMailManager(10ul, playerManager.Object, CreateMailModel(
                id: 101ul,
                recipientId: 10ul,
                senderId: 20ul));
            mailManager.ReturnMail(101ul);

            using TestCharacterContext context = CreateContext();
            var scope = new SaveCommitScope();
            mailManager.Save(context, scope);
            SaveCommitAcknowledgement acknowledgement = scope.CreateAcknowledgement();

            recipientMailManager.Verify(manager => manager.EnqueueMail(It.IsAny<IMailItem>()), Times.Never);
            acknowledgement.Acknowledge();
            acknowledgement.Acknowledge();

            recipientMailManager.Verify(manager => manager.EnqueueMail(
                It.Is<IMailItem>(mail => mail.Id == 101ul && mail.RecipientId == 20ul)), Times.Once);

            using TestCharacterContext subsequentContext = CreateContext();
            mailManager.Save(subsequentContext, new SaveCommitScope());
            Assert.Empty(subsequentContext.ChangeTracker.Entries<CharacterMailModel>());
        }

        [Fact]
        public void Save_OutgoingMailAddedDuringPendingCommitSurvivesAcknowledgement()
        {
            var recipientMailManager = new Mock<IMailManager>();
            var recipient = new Mock<IPlayer>();
            recipient.SetupGet(player => player.MailManager).Returns(recipientMailManager.Object);
            var playerManager = new Mock<IPlayerManager>();
            playerManager.Setup(manager => manager.GetPlayer(20ul)).Returns(recipient.Object);

            MailManager mailManager = CreateMailManager(
                10ul,
                playerManager.Object,
                CreateMailModel(id: 107ul, recipientId: 10ul, senderId: 20ul),
                CreateMailModel(id: 108ul, recipientId: 10ul, senderId: 20ul));
            mailManager.ReturnMail(107ul);

            using TestCharacterContext firstContext = CreateContext();
            var firstScope = new SaveCommitScope();
            mailManager.Save(firstContext, firstScope);

            mailManager.ReturnMail(108ul);
            firstScope.CreateAcknowledgement().Acknowledge();

            recipientMailManager.Verify(manager => manager.EnqueueMail(
                It.Is<IMailItem>(mail => mail.Id == 107ul)), Times.Once);
            recipientMailManager.Verify(manager => manager.EnqueueMail(
                It.Is<IMailItem>(mail => mail.Id == 108ul)), Times.Never);

            using TestCharacterContext secondContext = CreateContext();
            var secondScope = new SaveCommitScope();
            mailManager.Save(secondContext, secondScope);
            secondScope.CreateAcknowledgement().Acknowledge();

            recipientMailManager.Verify(manager => manager.EnqueueMail(
                It.Is<IMailItem>(mail => mail.Id == 108ul)), Times.Once);
        }

        [Fact]
        public void Save_DeletedAvailableMailRemainsRetryableUntilAcknowledged()
        {
            MailManager mailManager = CreateMailManager(
                10ul,
                new Mock<IPlayerManager>().Object,
                CreateMailModel(id: 109ul, recipientId: 10ul, senderId: 20ul));
            mailManager.MailDelete(109ul);

            using TestCharacterContext failedContext = CreateContext();
            var failedScope = new SaveCommitScope();
            mailManager.Save(failedContext, failedScope);
            Assert.Equal(EntityState.Deleted,
                failedContext.ChangeTracker.Entries<CharacterMailModel>().Single().State);

            using TestCharacterContext retryContext = CreateContext();
            var retryScope = new SaveCommitScope();
            mailManager.Save(retryContext, retryScope);
            Assert.Equal(EntityState.Deleted,
                retryContext.ChangeTracker.Entries<CharacterMailModel>().Single().State);

            retryScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext acknowledgedContext = CreateContext();
            mailManager.Save(acknowledgedContext, new SaveCommitScope());
            Assert.Empty(acknowledgedContext.ChangeTracker.Entries<CharacterMailModel>());
        }

        [Fact]
        public void Update_PromotesPendingMailOnceAndNotifiesClient()
        {
            MailManager mailManager = CreateMailManager(
                10ul,
                new Mock<IPlayerManager>().Object,
                out Mock<IGameSession> session);
            bool ready = false;
            var mail = new Mock<IMailItem>();
            mail.SetupGet(item => item.Id).Returns(102ul);
            mail.Setup(item => item.IsReadyToDeliver()).Returns(() => ready);
            mail.Setup(item => item.Build()).Returns(new ServerMailAvailable.Mail { MailId = 102ul });
            mailManager.EnqueueMail(mail.Object);

            mailManager.Update(0d);
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()), Times.Never);

            ready = true;
            mailManager.Update(1.1d);
            mailManager.Update(1.1d);

            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(
                It.Is<ServerMailAvailable>(message => message.MailList.Count == 1
                    && message.MailList[0].MailId == 102ul)), Times.Once);
        }

        [Fact]
        public void MailItemSave_SameMaskMutationDuringPendingCommitRemainsDirty()
        {
            var mail = new MailItem(CreateMailModel(id: 103ul, recipientId: 10ul, senderId: 20ul));
            mail.MarkAsRead();

            using TestCharacterContext firstContext = CreateContext();
            var firstScope = new SaveCommitScope();
            mail.Save(firstContext, firstScope);

            mail.MarkAsNotReturnable();
            firstScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext secondContext = CreateContext();
            mail.Save(secondContext, new SaveCommitScope());
            CharacterMailModel stagedMail = secondContext.ChangeTracker.Entries<CharacterMailModel>().Single().Entity;

            Assert.Equal((byte)(MailFlag.IsRead | MailFlag.NotReturnable), stagedMail.Flags);
        }

        [Fact]
        public void MailAttachmentSave_ClaimTombstoneDoesNotRestageInventoryItem()
        {
            var item = new Mock<IItem>();
            item.SetupGet(value => value.Guid).Returns(500ul);
            item.SetupGet(value => value.PendingCreate).Returns(false);
            item.SetupGet(value => value.PendingDelete).Returns(false);
            var attachment = new MailAttachment(104ul, 0u, item.Object);

            using TestCharacterContext createContext = CreateContext();
            var createScope = new SaveCommitScope();
            attachment.Save(createContext, createScope);
            createScope.CreateAcknowledgement().Acknowledge();
            item.Invocations.Clear();

            attachment.EnqueueDelete();
            using TestCharacterContext deleteContext = CreateContext();
            var deleteScope = new SaveCommitScope();
            attachment.Save(deleteContext, deleteScope);

            Assert.Equal(EntityState.Deleted,
                deleteContext.ChangeTracker.Entries<CharacterMailAttachmentModel>().Single().State);
            item.Verify(value => value.Save(It.IsAny<CharacterContext>(), It.IsAny<ISaveCommitScope>()), Times.Never);
        }

        [Fact]
        public void MailItemSave_DeletedAttachmentTombstoneRemainsUntilAcknowledged()
        {
            var mail = new MailItem(CreateMailModel(id: 105ul, recipientId: 10ul, senderId: 20ul));
            var item = new Mock<IItem>();
            item.SetupGet(value => value.Guid).Returns(501ul);
            item.SetupGet(value => value.PendingCreate).Returns(false);
            item.SetupGet(value => value.PendingDelete).Returns(false);
            var attachment = new MailAttachment(105ul, 7u, item.Object);

            using (TestCharacterContext createContext = CreateContext())
            {
                var createScope = new SaveCommitScope();
                attachment.Save(createContext, createScope);
                createScope.CreateAcknowledgement().Acknowledge();
            }

            mail.AttachmentAdd(attachment);
            Assert.Same(attachment, mail.GetAttachment(7u));
            mail.AttachmentDelete(attachment, 7u);
            Assert.Null(mail.GetAttachment(7u));

            using TestCharacterContext firstContext = CreateContext();
            var firstScope = new SaveCommitScope();
            mail.Save(firstContext, firstScope);
            Assert.Single(firstContext.ChangeTracker.Entries<CharacterMailAttachmentModel>());

            using TestCharacterContext retryContext = CreateContext();
            mail.Save(retryContext, new SaveCommitScope());
            Assert.Single(retryContext.ChangeTracker.Entries<CharacterMailAttachmentModel>());

            firstScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext acknowledgedContext = CreateContext();
            mail.Save(acknowledgedContext, new SaveCommitScope());
            Assert.Empty(acknowledgedContext.ChangeTracker.Entries<CharacterMailAttachmentModel>());
        }

        [Fact]
        public void MailItemSave_DeletingMailAlsoDeletesOrphanedAttachmentItem()
        {
            var mail = new MailItem(CreateMailModel(id: 106ul, recipientId: 10ul, senderId: 20ul));
            bool pendingItemDelete = false;
            var item = new Mock<IItem>();
            item.SetupGet(value => value.Guid).Returns(502ul);
            item.SetupGet(value => value.PendingCreate).Returns(false);
            item.SetupGet(value => value.PendingDelete).Returns(() => pendingItemDelete);
            item.Setup(value => value.EnqueueDelete(true)).Callback(() => pendingItemDelete = true);
            var attachment = new MailAttachment(106ul, 0u, item.Object);

            using (TestCharacterContext createContext = CreateContext())
            {
                var createScope = new SaveCommitScope();
                attachment.Save(createContext, createScope);
                createScope.CreateAcknowledgement().Acknowledge();
            }

            item.Invocations.Clear();
            mail.AttachmentAdd(attachment);
            mail.EnqueueDelete(true);

            using TestCharacterContext deleteContext = CreateContext();
            var deleteScope = new SaveCommitScope();
            mail.Save(deleteContext, deleteScope);

            item.Verify(value => value.EnqueueDelete(true), Times.Once);
            item.Verify(value => value.Save(deleteContext, deleteScope), Times.Once);
        }

        private static MailManager CreateMailManager(
            ulong characterId,
            IPlayerManager playerManager,
            params CharacterMailModel[] mail)
        {
            return CreateMailManager(characterId, playerManager, out _, mail);
        }

        private static MailManager CreateMailManager(
            ulong characterId,
            IPlayerManager playerManager,
            out Mock<IGameSession> session,
            params CharacterMailModel[] mail)
        {
            session = new Mock<IGameSession>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(characterId);
            player.SetupGet(value => value.Session).Returns(session.Object);

            var model = new CharacterModel { Id = characterId };
            foreach (CharacterMailModel mailItem in mail)
                model.Mail.Add(mailItem);

            var manager = new MailManager(player.Object, model, playerManager);
            player.SetupGet(value => value.MailManager).Returns(manager);
            return manager;
        }

        private static CharacterMailModel CreateMailModel(ulong id, ulong recipientId, ulong senderId)
        {
            return new CharacterMailModel
            {
                Id           = id,
                RecipientId  = recipientId,
                SenderId     = senderId,
                SenderType   = (byte)SenderType.Player,
                Subject      = "Subject",
                Message      = "Message",
                DeliveryTime = (byte)DeliverySpeed.Instant,
                CreateTime   = DateTime.Now
            };
        }

        private static TestCharacterContext CreateContext()
        {
            return new TestCharacterContext();
        }

        private sealed class TestCharacterContext : CharacterContext
        {
            public TestCharacterContext()
                : base(new DbContextOptionsBuilder<CharacterContext>()
                    .UseMySql(
                        "Server=localhost;Database=nexus_forever_test;User=test;Password=test;",
                        new MySqlServerVersion(new Version(8, 0, 36)))
                    .Options)
            {
            }
        }
    }
}
