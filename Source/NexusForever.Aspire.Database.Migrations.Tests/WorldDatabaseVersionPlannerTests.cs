using NexusForever.Aspire.Database.Migrations.WorldDatabase;
using NexusForever.Database.World.Model;

namespace NexusForever.Aspire.Database.Migrations.Tests
{
    public sealed class WorldDatabaseVersionPlannerTests
    {
        private static readonly WorldDatabaseManifestEntry Entry = new()
        {
            Path = "Alizar/Algoroc.sql",
            Size = 1,
            Sha256 = new string('a', 64),
            Action = WorldDatabaseFileAction.Import
        };

        [Fact]
        public void ExactCanonicalPathAndHashSkipsSql()
        {
            WorldDatabaseVersionPlan plan = WorldDatabaseVersionPlanner.Create(
                Entry,
                [Version("Alizar/Algoroc.sql", Entry.Sha256)]);

            Assert.Equal(WorldDatabaseVersionDisposition.AlreadyApplied, plan.Disposition);
        }

        [Fact]
        public void ExactLegacyBasenameAndHashRecordsCanonicalAlias()
        {
            DateTime appliedOn = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
            WorldDatabaseVersionPlan plan = WorldDatabaseVersionPlanner.Create(
                Entry,
                [Version("Algoroc.sql", Entry.Sha256, appliedOn)]);

            Assert.Equal(WorldDatabaseVersionDisposition.RecordCanonicalAlias, plan.Disposition);
            Assert.Equal(appliedOn, plan.LegacyAppliedOn);
        }

        [Fact]
        public void LegacyBasenameWithDifferentHashIsAConflict()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                WorldDatabaseVersionPlanner.Create(
                    Entry,
                    [Version("Algoroc.sql", new string('b', 64))]));

            Assert.Contains("version conflict", exception.Message);
            Assert.Contains("Algoroc.sql", exception.Message);
        }

        [Fact]
        public void CanonicalPathWithDifferentHashIsAConflict()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                WorldDatabaseVersionPlanner.Create(
                    Entry,
                    [Version("Alizar/Algoroc.sql", new string('b', 64))]));

            Assert.Contains("version conflict", exception.Message);
            Assert.Contains("Alizar/Algoroc.sql", exception.Message);
        }

        [Fact]
        public void MatchingHashAtUnrelatedPathDoesNotSkipSql()
        {
            WorldDatabaseVersionPlan plan = WorldDatabaseVersionPlanner.Create(
                Entry,
                [Version("Elsewhere.sql", Entry.Sha256)]);

            Assert.Equal(WorldDatabaseVersionDisposition.Apply, plan.Disposition);
        }

        private static VersionModel Version(string name, string hash, DateTime? appliedOn = null)
        {
            return new VersionModel
            {
                FileName = name,
                FileHash = hash,
                AppliedOn = appliedOn ?? DateTime.UtcNow
            };
        }
    }
}
