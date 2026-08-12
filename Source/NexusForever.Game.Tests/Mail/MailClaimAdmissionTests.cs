using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NexusForever.Database.Character.Model;
using NexusForever.Game;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Mail;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Configuration.Model;
using NexusForever.GameTable.Model;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model.Mail;
using NexusForever.Network.World.Message.Static;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.Mail
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class MailClaimAdmissionCollection
    {
        public const string Name = "Mail claim admission";
    }

    [Collection(MailClaimAdmissionCollection.Name)]
    public sealed class MailClaimAdmissionTests : IDisposable
    {
        private const ulong PlayerId = 10ul;
        private const ulong SenderId = 20ul;
        private const ulong MailId = 100ul;
        private const ulong CurrencyAmount = 125ul;
        private const uint AttachmentIndex = 3u;
        private const uint MailboxUnitId = 77u;

        private readonly IServiceProvider originalServiceProvider;
        private readonly ServiceProvider serviceProvider;
        private readonly GameTableManager gameTableManager = new(Options.Create(new GameTableConfig()));
        private readonly GameTable<GameFormulaEntry> originalGameFormula;

        public MailClaimAdmissionTests()
        {
            originalServiceProvider = LegacyServiceProvider.Provider;
            serviceProvider = new ServiceCollection()
                .AddSingleton(gameTableManager)
                .AddSingleton<AssetManager>()
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;

            originalGameFormula = gameTableManager.GameFormula;
            SetGameFormula(CreateGameTable(new GameFormulaEntry
            {
                Id = 237u,
                Datafloat0 = 5f
            }));
        }

        public void Dispose()
        {
            SetGameFormula(originalGameFormula);
            LegacyServiceProvider.Provider = originalServiceProvider;
            serviceProvider.Dispose();
        }

        public static IEnumerable<object[]> InvalidCashStates()
        {
            yield return new object[] { true, false, CurrencyType.Credits, CurrencyAmount, false, PlayerId, GenericError.MailNoAttachment };
            yield return new object[] { false, true, CurrencyType.Credits, CurrencyAmount, false, PlayerId, GenericError.MailNoAttachment };
            yield return new object[] { false, false, CurrencyType.Renown, CurrencyAmount, false, PlayerId, GenericError.MailNoAttachment };
            yield return new object[] { false, false, CurrencyType.Credits, 0ul, false, PlayerId, GenericError.MailNoAttachment };
            yield return new object[] { false, false, CurrencyType.Credits, CurrencyAmount, true, PlayerId, GenericError.MailDoesNotExist };
            yield return new object[] { false, false, CurrencyType.Credits, CurrencyAmount, false, SenderId, GenericError.MailDoesNotExist };
        }

        [Theory]
        [MemberData(nameof(InvalidCashStates))]
        public void MailTakeCash_InvalidServerStateRejectsBeforeMailboxAndCurrencyMutation(
            bool isCashOnDelivery,
            bool hasPaidOrCollectedCurrency,
            CurrencyType currencyType,
            ulong currencyAmount,
            bool pendingDelete,
            ulong recipientId,
            GenericError expectedResult)
        {
            Mock<IMailItem> mail = CreateMail(
                isCashOnDelivery,
                hasPaidOrCollectedCurrency,
                currencyType,
                currencyAmount,
                pendingDelete,
                recipientId);
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailTakeCash(MailId, MailboxUnitId);

            ServerMailResult result = GetSingleMessage<ServerMailResult>(fixture);
            Assert.Equal(2u, result.Action);
            Assert.Equal(MailId, result.MailId);
            Assert.Equal(expectedResult, result.Result);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
            fixture.CurrencyManager.Verify(value => value.CurrencyAddAmount(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
            mail.Verify(value => value.PayOrTakeCash(), Times.Never);
        }

        [Fact]
        public void MailTakeCash_PositiveUncollectedCreditsGiftPreservesExistingFlow()
        {
            Mock<IMailItem> mail = CreateMail();
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailTakeCash(MailId, MailboxUnitId);

            Assert.Equal(GenericError.Ok, GetSingleMessage<ServerMailResult>(fixture).Result);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(MailboxUnitId), Times.Once);
            fixture.CurrencyManager.Verify(value => value.CurrencyAddAmount(
                CurrencyType.Credits, CurrencyAmount, false), Times.Once);
            mail.Verify(value => value.PayOrTakeCash(), Times.Once);
        }

        public static IEnumerable<object[]> UnavailableAttachmentStates()
        {
            yield return new object[] { false, false, true, PlayerId, GenericError.MailDoesNotExist };
            yield return new object[] { false, false, false, SenderId, GenericError.MailDoesNotExist };
            yield return new object[] { true, false, false, PlayerId, GenericError.MailBusy };
        }

        [Theory]
        [MemberData(nameof(UnavailableAttachmentStates))]
        public void MailTakeAttachment_UnavailableStateRejectsBeforeAttachmentAndInventoryLookup(
            bool isCashOnDelivery,
            bool hasPaidOrCollectedCurrency,
            bool pendingDelete,
            ulong recipientId,
            GenericError expectedResult)
        {
            var attachment = new Mock<IMailAttachment>();
            Mock<IMailItem> mail = CreateMail(
                isCashOnDelivery: isCashOnDelivery,
                hasPaidOrCollectedCurrency: hasPaidOrCollectedCurrency,
                pendingDelete: pendingDelete,
                recipientId: recipientId,
                attachments: new[] { attachment.Object });
            mail.Setup(value => value.GetAttachment(AttachmentIndex)).Returns(attachment.Object);
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailTakeAttachment(MailId, AttachmentIndex, MailboxUnitId);

            ServerMailTakeAttachment result = GetSingleMessage<ServerMailTakeAttachment>(fixture);
            Assert.Equal(MailId, result.MailId);
            Assert.Equal(AttachmentIndex, result.Index);
            Assert.Equal(expectedResult, result.Result);
            mail.Verify(value => value.GetAttachment(It.IsAny<uint>()), Times.Never);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
            fixture.Inventory.Verify(value => value.IsInventoryFull(It.IsAny<InventoryLocation>()), Times.Never);
            fixture.Inventory.Verify(value => value.AddItem(
                It.IsAny<IItem>(), It.IsAny<InventoryLocation>(), It.IsAny<ItemUpdateReason>()), Times.Never);
            mail.Verify(value => value.MarkAsNotReturnable(), Times.Never);
            mail.Verify(value => value.AttachmentDelete(It.IsAny<IMailAttachment>(), It.IsAny<uint>()), Times.Never);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void MailTakeAttachment_OrdinaryOrPaidCodPreservesExistingFlow(
            bool isCashOnDelivery,
            bool hasPaidOrCollectedCurrency)
        {
            var item = new Mock<IItem>();
            var attachment = new Mock<IMailAttachment>();
            attachment.SetupGet(value => value.Item).Returns(item.Object);
            Mock<IMailItem> mail = CreateMail(
                isCashOnDelivery: isCashOnDelivery,
                hasPaidOrCollectedCurrency: hasPaidOrCollectedCurrency,
                attachments: new[] { attachment.Object });
            mail.Setup(value => value.GetAttachment(AttachmentIndex)).Returns(attachment.Object);
            MailFixture fixture = CreateFixture(mail);
            fixture.Inventory.Setup(value => value.IsInventoryFull(InventoryLocation.Inventory)).Returns(false);

            fixture.Manager.MailTakeAttachment(MailId, AttachmentIndex, MailboxUnitId);

            Assert.Equal(GenericError.Ok, GetSingleMessage<ServerMailTakeAttachment>(fixture).Result);
            item.VerifySet(value => value.CharacterId = PlayerId, Times.Once);
            fixture.Inventory.Verify(value => value.AddItem(
                item.Object, InventoryLocation.Inventory, ItemUpdateReason.NoReason), Times.Once);
            mail.Verify(value => value.MarkAsNotReturnable(), Times.Once);
            mail.Verify(value => value.AttachmentDelete(attachment.Object, AttachmentIndex), Times.Once);
        }

        public static IEnumerable<object[]> InvalidCodStates()
        {
            yield return new object[] { false, false, CurrencyType.Credits, CurrencyAmount, false, PlayerId, GenericError.MailBusy };
            yield return new object[] { true, true, CurrencyType.Credits, CurrencyAmount, false, PlayerId, GenericError.MailBusy };
            yield return new object[] { true, false, CurrencyType.Renown, CurrencyAmount, false, PlayerId, GenericError.MailBusy };
            yield return new object[] { true, false, CurrencyType.Credits, 0ul, false, PlayerId, GenericError.MailBusy };
            yield return new object[] { true, false, CurrencyType.Credits, CurrencyAmount, true, PlayerId, GenericError.MailDoesNotExist };
            yield return new object[] { true, false, CurrencyType.Credits, CurrencyAmount, false, SenderId, GenericError.MailDoesNotExist };
        }

        [Theory]
        [MemberData(nameof(InvalidCodStates))]
        public void MailPayCod_InvalidServerStateRejectsBeforeMailboxAndCurrencyMutation(
            bool isCashOnDelivery,
            bool hasPaidOrCollectedCurrency,
            CurrencyType currencyType,
            ulong currencyAmount,
            bool pendingDelete,
            ulong recipientId,
            GenericError expectedResult)
        {
            var attachment = new Mock<IMailAttachment>();
            Mock<IMailItem> mail = CreateMail(
                isCashOnDelivery,
                hasPaidOrCollectedCurrency,
                currencyType,
                currencyAmount,
                pendingDelete,
                recipientId,
                attachment.Object);
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailPayCod(MailId, MailboxUnitId);

            ServerMailResult result = GetSingleMessage<ServerMailResult>(fixture);
            Assert.Equal(3u, result.Action);
            Assert.Equal(MailId, result.MailId);
            Assert.Equal(expectedResult, result.Result);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
            fixture.CurrencyManager.Verify(value => value.CanAfford(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>()), Times.Never);
            fixture.CurrencyManager.Verify(value => value.CurrencySubtractAmount(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
            mail.Verify(value => value.PayOrTakeCash(), Times.Never);
        }

        [Fact]
        public void MailPayCod_MissingAttachmentRejectsBeforeMailboxAndCurrencyMutation()
        {
            Mock<IMailItem> mail = CreateMail(isCashOnDelivery: true);
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailPayCod(MailId, MailboxUnitId);

            Assert.Equal(GenericError.MailNoAttachment, GetSingleMessage<ServerMailResult>(fixture).Result);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
            fixture.CurrencyManager.Verify(value => value.CanAfford(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>()), Times.Never);
            fixture.CurrencyManager.Verify(value => value.CurrencySubtractAmount(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
            mail.Verify(value => value.PayOrTakeCash(), Times.Never);
        }

        [Fact]
        public void MailPayCod_ZeroMailboxRejectsBeforeAffordabilityAndCurrencyMutation()
        {
            var attachment = new Mock<IMailAttachment>();
            Mock<IMailItem> mail = CreateMail(isCashOnDelivery: true, attachments: new[] { attachment.Object });
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailPayCod(MailId, 0u);

            Assert.Equal(GenericError.MailMailBoxOutOfRange, GetSingleMessage<ServerMailResult>(fixture).Result);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
            fixture.CurrencyManager.Verify(value => value.CanAfford(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>()), Times.Never);
            fixture.CurrencyManager.Verify(value => value.CurrencySubtractAmount(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
            mail.Verify(value => value.PayOrTakeCash(), Times.Never);
        }

        [Fact]
        public void MailPayCod_InsufficientFundsRejectsBeforeCurrencyAndMailMutation()
        {
            var attachment = new Mock<IMailAttachment>();
            Mock<IMailItem> mail = CreateMail(isCashOnDelivery: true, attachments: new[] { attachment.Object });
            MailFixture fixture = CreateFixture(mail);
            fixture.CurrencyManager.Setup(value => value.CanAfford(CurrencyType.Credits, CurrencyAmount)).Returns(false);

            fixture.Manager.MailPayCod(MailId, MailboxUnitId);

            Assert.Equal(GenericError.MailInsufficientFunds, GetSingleMessage<ServerMailResult>(fixture).Result);
            fixture.CurrencyManager.Verify(value => value.CanAfford(
                CurrencyType.Credits, CurrencyAmount), Times.Once);
            fixture.CurrencyManager.Verify(value => value.CurrencySubtractAmount(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
            mail.Verify(value => value.PayOrTakeCash(), Times.Never);
        }

        [Fact]
        public void MailPayCod_ValidServerStatePreservesPaymentAndSenderMailFlow()
        {
            var attachment = new Mock<IMailAttachment>();
            Mock<IMailItem> mail = CreateMail(isCashOnDelivery: true, attachments: new[] { attachment.Object });
            MailFixture fixture = CreateFixture(mail);
            fixture.CurrencyManager.Setup(value => value.CanAfford(CurrencyType.Credits, CurrencyAmount)).Returns(true);

            fixture.Manager.MailPayCod(MailId, MailboxUnitId);

            Assert.Equal(GenericError.Ok, GetSingleMessage<ServerMailResult>(fixture).Result);
            fixture.CurrencyManager.Verify(value => value.CurrencySubtractAmount(
                CurrencyType.Credits, CurrencyAmount, false), Times.Once);
            mail.Verify(value => value.PayOrTakeCash(), Times.Once);

            IMailItem senderMail = Assert.Single(GetOutgoingMail(fixture.Manager));
            Assert.Equal(SenderId, senderMail.RecipientId);
            Assert.Equal(PlayerId, senderMail.SenderId);
            Assert.Equal(CurrencyType.Credits, senderMail.CurrencyType);
            Assert.Equal(CurrencyAmount, senderMail.CurrencyAmount);
            Assert.False(senderMail.IsCashOnDelivery);
        }

        private static Mock<IMailItem> CreateMail(
            bool isCashOnDelivery = false,
            bool hasPaidOrCollectedCurrency = false,
            CurrencyType currencyType = CurrencyType.Credits,
            ulong currencyAmount = CurrencyAmount,
            bool pendingDelete = false,
            ulong recipientId = PlayerId,
            params IMailAttachment[] attachments)
        {
            attachments ??= Array.Empty<IMailAttachment>();

            var mail = new Mock<IMailItem>();
            mail.SetupGet(value => value.Id).Returns(MailId);
            mail.SetupGet(value => value.RecipientId).Returns(recipientId);
            mail.SetupGet(value => value.SenderId).Returns(SenderId);
            mail.SetupGet(value => value.Subject).Returns("COD Subject");
            mail.SetupGet(value => value.CurrencyType).Returns(currencyType);
            mail.SetupGet(value => value.CurrencyAmount).Returns(currencyAmount);
            mail.SetupGet(value => value.IsCashOnDelivery).Returns(isCashOnDelivery);
            mail.SetupGet(value => value.HasPaidOrCollectedCurrency).Returns(hasPaidOrCollectedCurrency);
            mail.SetupGet(value => value.PendingDelete).Returns(pendingDelete);
            mail.Setup(value => value.GetEnumerator()).Returns(() => attachments.AsEnumerable().GetEnumerator());
            return mail;
        }

        private static MailFixture CreateFixture(Mock<IMailItem> mail)
        {
            var session = new Mock<IGameSession>();
            var currencyManager = new Mock<ICurrencyManager>();
            var inventory = new Mock<IInventory>();
            var mailbox = new Mock<IMailboxEntity>();
            mailbox.SetupGet(value => value.Position).Returns(Vector3.One);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(PlayerId);
            player.SetupGet(value => value.Position).Returns(Vector3.Zero);
            player.SetupGet(value => value.Session).Returns(session.Object);
            player.SetupGet(value => value.CurrencyManager).Returns(currencyManager.Object);
            player.SetupGet(value => value.Inventory).Returns(inventory.Object);
            player.Setup(value => value.GetVisible<IWorldEntity>(MailboxUnitId)).Returns(mailbox.Object);

            var model = new CharacterModel { Id = PlayerId };
            var manager = new MailManager(player.Object, model, new Mock<IPlayerManager>().Object);
            GetAvailableMail(manager).Add(MailId, mail.Object);

            return new MailFixture(manager, player, session, currencyManager, inventory);
        }

        private static Dictionary<ulong, IMailItem> GetAvailableMail(MailManager manager)
        {
            FieldInfo field = typeof(MailManager).GetField(
                "availableMail",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsType<Dictionary<ulong, IMailItem>>(field.GetValue(manager));
        }

        private static Queue<IMailItem> GetOutgoingMail(MailManager manager)
        {
            FieldInfo field = typeof(MailManager).GetField(
                "outgoingMail",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsType<Queue<IMailItem>>(field.GetValue(manager));
        }

        private static T GetSingleMessage<T>(MailFixture fixture) where T : class
        {
            return Assert.Single(fixture.Session.Invocations
                .SelectMany(invocation => invocation.Arguments)
                .OfType<T>());
        }

        private void SetGameFormula(GameTable<GameFormulaEntry> table)
        {
            typeof(GameTableManager)
                .GetProperty(nameof(GameTableManager.GameFormula))
                ?.SetValue(gameTableManager, table);
        }

        private static GameTable<T> CreateGameTable<T>(params T[] entries) where T : class, new()
        {
            var table = (GameTable<T>)RuntimeHelpers.GetUninitializedObject(typeof(GameTable<T>));
            typeof(GameTable<T>).GetProperty(nameof(GameTable<T>.Entries))?.SetValue(table, entries);

            FieldInfo idField = typeof(T).GetFields().First();
            uint maximumId = entries.Select(entry => (uint)idField.GetValue(entry)).DefaultIfEmpty().Max();
            int[] lookup = Enumerable.Repeat(-1, checked((int)maximumId + 1)).ToArray();
            for (int index = 0; index < entries.Length; index++)
                lookup[(uint)idField.GetValue(entries[index])] = index;

            typeof(GameTable<T>)
                .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, lookup);
            typeof(GameTable<T>)
                .GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, new GameTableHeader { MaxId = maximumId + 1ul });
            return table;
        }

        private sealed record MailFixture(
            MailManager Manager,
            Mock<IPlayer> Player,
            Mock<IGameSession> Session,
            Mock<ICurrencyManager> CurrencyManager,
            Mock<IInventory> Inventory);
    }
}
