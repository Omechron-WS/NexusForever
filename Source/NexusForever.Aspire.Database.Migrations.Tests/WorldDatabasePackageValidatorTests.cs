using System.Text;
using NexusForever.Aspire.Database.Migrations.WorldDatabase;

namespace NexusForever.Aspire.Database.Migrations.Tests
{
    public sealed class WorldDatabasePackageValidatorTests
    {
        [Fact]
        public void ValidateLoadsExactPackageIntoMemory()
        {
            using var directory = new TemporaryDirectory();
            byte[] first = Encoding.UTF8.GetBytes("SELECT 1;\n");
            byte[] second = Encoding.UTF8.GetBytes("SELECT 2;\n");
            directory.Write("A/First.sql", first);
            directory.Write("B/Second.sql", second);
            WorldDatabaseManifest manifest = TestManifest.Create(
                TestManifest.Entry("A/First.sql", first),
                TestManifest.Entry("B/Second.sql", second, WorldDatabaseFileAction.Exclude, "Unsupported test content."));

            ValidatedWorldDatabasePackage package = new WorldDatabasePackageValidator().Validate(directory.Path, manifest);
            directory.Write("A/First.sql", Encoding.UTF8.GetBytes("changed"));

            Assert.Equal(2, package.Files.Count);
            Assert.Equal("SELECT 1;\n", package.Files[0].Content);
            Assert.Equal(WorldDatabaseFileAction.Exclude, package.Files[1].Entry.Action);
        }

        [Fact]
        public void ValidateReportsMissingAndUnexpectedSqlFiles()
        {
            using var directory = new TemporaryDirectory();
            byte[] expected = Encoding.UTF8.GetBytes("SELECT 1;");
            directory.Write("Extra.sql", expected);
            WorldDatabaseManifest manifest = TestManifest.Create(TestManifest.Entry("Expected.sql", expected));

            WorldDatabasePackageValidationException exception = Assert.Throws<WorldDatabasePackageValidationException>(
                () => new WorldDatabasePackageValidator().Validate(directory.Path, manifest));

            Assert.Contains("Required SQL file `Expected.sql` is missing", exception.Message);
            Assert.Contains("Unexpected SQL file `Extra.sql` is present", exception.Message);
        }

        [Fact]
        public void ValidateReportsSizeAndHashMismatch()
        {
            using var directory = new TemporaryDirectory();
            byte[] expected = Encoding.UTF8.GetBytes("SELECT 1;");
            directory.Write("File.sql", Encoding.UTF8.GetBytes("SELECT 2;"));
            WorldDatabaseManifest manifest = TestManifest.Create(TestManifest.Entry("File.sql", expected));

            WorldDatabasePackageValidationException exception = Assert.Throws<WorldDatabasePackageValidationException>(
                () => new WorldDatabasePackageValidator().Validate(directory.Path, manifest));

            Assert.Contains("SHA-256", exception.Message);
        }

        [Fact]
        public void ValidateRejectsInvalidUtf8EvenWhenBytesArePinned()
        {
            using var directory = new TemporaryDirectory();
            byte[] invalidUtf8 = [0xc3, 0x28];
            directory.Write("File.sql", invalidUtf8);
            WorldDatabaseManifest manifest = TestManifest.Create(TestManifest.Entry("File.sql", invalidUtf8));

            WorldDatabasePackageValidationException exception = Assert.Throws<WorldDatabasePackageValidationException>(
                () => new WorldDatabasePackageValidator().Validate(directory.Path, manifest));

            Assert.Contains("not strict UTF-8", exception.Message);
        }

        [Fact]
        public void ValidateAlwaysRoutesPinnedCrimsonFileThroughSemanticPreflight()
        {
            using var directory = new TemporaryDirectory();
            byte[] content = Encoding.UTF8.GetBytes("SELECT 1;");
            directory.Write("Olyssia/CrimsonIsle.sql", content);
            WorldDatabaseManifest manifest = TestManifest.Create(TestManifest.Entry("Olyssia/CrimsonIsle.sql", content));

            WorldDatabasePackageValidationException exception = Assert.Throws<WorldDatabasePackageValidationException>(
                () => new WorldDatabasePackageValidator().Validate(directory.Path, manifest));

            Assert.Contains("Crimson Isle quest 5593 semantic preflight", exception.Message);
        }
    }
}
