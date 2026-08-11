using NexusForever.Aspire.Database.Migrations.WorldDatabase;

namespace NexusForever.Aspire.Database.Migrations.Tests
{
    public sealed class WorldDatabaseManifestTests
    {
        [Fact]
        public void DefaultManifestPinsAuditedCompatibilityProfile()
        {
            WorldDatabaseManifest manifest = WorldDatabaseManifest.LoadDefault();

            Assert.Equal(1, manifest.FormatVersion);
            Assert.Equal("7397e7f8a810fe240922f6e7bf5a8b7e551f8b3b", manifest.SourceRevision);
            Assert.Equal(30, manifest.Files.Count);
            Assert.Equal(28, manifest.Files.Count(entry => entry.Action == WorldDatabaseFileAction.Import));

            WorldDatabaseManifestEntry[] excluded = manifest.Files
                .Where(entry => entry.Action == WorldDatabaseFileAction.Exclude)
                .ToArray();
            Assert.Collection(
                excluded,
                entry =>
                {
                    Assert.Equal("Instance/Expedition/Evil from the Ether.sql", entry.Path);
                    Assert.Equal("65830f1327bc316cf99a46b6f90f1f10fa39b6d096b2b7e02083790b61e92ebb", entry.Sha256);
                    Assert.Contains("entity_script", entry.Reason);
                    Assert.Contains("entity_property", entry.Reason);
                    Assert.Contains("creature_info_property", entry.Reason);
                },
                entry =>
                {
                    Assert.Equal("Instance/Tutorial/New Player Experience.sql", entry.Path);
                    Assert.Equal("782e70786f5459c5ed7f5d78158e05d8384616caee88c15471f3aacc2729a076", entry.Sha256);
                    Assert.Contains("entity_script", entry.Reason);
                    Assert.Contains("Mode", entry.Reason);
                });

            Assert.Equal(
                manifest.Files.Select(entry => entry.Path).Order(StringComparer.Ordinal),
                manifest.Files.Select(entry => entry.Path));
            Assert.Equal(30, manifest.Files.Select(entry => Path.GetFileName(entry.Path)).Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void ManifestRejectsTraversalAndAmbiguousLegacyBasenames()
        {
            WorldDatabaseManifest manifest = TestManifest.Create(
                TestManifest.Entry("../outside.sql", "SELECT 1;"),
                TestManifest.Entry("Elsewhere/outside.sql", "SELECT 2;"));

            WorldDatabasePackageValidationException exception = Assert.Throws<WorldDatabasePackageValidationException>(manifest.Validate);

            Assert.Contains("normalized relative .sql path", exception.Message);
            Assert.Contains("basename", exception.Message);
        }

        [Fact]
        public void ManifestRejectsMissingAction()
        {
            WorldDatabaseManifest manifest = TestManifest.Create(new WorldDatabaseManifestEntry
            {
                Path = "File.sql",
                Size = 0,
                Sha256 = new string('a', 64)
            });

            WorldDatabasePackageValidationException exception = Assert.Throws<WorldDatabasePackageValidationException>(manifest.Validate);

            Assert.Contains("must have action Import or Exclude", exception.Message);
        }
    }
}
