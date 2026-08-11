using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexusForever.Aspire.Database.Migrations.WorldDatabase
{
    public enum WorldDatabaseFileAction
    {
        Unknown,
        Import,
        Exclude
    }

    public sealed class WorldDatabaseManifest
    {
        private const string ResourceName = "NexusForever.WorldDatabaseManifest.json";

        public int FormatVersion { get; set; }
        public string SourceRevision { get; set; }
        public List<WorldDatabaseManifestEntry> Files { get; set; } = [];

        public static WorldDatabaseManifest LoadDefault()
        {
            using Stream stream = typeof(WorldDatabaseManifest).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded world database manifest `{ResourceName}` was not found.");

            return Load(stream);
        }

        public static WorldDatabaseManifest Load(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            using FileStream stream = File.OpenRead(path);
            return Load(stream);
        }

        private static WorldDatabaseManifest Load(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));

            WorldDatabaseManifest manifest = JsonSerializer.Deserialize<WorldDatabaseManifest>(stream, options)
                ?? throw new InvalidDataException("The embedded world database manifest is empty.");
            manifest.Validate();
            return manifest;
        }

        public void Validate()
        {
            var diagnostics = new List<string>();

            if (FormatVersion != 1)
                diagnostics.Add($"Unsupported manifest format version `{FormatVersion}`.");
            if (string.IsNullOrWhiteSpace(SourceRevision)
                || SourceRevision.Length != 40
                || SourceRevision.Any(c => !IsLowerHex(c)))
            {
                diagnostics.Add("Source revision must be a 40-character lowercase hexadecimal Git commit.");
            }

            if (Files == null || Files.Count == 0)
            {
                diagnostics.Add("Manifest must contain at least one file.");
            }
            else
            {
                var paths = new HashSet<string>(StringComparer.Ordinal);
                var baseNames = new HashSet<string>(StringComparer.Ordinal);
                string previousPath = null;

                foreach (WorldDatabaseManifestEntry entry in Files)
                {
                    if (entry == null)
                    {
                        diagnostics.Add("Manifest contains a null file entry.");
                        continue;
                    }

                    if (!IsSafeRelativeSqlPath(entry.Path))
                        diagnostics.Add($"Manifest path `{entry.Path}` is not a normalized relative .sql path.");
                    else
                    {
                        if (!paths.Add(entry.Path))
                            diagnostics.Add($"Manifest path `{entry.Path}` is duplicated.");

                        if (previousPath != null && StringComparer.Ordinal.Compare(previousPath, entry.Path) >= 0)
                            diagnostics.Add("Manifest files must be unique and sorted by ordinal relative path.");
                        previousPath = entry.Path;
                    }

                    if (!string.IsNullOrEmpty(entry.Path))
                    {
                        string baseName = Path.GetFileName(entry.Path);
                        if (!baseNames.Add(baseName))
                            diagnostics.Add($"Manifest basename `{baseName}` is not unique; legacy ledger matching would be ambiguous.");
                    }

                    if (entry.Size < 0)
                        diagnostics.Add($"Manifest size for `{entry.Path}` cannot be negative.");
                    if (entry.Sha256 == null
                        || entry.Sha256.Length != 64
                        || entry.Sha256.Any(c => !IsLowerHex(c)))
                    {
                        diagnostics.Add($"Manifest SHA-256 for `{entry.Path}` must be 64 lowercase hexadecimal characters.");
                    }

                    switch (entry.Action)
                    {
                        case WorldDatabaseFileAction.Import when !string.IsNullOrWhiteSpace(entry.Reason):
                            diagnostics.Add($"Imported file `{entry.Path}` cannot have an exclusion reason.");
                            break;
                        case WorldDatabaseFileAction.Exclude when string.IsNullOrWhiteSpace(entry.Reason):
                            diagnostics.Add($"Excluded file `{entry.Path}` must explain why it is excluded.");
                            break;
                        case WorldDatabaseFileAction.Import:
                        case WorldDatabaseFileAction.Exclude:
                            break;
                        default:
                            diagnostics.Add($"Manifest file `{entry.Path}` must have action Import or Exclude.");
                            break;
                    }
                }
            }

            if (diagnostics.Count > 0)
                throw new WorldDatabasePackageValidationException(diagnostics);
        }

        private static bool IsSafeRelativeSqlPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)
                || Path.IsPathRooted(path)
                || path.Contains('\\')
                || path.StartsWith("/", StringComparison.Ordinal)
                || !path.EndsWith(".sql", StringComparison.Ordinal))
            {
                return false;
            }

            string[] segments = path.Split('/');
            return segments.All(segment => segment.Length > 0 && segment is not "." and not "..");
        }

        private static bool IsLowerHex(char value)
        {
            return value is >= '0' and <= '9' or >= 'a' and <= 'f';
        }
    }

    public sealed class WorldDatabaseManifestEntry
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
        public WorldDatabaseFileAction Action { get; set; }
        public string Reason { get; set; }
    }
}
