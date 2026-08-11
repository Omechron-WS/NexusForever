using System.Data;
using System.Data.Common;
using NexusForever.Aspire.Database.Migrations.WorldDatabase;

namespace NexusForever.Aspire.Database.Migrations.Tests
{
    public sealed class WorldDatabaseSqlExecutorTests
    {
        [Fact]
        public async Task ExecutePreservesLiteralBracesAndBindsTransactionAndTimeout()
        {
            const string sql = "INSERT INTO `example` (`json`, `text`) VALUES ('{\"value\": 1}', '{{literal}} {0}'); -- {comment}";
            var connection = new RecordingDbConnection();
            var transaction = new RecordingDbTransaction(connection);
            using var cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            await WorldDatabaseSqlExecutor.ExecuteAsync(
                connection,
                transaction,
                sql,
                417,
                cancellationToken);

            RecordingDbCommand command = Assert.Single(connection.Commands);
            Assert.Same(sql, command.CommandText);
            Assert.Same(transaction, command.Transaction);
            Assert.Equal(417, command.CommandTimeout);
            Assert.Equal(1, command.ExecuteCount);
            Assert.Equal(cancellationToken, command.CancellationToken);
        }

        private sealed class RecordingDbConnection : DbConnection
        {
            public List<RecordingDbCommand> Commands { get; } = [];

            public override string ConnectionString { get; set; }
            public override string Database => "worlddb";
            public override string DataSource => "test";
            public override string ServerVersion => "test";
            public override ConnectionState State => ConnectionState.Open;

            public override void ChangeDatabase(string databaseName)
            {
            }

            public override void Close()
            {
            }

            public override void Open()
            {
            }

            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            {
                return new RecordingDbTransaction(this);
            }

            protected override DbCommand CreateDbCommand()
            {
                var command = new RecordingDbCommand
                {
                    Connection = this
                };
                Commands.Add(command);
                return command;
            }
        }

        private sealed class RecordingDbTransaction : DbTransaction
        {
            private readonly DbConnection _connection;

            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            protected override DbConnection DbConnection => _connection;

            public RecordingDbTransaction(DbConnection connection)
            {
                _connection = connection;
            }

            public override void Commit()
            {
            }

            public override void Rollback()
            {
            }
        }

        private sealed class RecordingDbCommand : DbCommand
        {
            public int ExecuteCount { get; private set; }
            public CancellationToken CancellationToken { get; private set; }

            public override string CommandText { get; set; }
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection DbConnection { get; set; }
            protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
            protected override DbTransaction DbTransaction { get; set; }

            public override void Cancel()
            {
            }

            public override int ExecuteNonQuery()
            {
                ExecuteCount++;
                return 0;
            }

            public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            {
                ExecuteCount++;
                CancellationToken = cancellationToken;
                return Task.FromResult(0);
            }

            public override object ExecuteScalar()
            {
                throw new NotSupportedException();
            }

            public override void Prepare()
            {
            }

            protected override DbParameter CreateDbParameter()
            {
                throw new NotSupportedException();
            }

            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                throw new NotSupportedException();
            }
        }
    }
}
