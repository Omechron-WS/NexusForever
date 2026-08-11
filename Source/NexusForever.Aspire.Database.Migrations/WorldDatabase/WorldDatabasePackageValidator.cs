using System.Security.Cryptography;
using System.Text;
using NexusForever.Database.World.Validation;

namespace NexusForever.Aspire.Database.Migrations.WorldDatabase
{
    public sealed class WorldDatabasePackageValidator
    {
        private const string CrimsonIslePath = "Olyssia/CrimsonIsle.sql";
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        public ValidatedWorldDatabasePackage Validate(string rootPath, WorldDatabaseManifest manifest)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
            ArgumentNullException.ThrowIfNull(manifest);
            manifest.Validate();

            string fullRootPath = Path.GetFullPath(rootPath);
            if (!Directory.Exists(fullRootPath))
            {
                throw new WorldDatabasePackageValidationException(
                    [$"External world database root `{fullRootPath}` does not exist or is not a directory."]);
            }

            var diagnostics = new List<string>();
            Dictionary<string, string> actualSqlFiles = EnumerateSqlFiles(fullRootPath, diagnostics);
            var expectedPaths = manifest.Files.Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);

            foreach (string missingPath in expectedPaths.Except(actualSqlFiles.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                diagnostics.Add($"Required SQL file `{missingPath}` is missing.");
            foreach (string extraPath in actualSqlFiles.Keys.Except(expectedPaths, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                diagnostics.Add($"Unexpected SQL file `{extraPath}` is present.");

            var validatedFiles = new List<ValidatedWorldDatabaseFile>(manifest.Files.Count);
            foreach (WorldDatabaseManifestEntry entry in manifest.Files)
            {
                if (!actualSqlFiles.TryGetValue(entry.Path, out string filePath))
                    continue;

                byte[] bytes;
                try
                {
                    FileAttributes attributes = File.GetAttributes(filePath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        diagnostics.Add($"SQL file `{entry.Path}` is a symbolic link or reparse point.");
                        continue;
                    }

                    bytes = File.ReadAllBytes(filePath);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
                {
                    diagnostics.Add($"Unable to read SQL file `{entry.Path}`: {exception.Message}");
                    continue;
                }

                if (bytes.LongLength != entry.Size)
                    diagnostics.Add($"SQL file `{entry.Path}` has size {bytes.LongLength}; expected {entry.Size} bytes.");

                string actualHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (!actualHash.Equals(entry.Sha256, StringComparison.Ordinal))
                    diagnostics.Add($"SQL file `{entry.Path}` has SHA-256 `{actualHash}`; expected `{entry.Sha256}`.");

                string content;
                try
                {
                    content = StrictUtf8.GetString(bytes);
                }
                catch (DecoderFallbackException exception)
                {
                    diagnostics.Add($"SQL file `{entry.Path}` is not strict UTF-8: {exception.Message}");
                    continue;
                }

                if (entry.Path.Equals(CrimsonIslePath, StringComparison.Ordinal))
                {
                    CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(content);
                    if (!result.IsValid)
                    {
                        diagnostics.Add(
                            $"SQL file `{entry.Path}` failed the Crimson Isle quest 5593 semantic preflight: "
                            + string.Join("; ", result.Diagnostics));
                    }
                }

                validatedFiles.Add(new ValidatedWorldDatabaseFile(entry, content));
            }

            if (diagnostics.Count > 0)
                throw new WorldDatabasePackageValidationException(diagnostics);

            return new ValidatedWorldDatabasePackage(fullRootPath, validatedFiles);
        }

        private static Dictionary<string, string> EnumerateSqlFiles(
            string fullRootPath,
            ICollection<string> diagnostics)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var directories = new Stack<string>();
            directories.Push(fullRootPath);

            while (directories.TryPop(out string directoryPath))
            {
                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(directoryPath).ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add($"Unable to enumerate `{directoryPath}`: {exception.Message}");
                    continue;
                }

                foreach (string entryPath in entries)
                {
                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entryPath);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        diagnostics.Add($"Unable to inspect `{entryPath}`: {exception.Message}");
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        diagnostics.Add($"Symbolic link or reparse point `{NormalizeRelativePath(fullRootPath, entryPath)}` is not allowed in the external world database root.");
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories.Push(entryPath);
                        continue;
                    }

                    if (!Path.GetExtension(entryPath).Equals(".sql", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string relativePath = NormalizeRelativePath(fullRootPath, entryPath);
                    if (!files.TryAdd(relativePath, entryPath))
                        diagnostics.Add($"SQL path `{relativePath}` was enumerated more than once.");
                }
            }

            return files;
        }

        private static string NormalizeRelativePath(string rootPath, string filePath)
        {
            return Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
        }
    }
}
