using System.Data.Common;
using NexusForever.Aspire.Database.Migrations.Configuration.Model;

namespace NexusForever.Aspire.Database.Migrations.WorldDatabase
{
    public static class WorldDatabaseBootstrap
    {
        public static WorldDatabasePackageState Prepare(
            WorldDatabaseOptions options,
            WorldDatabaseManifest manifest = null,
            WorldDatabasePackageValidator validator = null)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.LockTimeoutSeconds is < 1 or > 300)
                throw new InvalidDataException("WorldDatabase:LockTimeoutSeconds must be between 1 and 300.");
            if (options.CommandTimeoutSeconds is < 1 or > 3_600)
                throw new InvalidDataException("WorldDatabase:CommandTimeoutSeconds must be between 1 and 3600.");

            manifest ??= WorldDatabaseManifest.LoadDefault();
            manifest.Validate();

            if (string.IsNullOrWhiteSpace(options.Path))
            {
                if (options.Required)
                    throw new InvalidDataException("WorldDatabase:Path is required but was not configured.");

                return new WorldDatabasePackageState(manifest, null);
            }

            validator ??= new WorldDatabasePackageValidator();
            return new WorldDatabasePackageState(manifest, validator.Validate(options.Path, manifest));
        }

        public static string PrepareConnectionString(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            foreach (string key in builder.Keys.Cast<string>().ToArray())
            {
                string normalizedKey = new(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                if (normalizedKey is "allowuservariables" or "ignorecommandtransaction")
                    builder.Remove(key);
            }

            builder["Allow User Variables"] = true;
            builder["Ignore Command Transaction"] = false;
            return builder.ConnectionString;
        }
    }
}
