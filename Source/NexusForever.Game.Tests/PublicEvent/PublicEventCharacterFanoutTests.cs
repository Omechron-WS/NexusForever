using Microsoft.Extensions.Logging;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.PublicEvent;
using NexusForever.Game.Static.PublicEvent;

namespace NexusForever.Game.Tests.PublicEvent
{
    public sealed class PublicEventCharacterFanoutTests
    {
        private const ulong CharacterId = 42ul;

        [Fact]
        public void MapRemoval_SelfRemovingEventsAllRunInRegistrationOrder()
        {
            (PublicEventCharacter character, Mock<IPlayer> player) = CreateCharacter();
            var operations = new List<uint>();
            Mock<IPublicEvent> firstEvent = CreateEvent(1u);
            Mock<IPublicEvent> secondEvent = CreateEvent(2u);
            firstEvent
                .Setup(@event => @event.LeaveEvent(player.Object, PublicEventRemoveReason.LeftArea))
                .Callback(() =>
                {
                    operations.Add(1u);
                    character.RemoveEvent(firstEvent.Object);
                });
            secondEvent
                .Setup(@event => @event.LeaveEvent(player.Object, PublicEventRemoveReason.LeftArea))
                .Callback(() =>
                {
                    operations.Add(2u);
                    character.RemoveEvent(secondEvent.Object);
                });
            character.AddEvent(firstEvent.Object);
            character.AddEvent(secondEvent.Object);

            Exception exception = Record.Exception(() => character.OnRemoveFromMap(player.Object));

            Assert.Null(exception);
            Assert.Equal([1u, 2u], operations);
            firstEvent.Verify(
                @event => @event.LeaveEvent(player.Object, PublicEventRemoveReason.LeftArea),
                Times.Once);
            secondEvent.Verify(
                @event => @event.LeaveEvent(player.Object, PublicEventRemoveReason.LeftArea),
                Times.Once);
        }

        [Fact]
        public void ObjectiveFailure_DoesNotSuppressLaterEventAndRetriesNextFanout()
        {
            (PublicEventCharacter character, Mock<IPlayer> player) = CreateCharacter();
            var operations = new List<uint>();
            int failedAttempts = 0;
            Mock<IPublicEvent> failedEvent = CreateEvent(1u);
            failedEvent
                .Setup(@event => @event.UpdateObjective(
                    player.Object,
                    PublicEventObjectiveType.Script,
                    55u,
                    1))
                .Callback(() =>
                {
                    operations.Add(1u);
                    if (failedAttempts++ == 0)
                        throw new InvalidOperationException("Test objective observer failure.");
                });
            Mock<IPublicEvent> healthyEvent = CreateEvent(2u);
            healthyEvent
                .Setup(@event => @event.UpdateObjective(
                    player.Object,
                    PublicEventObjectiveType.Script,
                    55u,
                    1))
                .Callback(() => operations.Add(2u));
            character.AddEvent(failedEvent.Object);
            character.AddEvent(healthyEvent.Object);

            Exception firstException = Record.Exception(() => character.UpdateObjective(
                player.Object,
                PublicEventObjectiveType.Script,
                55u,
                1));
            Exception secondException = Record.Exception(() => character.UpdateObjective(
                player.Object,
                PublicEventObjectiveType.Script,
                55u,
                1));

            Assert.Null(firstException);
            Assert.Null(secondException);
            Assert.Equal([1u, 2u, 1u, 2u], operations);
            failedEvent.Verify(
                @event => @event.UpdateObjective(
                    player.Object,
                    PublicEventObjectiveType.Script,
                    55u,
                    1),
                Times.Exactly(2));
            healthyEvent.Verify(
                @event => @event.UpdateObjective(
                    player.Object,
                    PublicEventObjectiveType.Script,
                    55u,
                    1),
                Times.Exactly(2));
        }

        [Fact]
        public void ReplacedSnapshotEventAndDistinctAddition_WaitForNextFanout()
        {
            (PublicEventCharacter character, Mock<IPlayer> player) = CreateCharacter();
            var operations = new List<string>();
            Mock<IPublicEvent> firstEvent = CreateEvent(1u);
            Mock<IPublicEvent> replacedEvent = CreateEvent(2u);
            Mock<IPublicEvent> laterEvent = CreateEvent(3u);
            Mock<IPublicEvent> replacementEvent = CreateEvent(2u);
            Mock<IPublicEvent> addedEvent = CreateEvent(4u);
            bool membershipChanged = false;
            firstEvent
                .Setup(@event => @event.UpdateStat(player.Object, PublicEventStat.Damage, 10u))
                .Callback(() =>
                {
                    operations.Add("first");
                    if (membershipChanged)
                        return;

                    membershipChanged = true;
                    character.RemoveEvent(replacedEvent.Object);
                    character.AddEvent(replacementEvent.Object);
                    character.AddEvent(addedEvent.Object);
                });
            replacedEvent
                .Setup(@event => @event.UpdateStat(player.Object, PublicEventStat.Damage, 10u))
                .Callback(() => operations.Add("replaced"));
            laterEvent
                .Setup(@event => @event.UpdateStat(player.Object, PublicEventStat.Damage, 10u))
                .Callback(() => operations.Add("later"));
            replacementEvent
                .Setup(@event => @event.UpdateStat(player.Object, PublicEventStat.Damage, 10u))
                .Callback(() => operations.Add("replacement"));
            addedEvent
                .Setup(@event => @event.UpdateStat(player.Object, PublicEventStat.Damage, 10u))
                .Callback(() => operations.Add("added"));
            character.AddEvent(firstEvent.Object);
            character.AddEvent(laterEvent.Object);
            character.AddEvent(replacedEvent.Object);

            character.UpdateStat(player.Object, PublicEventStat.Damage, 10u);

            Assert.Equal(["first", "later"], operations);
            replacedEvent.Verify(
                @event => @event.UpdateStat(It.IsAny<IPlayer>(), It.IsAny<PublicEventStat>(), It.IsAny<uint>()),
                Times.Never);
            replacementEvent.Verify(
                @event => @event.UpdateStat(It.IsAny<IPlayer>(), It.IsAny<PublicEventStat>(), It.IsAny<uint>()),
                Times.Never);
            addedEvent.Verify(
                @event => @event.UpdateStat(It.IsAny<IPlayer>(), It.IsAny<PublicEventStat>(), It.IsAny<uint>()),
                Times.Never);

            Assert.Throws<InvalidOperationException>(() => character.RemoveEvent(replacedEvent.Object));
            operations.Clear();

            character.UpdateStat(player.Object, PublicEventStat.Damage, 10u);

            Assert.Equal(["first", "later", "replacement", "added"], operations);
            replacementEvent.Verify(
                @event => @event.UpdateStat(player.Object, PublicEventStat.Damage, 10u),
                Times.Once);
            addedEvent.Verify(
                @event => @event.UpdateStat(player.Object, PublicEventStat.Damage, 10u),
                Times.Once);
        }

        private static (PublicEventCharacter Character, Mock<IPlayer> Player) CreateCharacter()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(CharacterId);

            var character = new PublicEventCharacter(Mock.Of<ILogger<PublicEventCharacter>>());
            character.Initialise(player.Object);
            return (character, player);
        }

        private static Mock<IPublicEvent> CreateEvent(uint id)
        {
            var @event = new Mock<IPublicEvent>();
            @event.SetupGet(value => value.Id).Returns(id);
            @event.SetupGet(value => value.Guid).Returns(Guid.NewGuid());
            return @event;
        }
    }
}
