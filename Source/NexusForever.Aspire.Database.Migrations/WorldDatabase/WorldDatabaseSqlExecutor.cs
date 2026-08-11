using System.Data.Common;

namespace NexusForever.Aspire.Database.Migrations.WorldDatabase
{
    public static class WorldDatabaseSqlExecutor
    {
        public static async Task ExecuteAsync(
            DbConnection connection,
            DbTransaction transaction,
            string sql,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(sql);
            if (commandTimeoutSeconds < 1)
                throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = commandTimeoutSeconds;
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
