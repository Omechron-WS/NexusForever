using System.Security.Cryptography;
using NexusForever.Aspire.Database.Migrations.WorldDatabase;

namespace NexusForever.Aspire.Database.Migrations.Tests
{
    internal static class TestManifest
    {
        public static WorldDatabaseManifest Create(params WorldDatabaseManifestEntry[] entries)
        {
            return new WorldDatabaseManifest
            {
                FormatVersion = 1,
                SourceRevision = new string('0', 40),
                Files = entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToList()
            };
        }

        public static WorldDatabaseManifestEntry Entry(
            string path,
            string content,
            WorldDatabaseFileAction action = WorldDatabaseFileAction.Import,
            string reason = null)
        {
            return Entry(path, System.Text.Encoding.UTF8.GetBytes(content), action, reason);
        }

        public static WorldDatabaseManifestEntry Entry(
            string path,
            byte[] content,
            WorldDatabaseFileAction action = WorldDatabaseFileAction.Import,
            string reason = null)
        {
            return new WorldDatabaseManifestEntry
            {
                Path = path,
                Size = content.LongLength,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
                Action = action,
                Reason = reason
            };
        }
    }

    internal sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "nexusforever-world-bootstrap-tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Write(string relativePath, byte[] bytes)
        {
            string path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
