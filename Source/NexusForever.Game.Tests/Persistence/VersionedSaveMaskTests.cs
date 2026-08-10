using NexusForever.Database;

namespace NexusForever.Game.Tests.Persistence
{
    public class VersionedSaveMaskTests
    {
        [Flags]
        private enum TestSaveMask : byte
        {
            None   = 0x00,
            First  = 0x01,
            Second = 0x02,
            Third  = 0x04
        }

        [Fact]
        public void Constructor_UsesInitialState()
        {
            var saveMask = new VersionedSaveMask<TestSaveMask>(TestSaveMask.First | TestSaveMask.Second);

            Assert.True(saveMask.IsDirty);
            Assert.Equal(TestSaveMask.First | TestSaveMask.Second, saveMask.Current);

            VersionedSaveMaskSnapshot<TestSaveMask> snapshot = saveMask.Capture();
            saveMask.Acknowledge(snapshot);

            Assert.False(saveMask.IsDirty);
            Assert.Equal(TestSaveMask.None, saveMask.Current);
        }

        [Fact]
        public void Mark_SameBitDuringPendingSavePreservesNewMutation()
        {
            var saveMask = new VersionedSaveMask<TestSaveMask>();
            saveMask.Mark(TestSaveMask.First);
            VersionedSaveMaskSnapshot<TestSaveMask> snapshot = saveMask.Capture();

            saveMask.Mark(TestSaveMask.First);
            saveMask.Acknowledge(snapshot);

            Assert.Equal(TestSaveMask.First, saveMask.Current);
        }

        [Fact]
        public void Clear_ChangesCapturedBitVersionWithoutRestoringIt()
        {
            var saveMask = new VersionedSaveMask<TestSaveMask>(TestSaveMask.First | TestSaveMask.Second);
            VersionedSaveMaskSnapshot<TestSaveMask> snapshot = saveMask.Capture();

            saveMask.Clear(TestSaveMask.First);
            saveMask.Acknowledge(snapshot);

            Assert.Equal(TestSaveMask.None, saveMask.Current);
        }

        [Fact]
        public void Acknowledge_MultiBitSnapshotClearsOnlyUnchangedCapturedBits()
        {
            var saveMask = new VersionedSaveMask<TestSaveMask>();
            saveMask.Mark(TestSaveMask.First | TestSaveMask.Second);
            VersionedSaveMaskSnapshot<TestSaveMask> snapshot = saveMask.Capture();

            saveMask.Mark(TestSaveMask.Second);
            saveMask.Mark(TestSaveMask.Third);
            saveMask.Acknowledge(snapshot);

            Assert.Equal(TestSaveMask.Second | TestSaveMask.Third, saveMask.Current);
        }

        [Fact]
        public void Acknowledge_SnapshotFromDifferentMaskThrows()
        {
            var firstSaveMask = new VersionedSaveMask<TestSaveMask>(TestSaveMask.First);
            var secondSaveMask = new VersionedSaveMask<TestSaveMask>(TestSaveMask.First);
            VersionedSaveMaskSnapshot<TestSaveMask> snapshot = firstSaveMask.Capture();

            Assert.Throws<ArgumentException>(() => secondSaveMask.Acknowledge(snapshot));
        }
    }
}
