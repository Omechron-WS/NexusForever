using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.PublicEvent;
using NexusForever.GameTable.Model;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.PublicEvent
{
    public sealed class PublicEventManagerUpdateTests
    {
        private const double FirstTick = 0.25d;
        private const double SecondTick = 0.5d;

        [Fact]
        public void Update_NonFinalFailureDoesNotBlockSiblingAndRetriesNextTick()
        {
            PublicEventManager manager = CreateManager();
            var operations = new List<string>();
            int failedAttempts = 0;
            Mock<IPublicEvent> failedEvent = CreateEvent(1u);
            failedEvent
                .Setup(@event => @event.Update(It.IsAny<double>()))
                .Callback<double>(_ =>
                {
                    operations.Add("failed");
                    if (failedAttempts++ == 0)
                        throw new InvalidOperationException("Test public event update failure.");
                });
            Mock<IPublicEvent> healthyEvent = CreateEvent(2u);
            healthyEvent
                .Setup(@event => @event.Update(It.IsAny<double>()))
                .Callback<double>(_ => operations.Add("healthy"));
            RegisterEvents(manager, failedEvent.Object, healthyEvent.Object);

            Exception firstException = Record.Exception(() => manager.Update(FirstTick));
            Exception secondException = Record.Exception(() => manager.Update(SecondTick));

            Assert.Null(firstException);
            Assert.Null(secondException);
            Assert.Equal(["failed", "healthy", "failed", "healthy"], operations);
            failedEvent.Verify(@event => @event.Update(FirstTick), Times.Once);
            failedEvent.Verify(@event => @event.Update(SecondTick), Times.Once);
            healthyEvent.Verify(@event => @event.Update(FirstTick), Times.Once);
            healthyEvent.Verify(@event => @event.Update(SecondTick), Times.Once);
            failedEvent.Verify(@event => @event.Dispose(), Times.Never);
            healthyEvent.Verify(@event => @event.Dispose(), Times.Never);
            Assert.Same(failedEvent.Object, manager.GetEvent(1u));
            Assert.Same(healthyEvent.Object, manager.GetEvent(2u));
        }

        [Fact]
        public void Update_CommittedFinalisationAndDisposeFailureDoNotBlockLaterCleanupOrRetry()
        {
            PublicEventManager manager = CreateManager();
            var operations = new List<string>();
            bool failedFinalised = false;
            bool healthyFinalised = false;
            Mock<IPublicEvent> failedEvent = CreateEvent(1u);
            failedEvent.SetupGet(@event => @event.IsFinalised).Returns(() => failedFinalised);
            failedEvent
                .Setup(@event => @event.Update(FirstTick))
                .Callback(() =>
                {
                    operations.Add("failed-update");
                    failedFinalised = true;
                    throw new InvalidOperationException("Test finalising update failure.");
                });
            failedEvent
                .Setup(@event => @event.Dispose())
                .Callback(() =>
                {
                    operations.Add("failed-dispose");
                    throw new InvalidOperationException("Test public event disposal failure.");
                });
            Mock<IPublicEvent> healthyEvent = CreateEvent(2u);
            healthyEvent.SetupGet(@event => @event.IsFinalised).Returns(() => healthyFinalised);
            healthyEvent
                .Setup(@event => @event.Update(FirstTick))
                .Callback(() =>
                {
                    operations.Add("healthy-update");
                    healthyFinalised = true;
                });
            healthyEvent
                .Setup(@event => @event.Dispose())
                .Callback(() => operations.Add("healthy-dispose"));
            RegisterEvents(manager, failedEvent.Object, healthyEvent.Object);

            Exception firstException = Record.Exception(() => manager.Update(FirstTick));

            Assert.Null(firstException);
            Assert.Equal(
                ["failed-update", "healthy-update", "failed-dispose", "healthy-dispose"],
                operations);
            Assert.Null(manager.GetEvent(1u));
            Assert.Null(manager.GetEvent(2u));

            Exception secondException = Record.Exception(() => manager.Update(SecondTick));

            Assert.Null(secondException);
            Assert.Equal(
                ["failed-update", "healthy-update", "failed-dispose", "healthy-dispose"],
                operations);
            failedEvent.Verify(@event => @event.Update(It.IsAny<double>()), Times.Once);
            healthyEvent.Verify(@event => @event.Update(It.IsAny<double>()), Times.Once);
            failedEvent.Verify(@event => @event.Dispose(), Times.Once);
            healthyEvent.Verify(@event => @event.Dispose(), Times.Once);
        }

        [Fact]
        public void Update_RemovedAndReplacedSnapshotEventIsSkippedAndAdditionWaitsForNextTick()
        {
            PublicEventManager manager = CreateManager();
            Dictionary<uint, IPublicEvent> events = GetEventStore(manager);
            var operations = new List<string>();
            bool registrationsChanged = false;
            Mock<IPublicEvent> firstEvent = CreateEvent(1u);
            Mock<IPublicEvent> replacedEvent = CreateEvent(2u);
            Mock<IPublicEvent> laterEvent = CreateEvent(3u);
            Mock<IPublicEvent> replacementEvent = CreateEvent(2u);
            Mock<IPublicEvent> addedEvent = CreateEvent(4u);
            firstEvent
                .Setup(@event => @event.Update(It.IsAny<double>()))
                .Callback<double>(_ =>
                {
                    operations.Add("first");
                    if (registrationsChanged)
                        return;

                    registrationsChanged = true;
                    events.Remove(2u);
                    events.Add(2u, replacementEvent.Object);
                    events.Add(4u, addedEvent.Object);
                });
            replacedEvent
                .Setup(@event => @event.Update(It.IsAny<double>()))
                .Callback<double>(_ => operations.Add("replaced"));
            laterEvent
                .Setup(@event => @event.Update(It.IsAny<double>()))
                .Callback<double>(_ => operations.Add("later"));
            replacementEvent
                .Setup(@event => @event.Update(It.IsAny<double>()))
                .Callback<double>(_ => operations.Add("replacement"));
            addedEvent
                .Setup(@event => @event.Update(It.IsAny<double>()))
                .Callback<double>(_ => operations.Add("added"));
            RegisterEvents(manager, firstEvent.Object, laterEvent.Object, replacedEvent.Object);

            manager.Update(FirstTick);

            Assert.Equal(["first", "later"], operations);
            replacedEvent.Verify(@event => @event.Update(It.IsAny<double>()), Times.Never);
            replacementEvent.Verify(@event => @event.Update(It.IsAny<double>()), Times.Never);
            addedEvent.Verify(@event => @event.Update(It.IsAny<double>()), Times.Never);

            manager.Update(SecondTick);

            Assert.Equal(["first", "later", "first", "later", "replacement", "added"], operations);
            replacementEvent.Verify(@event => @event.Update(SecondTick), Times.Once);
            addedEvent.Verify(@event => @event.Update(SecondTick), Times.Once);
        }

        [Fact]
        public void Update_FinalisedSnapshotEventReplacedBeforeRemovalDoesNotEraseReplacement()
        {
            PublicEventManager manager = CreateManager();
            Dictionary<uint, IPublicEvent> events = GetEventStore(manager);
            var operations = new List<string>();
            Mock<IPublicEvent> finalisedEvent = CreateEvent(1u);
            finalisedEvent.SetupGet(@event => @event.IsFinalised).Returns(true);
            finalisedEvent
                .Setup(@event => @event.Update(FirstTick))
                .Callback(() => operations.Add("finalised"));
            Mock<IPublicEvent> replacingEvent = CreateEvent(2u);
            Mock<IPublicEvent> replacementEvent = CreateEvent(1u);
            replacingEvent
                .Setup(@event => @event.Update(FirstTick))
                .Callback(() =>
                {
                    operations.Add("replacing");
                    events[1u] = replacementEvent.Object;
                });
            RegisterEvents(manager, finalisedEvent.Object, replacingEvent.Object);

            manager.Update(FirstTick);

            Assert.Equal(["finalised", "replacing"], operations);
            Assert.Same(replacementEvent.Object, manager.GetEvent(1u));
            finalisedEvent.Verify(@event => @event.Dispose(), Times.Never);
            replacementEvent.Verify(@event => @event.Update(It.IsAny<double>()), Times.Never);
        }

        private static PublicEventManager CreateManager()
        {
            var manager = new PublicEventManager(
                Mock.Of<ILogger<PublicEventManager>>(),
                Mock.Of<IPublicEventTemplateManager>(),
                Mock.Of<IPublicEventFactory>(),
                Mock.Of<IFactory<IPublicEventCharacter>>());
            var map = new Mock<IBaseMap>();
            map.SetupGet(value => value.Entry).Returns(new WorldEntry { Id = 1u });
            manager.Initialise(map.Object);
            return manager;
        }

        private static Mock<IPublicEvent> CreateEvent(uint id)
        {
            var @event = new Mock<IPublicEvent>();
            @event.SetupGet(value => value.Id).Returns(id);
            @event.SetupGet(value => value.Guid).Returns(Guid.NewGuid());
            return @event;
        }

        private static void RegisterEvents(PublicEventManager manager, params IPublicEvent[] events)
        {
            Dictionary<uint, IPublicEvent> eventStore = GetEventStore(manager);
            foreach (IPublicEvent @event in events)
                eventStore.Add(@event.Id, @event);
        }

        private static Dictionary<uint, IPublicEvent> GetEventStore(PublicEventManager manager)
        {
            FieldInfo field = typeof(PublicEventManager).GetField(
                "publicEvents",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (Dictionary<uint, IPublicEvent>)field.GetValue(manager);
        }
    }
}
