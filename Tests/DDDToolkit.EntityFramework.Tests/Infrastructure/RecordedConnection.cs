using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A connection to nothing, which keeps every statement sent down it with the values of its parameters: what an
/// interceptor would have set on a real one, read without a database.
/// </summary>
public sealed class RecordedConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Open;

    /// <summary>Every statement sent, in order, with its parameters' values.</summary>
    public List<(string Sql, IReadOnlyList<string?> Parameters)> Statements { get; } = [];

    [AllowNull]
    public override string ConnectionString { get; set; } = "Host=recorded.invalid;Database=recorded";

    public override string Database => "recorded";

    public override string DataSource => "recorded.invalid";

    public override string ServerVersion => "17.0";

    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName)
    {
    }

    public override void Close() => _state = ConnectionState.Closed;

    public override void Open() => _state = ConnectionState.Open;

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException("A recorded connection has no transactions.");

    protected override DbCommand CreateDbCommand() => new RecordedCommand(this);

    /// <summary>A command that runs nothing and tells its connection what it was asked to run.</summary>
    private sealed class RecordedCommand(RecordedConnection connection) : DbCommand
    {
        /// <summary>Npgsql's own parameters, so a value is kept the way the real command would have taken it.</summary>
        private readonly NpgsqlCommand _parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = "";

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; } = connection;

        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            connection.Statements.Add((CommandText, [.. _parameters.Parameters.Select(parameter => parameter.Value as string)]));
            return -1;
        }

        public override object? ExecuteScalar()
        {
            ExecuteNonQuery();
            return null;
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => _parameters.CreateParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => throw new NotSupportedException("A recorded connection answers nothing.");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _parameters.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
