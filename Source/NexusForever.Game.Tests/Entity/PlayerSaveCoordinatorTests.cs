using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;

namespace NexusForever.Game.Tests.Entity
{
    public class PlayerSaveCoordinatorTests
    {
        [Fact]
        public async Task SaveAsync_WaitsForEveryPlayerSave()
        {
            var firstCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IPlayer> firstPlayer = CreatePlayer(firstCompletion.Task);
            Mock<IPlayer> secondPlayer = CreatePlayer(secondCompletion.Task);

            Task save = PlayerSaveCoordinator.SaveAsync([firstPlayer.Object, secondPlayer.Object]);

            firstPlayer.Verify(player => player.SaveDirectAsync(It.IsAny<CancellationToken>()), Times.Once);
            secondPlayer.Verify(player => player.SaveDirectAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.False(save.IsCompleted);

            firstCompletion.SetResult();
            Assert.False(save.IsCompleted);

            secondCompletion.SetResult();
            await save;
        }

        [Fact]
        public async Task SaveAsync_SynchronousFailure_StillAttemptsRemainingPlayers()
        {
            var expected = new InvalidOperationException("first failure");
            var firstPlayer = new Mock<IPlayer>();
            firstPlayer.Setup(player => player.SaveDirectAsync(It.IsAny<CancellationToken>())).Throws(expected);
            Mock<IPlayer> secondPlayer = CreatePlayer(Task.CompletedTask);

            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
                PlayerSaveCoordinator.SaveAsync([firstPlayer.Object, secondPlayer.Object]));

            secondPlayer.Verify(player => player.SaveDirectAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.Contains(expected, exception.InnerExceptions);
        }

        [Fact]
        public async Task SaveAsync_MultipleAsynchronousFailures_ReportsEveryFailure()
        {
            var firstFailure = new InvalidOperationException("first failure");
            var secondFailure = new InvalidOperationException("second failure");
            Mock<IPlayer> firstPlayer = CreatePlayer(Task.FromException(firstFailure));
            Mock<IPlayer> secondPlayer = CreatePlayer(Task.FromException(secondFailure));

            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
                PlayerSaveCoordinator.SaveAsync([firstPlayer.Object, secondPlayer.Object]));

            Assert.Contains(firstFailure, exception.InnerExceptions);
            Assert.Contains(secondFailure, exception.InnerExceptions);
        }

        [Fact]
        public async Task SaveAsync_Cancellation_PropagatesToPlayerAndCaller()
        {
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            var player = new Mock<IPlayer>();
            player.Setup(mock => mock.SaveDirectAsync(cancellationSource.Token))
                .Returns(Task.FromCanceled(cancellationSource.Token));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PlayerSaveCoordinator.SaveAsync([player.Object], cancellationSource.Token));

            player.Verify(mock => mock.SaveDirectAsync(cancellationSource.Token), Times.Once);
        }

        [Fact]
        public async Task SaveAsync_FailureAndCancellation_ReportsBothOutcomes()
        {
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            var expected = new InvalidOperationException("failure");
            var failedPlayer = new Mock<IPlayer>();
            failedPlayer.Setup(mock => mock.SaveDirectAsync(cancellationSource.Token)).Throws(expected);
            var cancelledPlayer = new Mock<IPlayer>();
            cancelledPlayer.Setup(mock => mock.SaveDirectAsync(cancellationSource.Token))
                .Returns(Task.FromCanceled(cancellationSource.Token));

            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
                PlayerSaveCoordinator.SaveAsync([failedPlayer.Object, cancelledPlayer.Object], cancellationSource.Token));

            Assert.Contains(expected, exception.InnerExceptions);
            Assert.Contains(exception.InnerExceptions, inner => inner is OperationCanceledException);
        }

        private static Mock<IPlayer> CreatePlayer(Task task)
        {
            var player = new Mock<IPlayer>();
            player.Setup(mock => mock.SaveDirectAsync(It.IsAny<CancellationToken>())).Returns(task);
            return player;
        }
    }
}
