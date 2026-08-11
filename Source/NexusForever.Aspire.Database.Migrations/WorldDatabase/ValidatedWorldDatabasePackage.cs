namespace NexusForever.Aspire.Database.Migrations.WorldDatabase
{
    public sealed class ValidatedWorldDatabasePackage
    {
        public string RootPath { get; }
        public IReadOnlyList<ValidatedWorldDatabaseFile> Files { get; }

        public ValidatedWorldDatabasePackage(
            string rootPath,
            IReadOnlyList<ValidatedWorldDatabaseFile> files)
        {
            RootPath = rootPath;
            Files = files;
        }
    }

    public sealed class ValidatedWorldDatabaseFile
    {
        public WorldDatabaseManifestEntry Entry { get; }
        public string Content { get; }

        public ValidatedWorldDatabaseFile(WorldDatabaseManifestEntry entry, string content)
        {
            Entry = entry;
            Content = content;
        }
    }

    public sealed class WorldDatabasePackageState
    {
        public WorldDatabaseManifest Manifest { get; }
        public ValidatedWorldDatabasePackage Package { get; }

        public WorldDatabasePackageState(
            WorldDatabaseManifest manifest,
            ValidatedWorldDatabasePackage package)
        {
            Manifest = manifest;
            Package = package;
        }
    }
}
