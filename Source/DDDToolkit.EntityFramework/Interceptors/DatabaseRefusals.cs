using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Reads what a database refused in a failed save and makes the refusal the caller gets of it: the refusal a
/// unique index declares, or <see cref="ToolkitRefusals.Refused"/> for a row a policy denied. Both interceptors
/// that answer a failed save, <see cref="DatabaseRefusalInterceptor"/> and <see cref="AggregateVersionInterceptor"/>,
/// work through this one place, so a policy's denial is logged and phrased the same whichever way it showed.
/// </summary>
internal static class DatabaseRefusals
{
    private const string UniqueViolation = "23505";
    private const string InsufficientPrivilege = "42501";
    private const int SqliteConstraintUnique = 2067;
    private const int SqlServerDuplicateIndexRow = 2601;
    private const int SqlServerDuplicateConstraintKey = 2627;

    /// <summary>The routine of Postgres that checks a new row against the policies, which the error names in any language.</summary>
    private const string PolicyCheckRoutine = "ExecWithCheckOptions";

    private const string PolicyWords = "row-level security";

    private static readonly MethodInfo PropertyMethod
        = typeof(EF).GetMethod(nameof(EF.Property), BindingFlags.Public | BindingFlags.Static)!;

    private static readonly MethodInfo ObjectEqualsMethod
        = typeof(object).GetMethod(nameof(object.Equals), BindingFlags.Public | BindingFlags.Static, [typeof(object), typeof(object)])!;

    private static readonly MethodInfo StoredTokensMethod
        = typeof(DatabaseRefusals).GetMethod(nameof(StoredTokens), BindingFlags.NonPublic | BindingFlags.Static)!;

    // ---------------------------------------------------------------- reading the failure

    /// <summary>What the database refused in <paramref name="failure"/>, or <see langword="null"/>.</summary>
    public static DatabaseRefusal? Read(Exception failure)
    {
        if (InnermostDatabaseFailure(failure) is not { } database)
        {
            return null;
        }

        if (IsA(database, "Npgsql.PostgresException"))
        {
            return ReadPostgres(database);
        }

        if (IsA(database, "Microsoft.Data.Sqlite.SqliteException"))
        {
            return ReadSqlite(database);
        }

        if (IsA(database, "Microsoft.Data.SqlClient.SqlException") || IsA(database, "System.Data.SqlClient.SqlException"))
        {
            return ReadSqlServer(database);
        }

        return null;
    }

    private static DatabaseRefusal? ReadPostgres(DbException failure)
    {
        switch (failure.SqlState)
        {
            case UniqueViolation:
                return new DatabaseRefusal(DatabaseRefusalKind.DuplicateKey, Text(failure, "ConstraintName"), Text(failure, "TableName"), [])
                {
                    Schema = Text(failure, "SchemaName"),
                };

            case InsufficientPrivilege:
                // A missing privilege is 42501 too, and is the application's own set-up, not a rule that refused.
                var message = Text(failure, "MessageText") ?? failure.Message;
                return Text(failure, "Routine") == PolicyCheckRoutine || message.Contains(PolicyWords, StringComparison.Ordinal)
                    ? new DatabaseRefusal(DatabaseRefusalKind.PolicyDenied, Constraint: null, LastQuoted(message, '"', '"'), [])
                    : null;

            default:
                return null;
        }
    }

    private static DatabaseRefusal? ReadSqlite(DbException failure)
    {
        if (Value(failure, "SqliteExtendedErrorCode") is not SqliteConstraintUnique)
        {
            return null;
        }

        // "SQLite Error 19: 'UNIQUE constraint failed: Projects.TenantId, Projects.Number'." for an index over
        // columns, and "... failed: index 'IX_Name'" for one over expressions.
        const string Lead = "UNIQUE constraint failed: ";
        var message = failure.Message;
        var start = message.IndexOf(Lead, StringComparison.Ordinal);
        if (start < 0)
        {
            return new DatabaseRefusal(DatabaseRefusalKind.DuplicateKey, null, null, []);
        }

        var named = message[(start + Lead.Length)..].TrimEnd('.').TrimEnd('\'');
        if (named.StartsWith("index '", StringComparison.Ordinal))
        {
            return new DatabaseRefusal(DatabaseRefusalKind.DuplicateKey, named["index '".Length..].TrimEnd('\''), null, []);
        }

        string? table = null;
        var columns = new List<string>();
        foreach (var part in named.Split(", "))
        {
            var dot = part.IndexOf('.', StringComparison.Ordinal);
            if (dot <= 0 || (table is not null && !string.Equals(table, part[..dot], StringComparison.Ordinal)))
            {
                return new DatabaseRefusal(DatabaseRefusalKind.DuplicateKey, null, null, []);
            }

            table = part[..dot];
            columns.Add(part[(dot + 1)..]);
        }

        return new DatabaseRefusal(DatabaseRefusalKind.DuplicateKey, null, table, columns);
    }

    private static DatabaseRefusal? ReadSqlServer(DbException failure)
    {
        // 2601: "Cannot insert duplicate key row in object 'dbo.Projects' with unique index 'IX_Name'. ..."
        // 2627: "Violation of UNIQUE KEY constraint 'AK_Name'. Cannot insert duplicate key in object 'dbo.Projects'. ..."
        var lead = Value(failure, "Number") switch
        {
            SqlServerDuplicateIndexRow => "unique index '",
            SqlServerDuplicateConstraintKey => "constraint '",
            _ => null,
        };
        if (lead is null)
        {
            return null;
        }

        var message = failure.Message;
        var named = Between(message, "object '", "'");
        var dot = named?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        return new DatabaseRefusal(DatabaseRefusalKind.DuplicateKey, Between(message, lead, "'"), dot < 0 ? named : named![(dot + 1)..], [])
        {
            Schema = dot <= 0 ? null : named![..dot],
        };
    }

    /// <summary>The database exception deepest in <paramref name="failure"/>: the one the provider threw.</summary>
    private static DbException? InnermostDatabaseFailure(Exception? failure)
    {
        DbException? found = null;
        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current is DbException database)
            {
                found = database;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    found = InnermostDatabaseFailure(inner) ?? found;
                }

                break;
            }
        }

        return found;
    }

    /// <summary>Whether <paramref name="failure"/> is the provider's exception type named <paramref name="typeName"/>, or derives from it.</summary>
    private static bool IsA(DbException failure, string typeName)
    {
        for (var type = failure.GetType(); type is not null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, typeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Text(DbException failure, string property)
        => failure.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(failure) as string;

    private static int? Value(DbException failure, string property)
        => failure.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(failure) as int?;

    private static string? Between(string text, string lead, string end)
    {
        var start = text.IndexOf(lead, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += lead.Length;
        var stop = text.IndexOf(end, start, StringComparison.Ordinal);
        return stop < 0 ? null : text[start..stop];
    }

    private static string? LastQuoted(string text, char open, char close)
    {
        var stop = text.LastIndexOf(close);
        var start = stop <= 0 ? -1 : text.LastIndexOf(open, stop - 1);
        return start < 0 ? null : text[(start + 1)..stop];
    }

    // ---------------------------------------------------------------- the refusal of a failed save

    /// <summary>
    /// The refusal <paramref name="failure"/> stands for, with the failure as its inner exception: what the unique
    /// index it broke declares, or <see cref="ToolkitRefusals.Refused"/> for a row a policy denied, which is also
    /// logged. <see langword="null"/> for anything else, an unmarked index among them.
    /// </summary>
    public static RefusalException? Translate(DbContext context, Exception failure)
    {
        // A refusal or a conflict somebody made already is theirs.
        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current is RefusalException or ConcurrencyConflictException)
            {
                return null;
            }
        }

        if (Read(failure) is not { } refused)
        {
            return null;
        }

        switch (refused.Kind)
        {
            case DatabaseRefusalKind.PolicyDenied:
                LogDisagreement(context, DeniedTable(context, refused, failure), failure);
                return ToolkitRefusals.Of(ToolkitRefusals.Refused, failure);

            case DatabaseRefusalKind.DuplicateKey:
                return FindIndex(context.Model, refused) is { } index && IndexRefusal.Of(index) is { } declared
                    ? Declared(context, index, declared, failure)
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// The table a policy denied a row of, for the warning: with its schema, as the model names it, where the
    /// table Postgres quoted is one the failed save wrote, and as Postgres quoted it otherwise.
    /// </summary>
    private static string DeniedTable(DbContext context, DatabaseRefusal refused, Exception failure)
    {
        if (refused.Table is not { } table)
        {
            return "a table it did not name";
        }

        var written = FailedEntries(failure)
            .Select(entry => entry.Metadata)
            .Concat(context.Model.GetEntityTypes())
            .FirstOrDefault(entityType => string.Equals(entityType.GetTableName(), table, StringComparison.Ordinal));
        return written?.GetSchemaQualifiedTableName() ?? table;
    }

    /// <summary>
    /// The unique index of <paramref name="model"/> the database named: by its name, where the database gives one,
    /// and by its table and columns where it does not. <see langword="null"/> when the model has none, or has
    /// several the database's words fit that do not all refuse alike: SQLite cannot tell two unique indexes over
    /// the same columns apart.
    /// </summary>
    private static IReadOnlyIndex? FindIndex(IModel model, DatabaseRefusal refused)
    {
        var found = new List<IIndex>();
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() is not { } table
                || (refused.Table is not null && !string.Equals(refused.Table, table, StringComparison.Ordinal))
                || (refused.Schema is not null && entityType.GetSchema() is { } schema && !string.Equals(refused.Schema, schema, StringComparison.Ordinal)))
            {
                continue;
            }

            var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
            foreach (var index in entityType.GetDeclaredIndexes())
            {
                if (!index.IsUnique)
                {
                    continue;
                }

                var fits = refused.Constraint is not null
                    ? string.Equals(index.GetDatabaseName(store), refused.Constraint, StringComparison.Ordinal)
                    : refused.Table is not null
                      && refused.Columns.Count > 0
                      && index.Properties.Select(property => property.GetColumnName(store)).SequenceEqual(refused.Columns, StringComparer.Ordinal);
                if (fits)
                {
                    found.Add(index);
                }
            }
        }

        if (found.Count == 0)
        {
            return null;
        }

        var first = IndexRefusal.Of(found[0]);
        return found.Skip(1).All(index => IndexRefusal.Of(index) == first) ? found[0] : null;
    }

    /// <summary>The refusal <paramref name="index"/> declares, filled from the row that broke it where the save names it.</summary>
    private static RefusalException Declared(DbContext context, IReadOnlyIndex index, IndexRefusal declared, Exception failure)
    {
        var arguments = Arguments(context, index, RefusalTemplate.Placeholders(declared.Message), failure);
        return new RefusalException(declared.Code, declared.Kind, RefusalTemplate.Fill(declared.Message, arguments), arguments, failure);
    }

    /// <summary>
    /// What the message names, by property, from the row of the failed save that broke <paramref name="index"/>:
    /// the one row of the index's entity type the failure names that the save adds, or changes in a property of
    /// the index. When there are several, their values count if they agree; when they differ, the database did not
    /// say which one it refused, and nothing is filled in.
    /// </summary>
    private static Dictionary<string, object?> Arguments(DbContext context, IReadOnlyIndex index, IReadOnlyList<string> names, Exception failure)
    {
        var none = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return none;
        }

        var rows = Written(FailedEntries(failure), index);
        if (rows.Count == 0)
        {
            // The failure named no row: look among everything the context was saving.
            rows = Written(context.ChangeTracker.Entries(), index);
        }

        Dictionary<string, object?>? agreed = null;
        foreach (var row in rows)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                if (IndexRefusal.PropertyNamed(row.Metadata, name) is IProperty property)
                {
                    values[name] = Stored(property, row.Property(property.Name).CurrentValue);
                }
            }

            if (agreed is null)
            {
                agreed = values;
            }
            else if (agreed.Count != values.Count || agreed.Any(pair => !values.TryGetValue(pair.Key, out var other) || !Equals(pair.Value, other)))
            {
                return none;
            }
        }

        return agreed ?? none;
    }

    /// <summary>The entries the failed save names, from the outermost exception that names any.</summary>
    private static IReadOnlyList<EntityEntry> FailedEntries(Exception failure)
    {
        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateException { Entries.Count: > 0 } update)
            {
                return update.Entries;
            }
        }

        return [];
    }

    /// <summary>
    /// The rows among <paramref name="entries"/> that can have broken <paramref name="index"/>: of its entity type,
    /// and added, or changed in a property of the index, or changed at all when the index has a filter, since a
    /// filter's columns are the database's to know.
    /// </summary>
    private static List<EntityEntry> Written(IEnumerable<EntityEntry> entries, IReadOnlyIndex index)
        => [.. entries.Where(entry => index.DeclaringEntityType.IsAssignableFrom(entry.Metadata)
            && (entry.State == EntityState.Added
                || (entry.State == EntityState.Modified
                    && (index.GetFilter() is not null || index.Properties.Any(property => entry.Property(property.Name).IsModified)))))];

    /// <summary>The value as the property's converter stores it, so an id or a value object is the plain value underneath.</summary>
    private static object? Stored(IProperty property, object? value)
        => value is not null && property.GetTypeMapping().Converter is { } converter ? converter.ConvertToProvider(value) : value;

    // ---------------------------------------------------------------- a save that found no row

    /// <summary>
    /// Whether a save that found no row to change was denied rather than too late, and the refusal if so. Each of
    /// <paramref name="rows"/> is read again by its key, as the same caller and through the context's filters,
    /// outside the failed save: when every one is still there with the concurrency tokens the save sent, nobody
    /// changed it, so a policy hid it from the statement, and that is <see cref="ToolkitRefusals.Refused"/>, logged
    /// as a warning, with <paramref name="conflict"/> as its inner exception. A row that is gone, hidden, or
    /// changed leaves the conflict a conflict, and so does a read that fails.
    /// </summary>
    public static RefusalException? DeniedNotLate(DbContext context, IReadOnlyList<EntityEntry> rows, Exception conflict)
    {
        try
        {
            if (rows.Count == 0)
            {
                return null;
            }

            foreach (var row in rows)
            {
                if (Tokens(row) is not { } tokens || !Unchanged(row, tokens, Query(context, row, tokens)?.FirstOrDefault()))
                {
                    return null;
                }
            }
        }
        catch (Exception)
        {
            // The read itself failed, so nothing is known beyond the conflict it was about: that stands.
            return null;
        }

        return Denied(context, rows, conflict);
    }

    /// <inheritdoc cref="DeniedNotLate"/>
    public static async Task<RefusalException?> DeniedNotLateAsync(DbContext context, IReadOnlyList<EntityEntry> rows, Exception conflict, CancellationToken cancellationToken)
    {
        try
        {
            if (rows.Count == 0)
            {
                return null;
            }

            foreach (var row in rows)
            {
                if (Tokens(row) is not { } tokens || Query(context, row, tokens) is not { } query
                    || !Unchanged(row, tokens, await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)))
                {
                    return null;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        return Denied(context, rows, conflict);
    }

    private static RefusalException Denied(DbContext context, IReadOnlyList<EntityEntry> rows, Exception conflict)
    {
        LogDisagreement(context, string.Join(", ", rows.Select(row => row.Metadata.GetSchemaQualifiedTableName() ?? row.Metadata.DisplayName()).Distinct(StringComparer.Ordinal)), conflict);
        return ToolkitRefusals.Of(ToolkitRefusals.Refused, conflict);
    }

    /// <summary>The concurrency tokens of a row that has a key to be read by, or <see langword="null"/> when it has neither.</summary>
    private static List<IProperty>? Tokens(EntityEntry row)
    {
        if (row.Metadata.FindPrimaryKey() is null)
        {
            return null;
        }

        var tokens = row.Metadata.GetProperties().Where(static property => property.IsConcurrencyToken).ToList();
        return tokens.Count == 0 ? null : tokens;
    }

    /// <summary>Whether the row was found and holds, in every token, the value the save compared with.</summary>
    private static bool Unchanged(EntityEntry row, List<IProperty> tokens, object?[]? stored)
    {
        if (stored is null)
        {
            return false;
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            if (!tokens[i].GetValueComparer().Equals(stored[i], row.Property(tokens[i].Name).OriginalValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The query that reads the tokens of <paramref name="row"/> as the database holds them now, by its key.</summary>
    private static IQueryable<object?[]>? Query(DbContext context, EntityEntry row, List<IProperty> tokens)
    {
        var key = row.Metadata.FindPrimaryKey()!.Properties;
        var values = new KeyValue[key.Count];
        for (var i = 0; i < key.Count; i++)
        {
            if (row.Property(key[i].Name).CurrentValue is not { } value)
            {
                return null;
            }

            values[i] = new KeyValue(value);
        }

        return (IQueryable<object?[]>)StoredTokensMethod.MakeGenericMethod(row.Metadata.ClrType).Invoke(null, [context, row.Metadata, key, values, tokens])!;
    }

    /// <summary>
    /// Reads the tokens of one row of <typeparamref name="TEntity"/>: through the context's set, so its query
    /// filters apply and the caller is whoever the context runs as, without tracking, and with the key as
    /// parameters, so the statement is one the database has seen before.
    /// </summary>
    private static IQueryable<object?[]> StoredTokens<TEntity>(DbContext context, IEntityType entityType, IReadOnlyList<IProperty> key, KeyValue[] values, List<IProperty> tokens)
        where TEntity : class
    {
        var row = Expression.Parameter(typeof(TEntity), "row");

        Expression? isTheRow = null;
        for (var i = 0; i < key.Count; i++)
        {
            var same = Expression.Call(
                ObjectEqualsMethod,
                Expression.Call(PropertyMethod.MakeGenericMethod(typeof(object)), row, Expression.Constant(key[i].Name)),
                Expression.Property(Expression.Constant(values[i]), nameof(KeyValue.Value)));
            isTheRow = isTheRow is null ? same : Expression.AndAlso(isTheRow, same);
        }

        var read = Expression.NewArrayInit(
            typeof(object),
            tokens.Select(token => Expression.Call(PropertyMethod.MakeGenericMethod(typeof(object)), row, Expression.Constant(token.Name))));

        var set = entityType.HasSharedClrType ? context.Set<TEntity>(entityType.Name) : context.Set<TEntity>();
        return set.AsNoTracking()
            .Where(Expression.Lambda<Func<TEntity, bool>>(isTheRow!, row))
            .Select(Expression.Lambda<Func<TEntity, object?[]>>(read, row));
    }

    /// <summary>Holds one key value, so the query reads it as a parameter rather than as a constant of its own.</summary>
    private sealed class KeyValue(object value)
    {
        public object Value { get; } = value;
    }

    // ---------------------------------------------------------------- the warning

    /// <summary>
    /// Says, as a warning, that the database refused what the application allowed. A policy that refuses a save
    /// means a rule is written in two places that do not agree, or a rule the application never asked: either
    /// way somebody should hear of it, while the caller is answered with a plain refusal.
    /// </summary>
    private static void LogDisagreement(DbContext context, string table, Exception failure)
    {
        var logger = context.GetService<ILoggerFactory>().CreateLogger<DatabaseRefusalInterceptor>();
        logger.LogWarning(failure, "The database refused a save the application allowed: {Table}. C# and the policies disagree.", table);
    }
}
