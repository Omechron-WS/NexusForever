using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Map;

namespace NexusForever.Game.Tests.Map
{
    public class MapCellTests
    {
        [Fact]
        public void Update_EntityFailureDoesNotSuppressSnapshotSiblings()
        {
            const double firstTick = 0.25d;
            const double secondTick = 0.5d;
            var cell = new MapCell(1u, 2u);
            var failedEntity = new Mock<IGridEntity>();
            failedEntity
                .Setup(entity => entity.Update(It.IsAny<double>()))
                .Throws(new InvalidOperationException("entity failure"));
            var healthyEntity = new Mock<IGridEntity>();
            cell.AddEntity(failedEntity.Object);
            cell.AddEntity(healthyEntity.Object);

            cell.Update(firstTick);
            cell.Update(secondTick);

            failedEntity.Verify(entity => entity.Update(firstTick), Times.Once);
            failedEntity.Verify(entity => entity.Update(secondTick), Times.Once);
            healthyEntity.Verify(entity => entity.Update(firstTick), Times.Once);
            healthyEntity.Verify(entity => entity.Update(secondTick), Times.Once);
        }

        [Fact]
        public void Update_RemovedSnapshotEntityIsSkippedAndAdditionWaitsForNextTick()
        {
            const double firstTick = 0.25d;
            const double secondTick = 0.5d;
            var cell = new MapCell(1u, 2u);
            var firstEntity = new Mock<IGridEntity>();
            var removedEntity = new Mock<IGridEntity>();
            var addedEntity = new Mock<IGridEntity>();
            firstEntity
                .Setup(entity => entity.Update(firstTick))
                .Callback(() =>
                {
                    cell.RemoveEntity(removedEntity.Object);
                    cell.AddEntity(addedEntity.Object);
                });
            cell.AddEntity(firstEntity.Object);
            cell.AddEntity(removedEntity.Object);

            cell.Update(firstTick);
            cell.Update(secondTick);

            firstEntity.Verify(entity => entity.Update(firstTick), Times.Once);
            firstEntity.Verify(entity => entity.Update(secondTick), Times.Once);
            removedEntity.Verify(entity => entity.Update(It.IsAny<double>()), Times.Never);
            addedEntity.Verify(entity => entity.Update(firstTick), Times.Never);
            addedEntity.Verify(entity => entity.Update(secondTick), Times.Once);
        }
    }
}
