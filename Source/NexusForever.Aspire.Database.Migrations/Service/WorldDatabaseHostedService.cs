using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusForever.Aspire.Database.Migrations.Configuration.Model;
using NexusForever.Aspire.Database.Migrations.WorldDatabase;
using NexusForever.Database.World;
using NexusForever.Database.World.Model;

namespace NexusForever.Aspire.Database.Migrations.Service
{
    public sealed class WorldDatabaseHostedService : IHostedService
    {
        private const string AdvisoryLockName = "nexusforever-world-database-v1";

        private static readonly HashSet<string> RequiredEntityCascadeTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "entity_emote",
            "entity_event",
            "entity_spline",
            "entity_stats",
            "entity_vendor",
            "entity_vendor_category",
            "entity_vendor_item"
        };

        private readonly ILogger<WorldDatabaseHostedService> _log;
        private readonly WorldDatabaseOptions _options;
        private readonly WorldDatabasePackageState _packageState;
        private readonly WorldContext _context;

        public WorldDatabaseHostedService(
            ILogger<WorldDatabaseHostedService> log,
            IOptions<WorldDatabaseOptions> options,
            WorldDatabasePackageState packageState,
            WorldContext context)
        {
            _log = log;
            _options = options.Value;
            _packageState = packageState;
            _context = context;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_packageState.Package == null)
            {
                _log.LogWarning("World database path is not configured. Skipping external world content import.");
                return;
            }

            ValidatedWorldDatabasePackage package = _packageState.Package;
            int expectedImportCount = package.Files.Count(file => file.Entry.Action == WorldDatabaseFileAction.Import);
            int excludedCount = package.Files.Count - expectedImportCount;

            _log.LogInformation(
                "Importing pinned world database revision {Revision}: {ImportCount} compatible files, {ExcludedCount} explicit exclusions.",
                _packageState.Manifest.SourceRevision,
                expectedImportCount,
                excludedCount);

            _context.Database.SetCommandTimeout(_options.CommandTimeoutSeconds);
            await _context.Database.OpenConnectionAsync(cancellationToken);

            DbConnection connection = _context.Database.GetDbConnection();
            bool lockAcquired = false;
            Exception operationException = null;
            try
            {
                await AcquireLockAsync(connection, _options.LockTimeoutSeconds, cancellationToken);
                lockAcquired = true;

                await VerifyDatabaseSafetyAsync(connection, cancellationToken);

                int appliedCount = 0;
                int skippedCount = 0;
                int aliasCount = 0;

                foreach (ValidatedWorldDatabaseFile file in package.Files)
                {
                    if (file.Entry.Action == WorldDatabaseFileAction.Exclude)
                    {
                        _log.LogWarning(
                            "Excluding pinned world database file {Path}: {Reason}",
                            file.Entry.Path,
                            file.Entry.Reason);
                        continue;
                    }

                    WorldDatabaseVersionDisposition disposition = await ImportFileAsync(file, cancellationToken);
                    switch (disposition)
                    {
                        case WorldDatabaseVersionDisposition.Apply:
                            appliedCount++;
                            break;
                        case WorldDatabaseVersionDisposition.AlreadyApplied:
                            skippedCount++;
                            break;
                        case WorldDatabaseVersionDisposition.RecordCanonicalAlias:
                            aliasCount++;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null);
                    }
                }

                _log.LogInformation(
                    "World database compatibility profile complete: {CompatibleCount} compatible ({AppliedCount} applied, {SkippedCount} already applied, {AliasCount} legacy aliases), {ExcludedCount} excluded.",
                    expectedImportCount,
                    appliedCount,
                    skippedCount,
                    aliasCount,
                    excludedCount);
            }
            catch (Exception exception)
            {
                operationException = exception;
                throw;
            }
            finally
            {
                Exception cleanupException = null;
                if (lockAcquired)
                {
                    try
                    {
                        await ReleaseLockAsync(connection);
                    }
                    catch (Exception exception)
                    {
                        _log.LogError(exception, "Failed to explicitly release world database advisory lock; closing the connection will release it.");
                        cleanupException = exception;
                    }
                }

                try
                {
                    await _context.Database.CloseConnectionAsync();
                }
                catch (Exception exception)
                {
                    _log.LogError(exception, "Failed to close the world database import connection.");
                    cleanupException ??= exception;
                }

                if (operationException == null && cleanupException != null)
                {
                    throw new InvalidOperationException(
                        "World database import completed, but advisory-lock connection cleanup failed.",
                        cleanupException);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private async Task<WorldDatabaseVersionDisposition> ImportFileAsync(
            ValidatedWorldDatabaseFile file,
            CancellationToken cancellationToken)
        {
            await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                string legacyFileName = Path.GetFileName(file.Entry.Path);
                List<VersionModel> relevantVersions = await _context.Version
                    .AsNoTracking()
                    .Where(version => version.FileName == file.Entry.Path
                        || version.FileName == legacyFileName
                        || version.FileHash == file.Entry.Sha256)
                    .ToListAsync(cancellationToken);
                WorldDatabaseVersionPlan plan = WorldDatabaseVersionPlanner.Create(file.Entry, relevantVersions);

                if (plan.Disposition == WorldDatabaseVersionDisposition.AlreadyApplied)
                {
                    await transaction.CommitAsync(cancellationToken);
                    _log.LogInformation("Skipping already applied world database file {Path}.", file.Entry.Path);
                    return plan.Disposition;
                }

                if (plan.Disposition == WorldDatabaseVersionDisposition.Apply)
                {
                    _log.LogInformation("Applying world database file {Path}.", file.Entry.Path);
                    await WorldDatabaseSqlExecutor.ExecuteAsync(
                        _context.Database.GetDbConnection(),
                        transaction.GetDbTransaction(),
                        file.Content,
                        _options.CommandTimeoutSeconds,
                        cancellationToken);
                }
                else
                {
                    _log.LogInformation(
                        "Recording canonical path {Path} for an exact legacy basename/hash version record.",
                        file.Entry.Path);
                }

                _context.Version.Add(new VersionModel
                {
                    FileName = file.Entry.Path,
                    FileHash = file.Entry.Sha256,
                    AppliedOn = plan.LegacyAppliedOn ?? DateTime.UtcNow
                });
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _context.ChangeTracker.Clear();

                return plan.Disposition;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    _log.LogError(rollbackException, "Rollback also failed for world database file {Path}.", file.Entry.Path);
                }

                _context.ChangeTracker.Clear();
                throw new WorldDatabaseImportException(file.Entry.Path, exception);
            }
        }

        private static async Task AcquireLockAsync(
            DbConnection connection,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT GET_LOCK('{AdvisoryLockName}', {timeoutSeconds});";
            object result = await command.ExecuteScalarAsync(cancellationToken);
            if (result == null || result == DBNull.Value || Convert.ToInt32(result) != 1)
            {
                throw new TimeoutException(
                    $"Could not acquire MySQL advisory lock `{AdvisoryLockName}` within {timeoutSeconds} seconds.");
            }
        }

        private static async Task ReleaseLockAsync(DbConnection connection)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT RELEASE_LOCK('{AdvisoryLockName}');";
            object result = await command.ExecuteScalarAsync(CancellationToken.None);
            if (result == null || result == DBNull.Value || Convert.ToInt32(result) != 1)
                throw new InvalidOperationException($"MySQL advisory lock `{AdvisoryLockName}` was not owned by this connection at release.");
        }

        private static async Task VerifyDatabaseSafetyAsync(
            DbConnection connection,
            CancellationToken cancellationToken)
        {
            object foreignKeyChecks = await ExecuteScalarAsync(
                connection,
                "SELECT @@SESSION.foreign_key_checks;",
                cancellationToken);
            if (foreignKeyChecks == null || Convert.ToInt32(foreignKeyChecks) != 1)
                throw new InvalidOperationException("World import requires @@SESSION.foreign_key_checks = 1.");

            string sqlMode = Convert.ToString(await ExecuteScalarAsync(
                connection,
                "SELECT @@SESSION.sql_mode;",
                cancellationToken)) ?? string.Empty;
            HashSet<string> sqlModes = sqlMode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!sqlModes.Contains("STRICT_TRANS_TABLES") && !sqlModes.Contains("STRICT_ALL_TABLES"))
                throw new InvalidOperationException("World import requires STRICT_TRANS_TABLES or STRICT_ALL_TABLES in @@SESSION.sql_mode.");

            List<string> nonInnoDbTables = await ReadFirstColumnAsync(
                connection,
                "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES "
                    + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' "
                    + "AND COALESCE(UPPER(ENGINE), '') <> 'INNODB' ORDER BY TABLE_NAME;",
                cancellationToken);
            if (nonInnoDbTables.Count > 0)
            {
                throw new InvalidOperationException(
                    "World import requires every world schema table to use InnoDB. Incompatible tables: "
                    + string.Join(", ", nonInnoDbTables));
            }

            Dictionary<string, string> cascadeRules = await ReadEntityDeleteRulesAsync(connection, cancellationToken);
            string[] missingTables = RequiredEntityCascadeTables.Except(cascadeRules.Keys, StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] unsafeTables = cascadeRules
                .Where(pair => !pair.Value.Equals("CASCADE", StringComparison.OrdinalIgnoreCase))
                .Select(pair => $"{pair.Key} ({pair.Value})")
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (missingTables.Length > 0 || unsafeTables.Length > 0)
            {
                throw new InvalidOperationException(
                    "World import cannot safely replace entity rows. Missing entity cascade tables: "
                    + (missingTables.Length == 0 ? "none" : string.Join(", ", missingTables))
                    + "; non-CASCADE references: "
                    + (unsafeTables.Length == 0 ? "none" : string.Join(", ", unsafeTables))
                    + ".");
            }
        }

        private static async Task<object> ExecuteScalarAsync(
            DbConnection connection,
            string commandText,
            CancellationToken cancellationToken)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = commandText;
            object result = await command.ExecuteScalarAsync(cancellationToken);
            return result == DBNull.Value ? null : result;
        }

        private static async Task<List<string>> ReadFirstColumnAsync(
            DbConnection connection,
            string commandText,
            CancellationToken cancellationToken)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = commandText;
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            var values = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
                values.Add(reader.GetString(0));
            return values;
        }

        private static async Task<Dictionary<string, string>> ReadEntityDeleteRulesAsync(
            DbConnection connection,
            CancellationToken cancellationToken)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT TABLE_NAME, DELETE_RULE FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS "
                + "WHERE CONSTRAINT_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME = 'entity' "
                + "ORDER BY TABLE_NAME, CONSTRAINT_NAME;";
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            var rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken))
            {
                string tableName = reader.GetString(0);
                string deleteRule = reader.GetString(1);
                if (rules.TryGetValue(tableName, out string existingRule)
                    && !existingRule.Equals(deleteRule, StringComparison.OrdinalIgnoreCase))
                {
                    rules[tableName] = $"{existingRule}/{deleteRule}";
                }
                else
                {
                    rules[tableName] = deleteRule;
                }
            }

            return rules;
        }
    }
}
