using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NexusForever.Database.Character.Model;
using NexusForever.Game;
using NexusForever.Game.Abstract.Character;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Mail;
using NexusForever.Game.Character;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Mail;
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
        private const ulong TargetId = 30ul;
        private const string TargetName = "Recipient";

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
                .AddSingleton<CharacterManager>()
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;

            originalGameFormula = gameTableManager.GameFormula;
            SetGameFormula(CreateGameTable(new GameFormulaEntry
            {
                Id = 237u,
                Datafloat0 = 5f
            }, new GameFormulaEntry
            {
                Id = 861u
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
                attachments: new[] { attachment.Object });
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

        public static IEnumerable<object[]> InvalidReturnStates()
        {
            yield return new object[] { ReturnMailFailure.Missing, GenericError.MailDoesNotExist };
            yield return new object[] { ReturnMailFailure.PendingDelete, GenericError.MailDoesNotExist };
            yield return new object[] { ReturnMailFailure.MismatchedId, GenericError.MailDoesNotExist };
            yield return new object[] { ReturnMailFailure.ForeignRecipient, GenericError.MailDoesNotExist };
            yield return new object[] { ReturnMailFailure.CreatureSender, GenericError.MailCannotReturn };
            yield return new object[] { ReturnMailFailure.GmSender, GenericError.MailCannotReturn };
            yield return new object[] { ReturnMailFailure.CommodityAuctionSender, GenericError.MailCannotReturn };
            yield return new object[] { ReturnMailFailure.ItemAuctionSender, GenericError.MailCannotReturn };
            yield return new object[] { ReturnMailFailure.AccountItemSender, GenericError.MailCannotReturn };
            yield return new object[] { ReturnMailFailure.ZeroSender, GenericError.MailCannotReturn };
            yield return new object[] { ReturnMailFailure.SelfSender, GenericError.MailCannotReturn };
            yield return new object[] { ReturnMailFailure.NotReturnable, GenericError.MailCannotReturn };
        }

        [Theory]
        [MemberData(nameof(InvalidReturnStates))]
        public void ReturnMail_InvalidLiveOrSenderStateRejectsWithoutMutation(
            ReturnMailFailure failure,
            GenericError expectedResult)
        {
            Mock<IMailItem> mail = failure == ReturnMailFailure.Missing
                ? null
                : CreateMail(
                    currencyAmount: 0ul,
                    pendingDelete: failure == ReturnMailFailure.PendingDelete,
                    recipientId: failure == ReturnMailFailure.ForeignRecipient ? SenderId : PlayerId,
                    senderType: failure switch
                    {
                        ReturnMailFailure.CreatureSender => SenderType.Creature,
                        ReturnMailFailure.GmSender => SenderType.GM,
                        ReturnMailFailure.CommodityAuctionSender => SenderType.CommodityAuction,
                        ReturnMailFailure.ItemAuctionSender => SenderType.ItemAuction,
                        ReturnMailFailure.AccountItemSender => SenderType.AccountItem,
                        _ => SenderType.Player
                    },
                    senderId: failure switch
                    {
                        ReturnMailFailure.ZeroSender => 0ul,
                        ReturnMailFailure.SelfSender => PlayerId,
                        _ => SenderId
                    },
                    flags: failure == ReturnMailFailure.NotReturnable
                        ? MailFlag.NotReturnable
                        : MailFlag.None,
                    id: failure == ReturnMailFailure.MismatchedId ? MailId + 1ul : MailId);
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.ReturnMail(MailId);

            ServerMailResult result = GetSingleMessage<ServerMailResult>(fixture);
            Assert.Equal(1u, result.Action);
            Assert.Equal(MailId, result.MailId);
            Assert.Equal(expectedResult, result.Result);
            Assert.Empty(GetMessages<ServerMailUnavailable>(fixture));
            Assert.Empty(GetOutgoingMail(fixture.Manager));
            if (mail != null)
            {
                Assert.Same(mail.Object, GetAvailableMail(fixture.Manager)[MailId]);
                mail.Verify(value => value.ReturnMail(), Times.Never);
            }
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
        }

        [Theory]
        [InlineData(ReturnMailPayload.Empty)]
        [InlineData(ReturnMailPayload.Attachment)]
        [InlineData(ReturnMailPayload.Gift)]
        [InlineData(ReturnMailPayload.UnpaidCod)]
        [InlineData(ReturnMailPayload.PendingCreate)]
        public void ReturnMail_ValidPlayerMailPreservesMutationAndPublicationOrder(
            ReturnMailPayload payload)
        {
            IMailAttachment[] attachments = payload is ReturnMailPayload.Attachment or ReturnMailPayload.UnpaidCod
                ? new[] { new Mock<IMailAttachment>().Object }
                : Array.Empty<IMailAttachment>();
            Mock<IMailItem> mail = CreateMail(
                isCashOnDelivery: payload == ReturnMailPayload.UnpaidCod,
                hasPaidOrCollectedCurrency: false,
                currencyAmount: payload is ReturnMailPayload.Gift or ReturnMailPayload.UnpaidCod
                    ? CurrencyAmount
                    : 0ul,
                pendingCreate: payload == ReturnMailPayload.PendingCreate,
                attachments: attachments);
            MailFixture fixture = CreateFixture(mail);
            bool returnMutationStarted = false;
            mail.Setup(value => value.ReturnMail()).Callback(() =>
            {
                returnMutationStarted = true;
                Assert.Same(mail.Object, GetAvailableMail(fixture.Manager)[MailId]);
                Assert.Empty(GetOutgoingMail(fixture.Manager));
            });

            fixture.Manager.ReturnMail(MailId);

            Assert.True(returnMutationStarted);
            Assert.False(GetAvailableMail(fixture.Manager).ContainsKey(MailId));
            Assert.Same(mail.Object, Assert.Single(GetOutgoingMail(fixture.Manager)));
            object[] messages = fixture.Session.Invocations
                .SelectMany(invocation => invocation.Arguments)
                .ToArray();
            Assert.Collection(messages,
                value => Assert.Equal(MailId, Assert.IsType<ServerMailUnavailable>(value).MailId),
                value =>
                {
                    ServerMailResult result = Assert.IsType<ServerMailResult>(value);
                    Assert.Equal(1u, result.Action);
                    Assert.Equal(MailId, result.MailId);
                    Assert.Equal(GenericError.Ok, result.Result);
                });
            mail.Verify(value => value.ReturnMail(), Times.Once);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void ReturnMail_ReplayAfterSuccessReturnsDoesNotExistWithoutRepeatingMutation()
        {
            Mock<IMailItem> mail = CreateMail(currencyAmount: 0ul);
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.ReturnMail(MailId);
            fixture.Manager.ReturnMail(MailId);

            Assert.Equal(
                new[] { GenericError.Ok, GenericError.MailDoesNotExist },
                GetMessages<ServerMailResult>(fixture).Select(value => value.Result));
            Assert.Single(GetMessages<ServerMailUnavailable>(fixture));
            Assert.Same(mail.Object, Assert.Single(GetOutgoingMail(fixture.Manager)));
            mail.Verify(value => value.ReturnMail(), Times.Once);
        }

        [Fact]
        public void ReturnMail_AfterDeleteTombstoneReturnsDoesNotExistWithoutTransfer()
        {
            Mock<IMailItem> mail = CreateMail(currencyAmount: 0ul);
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailDelete(MailId);
            fixture.Manager.ReturnMail(MailId);

            object[] messages = fixture.Session.Invocations
                .SelectMany(invocation => invocation.Arguments)
                .ToArray();
            Assert.Collection(messages,
                value => Assert.Equal(MailId, Assert.IsType<ServerMailUnavailable>(value).MailId),
                value =>
                {
                    ServerMailResult result = Assert.IsType<ServerMailResult>(value);
                    Assert.Equal(5u, result.Action);
                    Assert.Equal(GenericError.Ok, result.Result);
                },
                value =>
                {
                    ServerMailResult result = Assert.IsType<ServerMailResult>(value);
                    Assert.Equal(1u, result.Action);
                    Assert.Equal(GenericError.MailDoesNotExist, result.Result);
                });
            Assert.Same(mail.Object, GetAvailableMail(fixture.Manager)[MailId]);
            Assert.Empty(GetOutgoingMail(fixture.Manager));
            mail.Verify(value => value.EnqueueDelete(true), Times.Once);
            mail.Verify(value => value.ReturnMail(), Times.Never);
        }

        public static IEnumerable<object[]> UnavailableDeleteStates()
        {
            yield return new object[] { true, false, PlayerId };
            yield return new object[] { false, true, PlayerId };
            yield return new object[] { false, false, SenderId };
        }

        [Theory]
        [MemberData(nameof(UnavailableDeleteStates))]
        public void MailDelete_MissingDeletedOrForeignMailReturnsDoesNotExistWithoutMutation(
            bool missing,
            bool pendingDelete,
            ulong recipientId)
        {
            Mock<IMailItem> mail = missing
                ? null
                : CreateMail(
                    currencyAmount: 0ul,
                    pendingDelete: pendingDelete,
                    recipientId: recipientId);
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailDelete(MailId);

            ServerMailResult result = GetSingleMessage<ServerMailResult>(fixture);
            Assert.Equal(5u, result.Action);
            Assert.Equal(MailId, result.MailId);
            Assert.Equal(GenericError.MailDoesNotExist, result.Result);
            Assert.Empty(GetMessages<ServerMailUnavailable>(fixture));
            mail?.Verify(value => value.EnqueueDelete(It.IsAny<bool>()), Times.Never);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
        }

        public static IEnumerable<object[]> NonDeletableMailStates()
        {
            yield return new object[] { false, true, 0ul, true };
            yield return new object[] { false, false, CurrencyAmount, false };
            yield return new object[] { true, false, CurrencyAmount, false };
        }

        [Theory]
        [MemberData(nameof(NonDeletableMailStates))]
        public void MailDelete_AttachmentOrUncollectedCurrencyReturnsCannotDeleteWithoutMutation(
            bool isCashOnDelivery,
            bool hasPaidOrCollectedCurrency,
            ulong currencyAmount,
            bool hasAttachment)
        {
            var attachment = new Mock<IMailAttachment>();
            Mock<IMailItem> mail = CreateMail(
                isCashOnDelivery: isCashOnDelivery,
                hasPaidOrCollectedCurrency: hasPaidOrCollectedCurrency,
                currencyAmount: currencyAmount,
                attachments: hasAttachment ? new[] { attachment.Object } : Array.Empty<IMailAttachment>());
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailDelete(MailId);

            ServerMailResult result = GetSingleMessage<ServerMailResult>(fixture);
            Assert.Equal(5u, result.Action);
            Assert.Equal(MailId, result.MailId);
            Assert.Equal(GenericError.MailCannotDelete, result.Result);
            Assert.Empty(GetMessages<ServerMailUnavailable>(fixture));
            mail.Verify(value => value.EnqueueDelete(It.IsAny<bool>()), Times.Never);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
        }

        public static IEnumerable<object[]> DeletableMailStates()
        {
            yield return new object[] { false, false, 0ul };
            yield return new object[] { false, true, CurrencyAmount };
            yield return new object[] { true, true, CurrencyAmount };
        }

        [Theory]
        [MemberData(nameof(DeletableMailStates))]
        public void MailDelete_EmptyMailWithoutUncollectedCurrencyPreservesTombstoneAndPublication(
            bool isCashOnDelivery,
            bool hasPaidOrCollectedCurrency,
            ulong currencyAmount)
        {
            Mock<IMailItem> mail = CreateMail(
                isCashOnDelivery: isCashOnDelivery,
                hasPaidOrCollectedCurrency: hasPaidOrCollectedCurrency,
                currencyAmount: currencyAmount);
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailDelete(MailId);

            object[] messages = fixture.Session.Invocations
                .SelectMany(invocation => invocation.Arguments)
                .ToArray();
            Assert.Collection(messages,
                value => Assert.Equal(MailId, Assert.IsType<ServerMailUnavailable>(value).MailId),
                value => Assert.Equal(GenericError.Ok, Assert.IsType<ServerMailResult>(value).Result));
            mail.Verify(value => value.EnqueueDelete(true), Times.Once);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void MailDelete_ReplayAfterSuccessfulTombstoneReturnsDoesNotExist()
        {
            Mock<IMailItem> mail = CreateMail(currencyAmount: 0ul);
            MailFixture fixture = CreateFixture(mail);

            fixture.Manager.MailDelete(MailId);
            fixture.Manager.MailDelete(MailId);

            Assert.Equal(
                new[] { GenericError.Ok, GenericError.MailDoesNotExist },
                GetMessages<ServerMailResult>(fixture).Select(value => value.Result));
            Assert.Single(GetMessages<ServerMailUnavailable>(fixture));
            mail.Verify(value => value.EnqueueDelete(true), Times.Once);
            fixture.Player.Verify(value => value.GetVisible<IWorldEntity>(It.IsAny<uint>()), Times.Never);
        }

        public static IEnumerable<object[]> InvalidSendAttachmentStates()
        {
            yield return new object[] { SendAttachmentState.Duplicate };
            yield return new object[] { SendAttachmentState.Missing };
            yield return new object[] { SendAttachmentState.GuidMismatch };
            yield return new object[] { SendAttachmentState.MissingOwner };
            yield return new object[] { SendAttachmentState.ForeignOwner };
            yield return new object[] { SendAttachmentState.PlayerBank };
            yield return new object[] { SendAttachmentState.Ability };
            yield return new object[] { SendAttachmentState.PendingDelete };
            yield return new object[] { SendAttachmentState.ZeroStack };
            yield return new object[] { SendAttachmentState.MissingInfo };
            yield return new object[] { SendAttachmentState.MissingEntry };
        }

        [Theory]
        [MemberData(nameof(InvalidSendAttachmentStates))]
        public void SendMail_InvalidAttachmentPlanRejectsBeforeCostOrMutation(SendAttachmentState state)
        {
            const ulong itemGuid = 1_001ul;

            MailFixture fixture = CreateSendFixture();
            Mock<IItem> item = state == SendAttachmentState.Missing
                ? null
                : CreateSendItem(itemGuid, state);
            fixture.Inventory
                .Setup(value => value.GetItem(itemGuid))
                .Returns(item?.Object);

            ClientMailSend request = CreateSendRequest(
                state == SendAttachmentState.Duplicate
                    ? new[] { itemGuid, itemGuid }
                    : new[] { itemGuid });

            fixture.Manager.SendMail(request);

            ServerMailResult result = GetSingleMessage<ServerMailResult>(fixture);
            Assert.Equal(1u, result.Action);
            Assert.Equal(GenericError.MailInvalidInventorySlot, result.Result);
            fixture.CurrencyManager.Verify(value => value.CanAfford(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>()), Times.Never);
            fixture.CurrencyManager.Verify(value => value.CurrencySubtractAmount(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
            fixture.Inventory.Verify(value => value.ItemRemove(
                It.IsAny<IItem>(), It.IsAny<ItemUpdateReason>()), Times.Never);
            Assert.Empty(GetOutgoingMail(fixture.Manager));
        }

        [Fact]
        public void SendMail_DistinctLiveInventoryAttachmentsPreserveOrderAndPendingCreateAdmission()
        {
            const ulong firstGuid = 1_001ul;
            const ulong secondGuid = 1_002ul;

            MailFixture fixture = CreateSendFixture();
            Mock<IItem> first = CreateSendItem(firstGuid);
            Mock<IItem> second = CreateSendItem(secondGuid, pendingCreate: true);
            fixture.Inventory
                .Setup(value => value.GetItem(It.IsAny<ulong>()))
                .Returns((ulong guid) => guid switch
                {
                    firstGuid => first.Object,
                    secondGuid => second.Object,
                    _ => null
                });

            var removed = new List<ulong>();
            fixture.Inventory
                .Setup(value => value.ItemRemove(It.IsAny<IItem>(), ItemUpdateReason.NoReason))
                .Callback<IItem, ItemUpdateReason>((item, _) => removed.Add(item.Guid));

            fixture.Manager.SendMail(CreateSendRequest(firstGuid, secondGuid));

            Assert.Equal(GenericError.Ok, GetSingleMessage<ServerMailResult>(fixture).Result);
            Assert.Equal(new[] { firstGuid, secondGuid }, removed);
            fixture.Inventory.Verify(value => value.GetItem(firstGuid), Times.Once);
            fixture.Inventory.Verify(value => value.GetItem(secondGuid), Times.Once);
            fixture.CurrencyManager.Verify(value => value.CanAfford(
                CurrencyType.Credits, 0ul), Times.Exactly(2));
            fixture.CurrencyManager.Verify(value => value.CurrencySubtractAmount(
                CurrencyType.Credits, 0ul, false), Times.Once);

            IMailItem outgoing = Assert.Single(GetOutgoingMail(fixture.Manager));
            Assert.Equal(TargetId, outgoing.RecipientId);
            Assert.Equal(PlayerId, outgoing.SenderId);
            Assert.Equal(new[] { first.Object, second.Object }, outgoing.Select(attachment => attachment.Item));
        }

        private static Mock<IMailItem> CreateMail(
            bool isCashOnDelivery = false,
            bool hasPaidOrCollectedCurrency = false,
            CurrencyType currencyType = CurrencyType.Credits,
            ulong currencyAmount = CurrencyAmount,
            bool pendingDelete = false,
            ulong recipientId = PlayerId,
            bool pendingCreate = false,
            SenderType senderType = SenderType.Player,
            ulong senderId = SenderId,
            MailFlag flags = MailFlag.None,
            ulong id = MailId,
            params IMailAttachment[] attachments)
        {
            attachments ??= Array.Empty<IMailAttachment>();

            var mail = new Mock<IMailItem>();
            mail.SetupGet(value => value.Id).Returns(id);
            mail.SetupGet(value => value.RecipientId).Returns(recipientId);
            mail.SetupGet(value => value.SenderType).Returns(senderType);
            mail.SetupGet(value => value.SenderId).Returns(senderId);
            mail.SetupGet(value => value.Subject).Returns("COD Subject");
            mail.SetupGet(value => value.CurrencyType).Returns(currencyType);
            mail.SetupGet(value => value.CurrencyAmount).Returns(currencyAmount);
            mail.SetupGet(value => value.IsCashOnDelivery).Returns(isCashOnDelivery);
            mail.SetupGet(value => value.HasPaidOrCollectedCurrency).Returns(hasPaidOrCollectedCurrency);
            mail.SetupGet(value => value.Flags).Returns(flags);
            mail.SetupGet(value => value.PendingCreate).Returns(pendingCreate);
            bool currentPendingDelete = pendingDelete;
            mail.SetupGet(value => value.PendingDelete).Returns(() => currentPendingDelete);
            mail.Setup(value => value.EnqueueDelete(It.IsAny<bool>()))
                .Callback<bool>(value => currentPendingDelete = value);
            mail.Setup(value => value.GetEnumerator()).Returns(() => attachments.AsEnumerable().GetEnumerator());
            return mail;
        }

        private static Mock<IItem> CreateSendItem(
            ulong itemGuid,
            SendAttachmentState state = SendAttachmentState.Valid,
            bool pendingCreate = false)
        {
            var info = new Mock<IItemInfo>();
            if (state != SendAttachmentState.MissingEntry)
                info.SetupGet(value => value.Entry).Returns(new Item2Entry { Id = 500u });

            var item = new Mock<IItem>();
            item.SetupGet(value => value.Guid).Returns(
                state == SendAttachmentState.GuidMismatch ? itemGuid + 1ul : itemGuid);
            item.SetupGet(value => value.CharacterId).Returns(state switch
            {
                SendAttachmentState.MissingOwner => null,
                SendAttachmentState.ForeignOwner => SenderId,
                _ => PlayerId
            });
            item.SetupGet(value => value.Location).Returns(state switch
            {
                SendAttachmentState.PlayerBank => InventoryLocation.PlayerBank,
                SendAttachmentState.Ability => InventoryLocation.Ability,
                _ => InventoryLocation.Inventory
            });
            item.SetupGet(value => value.PendingCreate).Returns(pendingCreate);
            item.SetupGet(value => value.PendingDelete).Returns(state == SendAttachmentState.PendingDelete);
            item.SetupGet(value => value.StackCount).Returns(state == SendAttachmentState.ZeroStack ? 0u : 1u);
            item.SetupGet(value => value.Info).Returns(
                state == SendAttachmentState.MissingInfo ? null : info.Object);
            return item;
        }

        private MailFixture CreateSendFixture()
        {
            var target = new Mock<ICharacter>();
            target.SetupGet(value => value.CharacterId).Returns(TargetId);
            target.SetupGet(value => value.Name).Returns(TargetName);
            MethodInfo addCharacter = typeof(CharacterManager).GetMethod(
                "AddCharacter",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(ulong), typeof(ICharacter) },
                null);
            addCharacter.Invoke(CharacterManager.Instance, new object[] { TargetId, target.Object });

            var session = new Mock<IGameSession>();
            var currencyManager = new Mock<ICurrencyManager>();
            currencyManager
                .Setup(value => value.CanAfford(CurrencyType.Credits, It.IsAny<ulong>()))
                .Returns(true);
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

            var manager = new MailManager(
                player.Object,
                new CharacterModel { Id = PlayerId },
                new Mock<IPlayerManager>().Object);
            return new MailFixture(manager, player, session, currencyManager, inventory);
        }

        private static ClientMailSend CreateSendRequest(params ulong[] itemGuids)
        {
            var request = new ClientMailSend();
            SetProperty(request, nameof(ClientMailSend.Name), TargetName);
            SetProperty(request, nameof(ClientMailSend.Subject), "Subject");
            SetProperty(request, nameof(ClientMailSend.Message), "Message");
            SetProperty(request, nameof(ClientMailSend.DeliverySpeed), DeliverySpeed.Instant);
            SetProperty(request, nameof(ClientMailSend.MailboxUnitId), MailboxUnitId);
            request.Items.AddRange(itemGuids);
            return request;
        }

        private static void SetProperty<T>(ClientMailSend request, string propertyName, T value)
        {
            typeof(ClientMailSend).GetProperty(propertyName)?.SetValue(request, value);
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
            if (mail != null)
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
            return Assert.Single(GetMessages<T>(fixture));
        }

        private static IEnumerable<T> GetMessages<T>(MailFixture fixture) where T : class
        {
            return fixture.Session.Invocations
                .SelectMany(invocation => invocation.Arguments)
                .OfType<T>();
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

        public enum SendAttachmentState
        {
            Valid,
            Duplicate,
            Missing,
            GuidMismatch,
            MissingOwner,
            ForeignOwner,
            PlayerBank,
            Ability,
            PendingDelete,
            ZeroStack,
            MissingInfo,
            MissingEntry
        }

        public enum ReturnMailFailure
        {
            Missing,
            PendingDelete,
            MismatchedId,
            ForeignRecipient,
            CreatureSender,
            GmSender,
            CommodityAuctionSender,
            ItemAuctionSender,
            AccountItemSender,
            ZeroSender,
            SelfSender,
            NotReturnable
        }

        public enum ReturnMailPayload
        {
            Empty,
            Attachment,
            Gift,
            UnpaidCod,
            PendingCreate
        }
    }
}
