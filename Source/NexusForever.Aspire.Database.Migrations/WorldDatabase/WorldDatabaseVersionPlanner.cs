using NexusForever.Database.World.Model;

namespace NexusForever.Aspire.Database.Migrations.WorldDatabase
{
    public enum WorldDatabaseVersionDisposition
    {
        Apply,
        AlreadyApplied,
        RecordCanonicalAlias
    }

    public sealed class WorldDatabaseVersionPlan
    {
        public WorldDatabaseVersionDisposition Disposition { get; }
        public DateTime? LegacyAppliedOn { get; }

        public WorldDatabaseVersionPlan(
            WorldDatabaseVersionDisposition disposition,
            DateTime? legacyAppliedOn = null)
        {
            Disposition = disposition;
            LegacyAppliedOn = legacyAppliedOn;
        }
    }

    public static class WorldDatabaseVersionPlanner
    {
        public static WorldDatabaseVersionPlan Create(
            WorldDatabaseManifestEntry entry,
            IEnumerable<VersionModel> versions)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(versions);

            VersionModel[] materializedVersions = versions.ToArray();
            string legacyFileName = Path.GetFileName(entry.Path);
            VersionModel[] recognizedVersions = materializedVersions
                .Where(version => version.FileName.Equals(entry.Path, StringComparison.Ordinal)
                    || version.FileName.Equals(legacyFileName, StringComparison.Ordinal))
                .ToArray();

            VersionModel conflictingVersion = recognizedVersions.FirstOrDefault(
                version => !version.FileHash.Equals(entry.Sha256, StringComparison.Ordinal));
            if (conflictingVersion != null)
            {
                throw new InvalidOperationException(
                    $"World database version conflict for `{entry.Path}`: ledger name `{conflictingVersion.FileName}` "
                    + $"has SHA-256 `{conflictingVersion.FileHash}`, expected `{entry.Sha256}`.");
            }

            if (recognizedVersions.Any(version => version.FileName.Equals(entry.Path, StringComparison.Ordinal)))
                return new WorldDatabaseVersionPlan(WorldDatabaseVersionDisposition.AlreadyApplied);

            VersionModel legacyVersion = recognizedVersions.SingleOrDefault(
                version => version.FileName.Equals(legacyFileName, StringComparison.Ordinal));
            if (legacyVersion != null && !legacyFileName.Equals(entry.Path, StringComparison.Ordinal))
            {
                return new WorldDatabaseVersionPlan(
                    WorldDatabaseVersionDisposition.RecordCanonicalAlias,
                    legacyVersion.AppliedOn);
            }

            return new WorldDatabaseVersionPlan(WorldDatabaseVersionDisposition.Apply);
        }
    }
}
