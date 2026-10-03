using System.Globalization;
using System.Text;
using DDDToolkit.EntityFramework.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.EntityFramework.Postgres;

// What a script writes besides the policies: the privileges the policies ask for, the role the application's
// own bookkeeping runs as, and the guard of a table that only grows.
public static partial class PostgresRowAccess
{
    /// <summary>The commands a privilege is for, in the order a script grants them. <c>UPDATE</c> is granted per column, on a line of its own.</summary>
    private static readonly string[] TableCommands = ["SELECT", "INSERT", "DELETE"];

    /// <summary>The trigger function that keeps the rows of a table that only grows, as a statement names it.</summary>
    private const string KeptRowsFunction = "ddd.refuse_changes_to_kept_rows";

    /// <summary>The signature <see cref="KeptRowsFunction"/> is found by.</summary>
    private const string KeptRowsSignature = KeptRowsFunction + "()";

    /// <summary>The trigger on every row of such a table. Postgres names a trigger per table, so one name serves them all.</summary>
    private const string KeptRowsTrigger = "ddd_kept_rows";

    /// <summary>The trigger on <c>TRUNCATE</c> of such a table.</summary>
    private const string KeptRowsTruncateTrigger = "ddd_kept_rows_truncate";

    /// <summary>What a statement the guard refused is told.</summary>
    public const string KeptRowsRefusal = "A kept row does not change.";

    /// <summary>
    /// The SQLSTATE the guard refuses with: <c>object_not_in_prerequisite_state</c>, which is what a row that may
    /// not change yet is, and a code no constraint and no policy raises by itself.
    /// </summary>
    public const string KeptRowsSqlState = "55000";

    /// <summary>
    /// The body of <c>ddd.refuse_changes_to_kept_rows()</c>. An update and a truncate are always refused. A
    /// delete is let through only on a table that says how long it keeps a row, the trigger's first argument in
    /// seconds, and only once the row is older than that by the database's own clock; the second argument names
    /// the column a row's age is counted from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The column is read through <c>to_jsonb</c> because one function serves every such table, and a naming
    /// convention may have given the column another name on each. JSON writes a timestamp in ISO 8601 whatever
    /// the session's <c>DateStyle</c> says, so the cast back reads the same instant.
    /// </para>
    /// <para>
    /// <c>now()</c> is the start of the transaction, so a long transaction counts a row as a little younger than
    /// it is. That errs on the side of keeping.
    /// </para>
    /// <para>
    /// Every function and type name is qualified with <c>pg_catalog</c>: the function runs as whoever fires the
    /// trigger, with an empty search path, and Postgres still looks for a type name in the session's temporary
    /// schema first.
    /// </para>
    /// </remarks>
    private const string KeptRowsBody =
        "\n" +
        "BEGIN\n" +
        "    IF TG_OP = 'DELETE' AND TG_ARGV[0] <> '' THEN\n" +
        "        IF (pg_catalog.to_jsonb(OLD) ->> TG_ARGV[1])::pg_catalog.timestamptz\n" +
        "               < pg_catalog.now() - pg_catalog.make_interval(secs => TG_ARGV[0]::pg_catalog.float8) THEN\n" +
        "            RETURN OLD;\n" +
        "        END IF;\n" +
        "    END IF;\n" +
        "    RAISE EXCEPTION '" + KeptRowsRefusal + "' USING\n" +
        "        ERRCODE = '" + KeptRowsSqlState + "',\n" +
        "        DETAIL = pg_catalog.format('%s on %I.%I was refused: the table only grows.', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME);\n" +
        "END\n";

    /// <summary>What <see cref="KeptRowsBody"/> is the body of, up to the body.</summary>
    private const string KeptRowsHeader =
        "CREATE OR REPLACE FUNCTION " + KeptRowsSignature + " RETURNS trigger LANGUAGE plpgsql SET search_path = '' AS ";

    /// <summary>
    /// Whether a script has something to write for <paramref name="context"/> beyond what rules, access functions
    /// and contributions give it: a table that only grows, an event log, to guard, or, with
    /// <see cref="RowAccessExport.WriteGrants"/>, tables of the toolkit's own, an outbox, an inbox or an event
    /// log, to write the privileges of. The Supabase export asks it, so a module with neither a rule nor a
    /// contribution still gets its access file.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="export"/> is null.</exception>
    public static bool WritesFor(DbContext context, RowAccessExport export)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(export);

        var tables = ToolkitTablesOf(context);
        return tables.Any(table => table.Kind == ToolkitTableKind.Kept) || (export.WriteGrants && tables.Count > 0);
    }

    /// <summary>
    /// The commands the permissive policies of a script allow each role on each table, collected as the policies
    /// are written, so the privileges are read from the very policies the script holds and from nothing else.
    /// </summary>
    private sealed class Privileges
    {
        private readonly Dictionary<StoreObjectIdentifier, SortedDictionary<string, HashSet<string>>> _allowed = [];

        /// <summary>A permissive policy lets <paramref name="role"/> run <paramref name="command"/> on <paramref name="table"/>.</summary>
        public void Allow(StoreObjectIdentifier table, string command, string role)
        {
            if (!_allowed.TryGetValue(table, out var roles))
            {
                _allowed[table] = roles = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
            }

            if (!roles.TryGetValue(role, out var commands))
            {
                roles[role] = commands = new HashSet<string>(StringComparer.Ordinal);
            }

            commands.Add(command);
        }

        /// <summary>The roles a permissive policy on <paramref name="table"/> is for, in order, each with its commands.</summary>
        public IEnumerable<KeyValuePair<string, HashSet<string>>> On(StoreObjectIdentifier table)
            => _allowed.TryGetValue(table, out var roles) ? roles : [];

        /// <summary>The roles the policies let add, change or remove rows of some table.</summary>
        public IEnumerable<string> Writers
            => _allowed.Values.SelectMany(roles => roles.Where(role => role.Value.Overlaps(["INSERT", "UPDATE", "DELETE"])).Select(role => role.Key));
    }

    /// <summary>What a table of the toolkit's own is for.</summary>
    private enum ToolkitTableKind
    {
        /// <summary>The outbox: every save adds rows, and the bookkeeping reads, marks and deletes them.</summary>
        Outbox,

        /// <summary>The inbox: the handlers of integration events add rows, and the bookkeeping deletes old ones.</summary>
        Inbox,

        /// <summary>A table that only grows, an event log: every save adds rows, and nothing changes one.</summary>
        Kept,
    }

    /// <summary>A table of the toolkit's own in a context's model.</summary>
    /// <param name="Kind">What it is for.</param>
    /// <param name="Table">The table.</param>
    /// <param name="KeyColumns">The columns of its primary key.</param>
    /// <param name="RecordedAtColumn">For a table that only grows, the column a row's age is counted from.</param>
    /// <param name="KeepForSeconds">For a table that only grows, how long it keeps a row before it may be deleted; <see langword="null"/> for good.</param>
    private sealed record ToolkitTable(ToolkitTableKind Kind, StoreObjectIdentifier Table, IReadOnlyList<string> KeyColumns, string? RecordedAtColumn, long? KeepForSeconds);

    /// <summary>
    /// The outbox, the inbox and the tables that only grow of <paramref name="context"/>'s model, by schema and
    /// name. The first two are known by their classes, the last by the mark <c>AddEventLog</c> leaves.
    /// </summary>
    /// <exception cref="InvalidOperationException">A table marked as only growing has no primary key, or names no property a row's age is counted from.</exception>
    private static List<ToolkitTable> ToolkitTablesOf(DbContext context)
    {
        var tables = new List<ToolkitTable>();
        foreach (var entity in context.Model.GetEntityTypes().Where(entity => entity.GetTableName() is not null))
        {
            ToolkitTableKind? kind = KeptRows.IsAppendOnly(entity) ? ToolkitTableKind.Kept
                : entity.ClrType.FullName == KeptRows.OutboxClrType ? ToolkitTableKind.Outbox
                : entity.ClrType.FullName == KeptRows.InboxClrType ? ToolkitTableKind.Inbox
                : null;
            if (kind is null)
            {
                continue;
            }

            var table = TableOf(entity);
            List<string> key = [.. (entity.FindPrimaryKey()?.Properties ?? []).Select(property => property.GetColumnName(table)!)];
            var recordedAt = KeptRows.RecordedAtOf(entity)?.GetColumnName(table);
            if (kind == ToolkitTableKind.Kept && (key.Count == 0 || recordedAt is null))
            {
                throw new InvalidOperationException(
                    $"{entity.DisplayName()} is marked as a table that only grows, but it has no primary key or names no property a row's age is counted from. Map it with modelBuilder.AddEventLog(Database).");
            }

            tables.Add(new ToolkitTable(kind.Value, table, key, recordedAt, KeptRows.KeepForSecondsOf(entity)));
        }

        return [.. tables
            .OrderBy(each => each.Table.Schema ?? DefaultSchema, StringComparer.Ordinal)
            .ThenBy(each => each.Table.Name, StringComparer.Ordinal)];
    }

    /// <summary><c>ALTER TABLE … ENABLE ROW LEVEL SECURITY</c>, and <c>FORCE</c> on the line after it where the export asks for it.</summary>
    private static string EnableStatement(string qualified, bool force)
        => "ALTER TABLE " + qualified + " ENABLE ROW LEVEL SECURITY;\n"
           + (force ? "ALTER TABLE " + qualified + " FORCE ROW LEVEL SECURITY;\n" : "");

    /// <summary>
    /// The privileges of the tables <paramref name="prepared"/>'s script wrote policies for, read from those
    /// policies, and of the context's outbox, inbox and tables that only grow; see
    /// <see cref="RowAccessExport.WriteGrants"/>.
    /// </summary>
    /// <param name="prepared">The context and what its script is written from.</param>
    /// <param name="secured">The tables the script turned row level security on for.</param>
    /// <param name="roots">Those of them that are an aggregate's own table, which a refusal names before the tables of its entities.</param>
    /// <param name="privileges">What the script's permissive policies allow.</param>
    /// <param name="names">The roles of the script.</param>
    /// <param name="sql">How Postgres quotes a name.</param>
    /// <exception cref="InvalidOperationException">The policies let a role change or remove rows of a table it may not read.</exception>
    private static string PrivilegeStatements(
        Prepared prepared,
        IReadOnlySet<StoreObjectIdentifier> secured,
        IReadOnlySet<StoreObjectIdentifier> roots,
        Privileges privileges,
        RowAccessRoleNames names,
        ISqlGenerationHelper sql)
    {
        var context = prepared.Context;
        var system = names.System;
        List<StoreObjectIdentifier> covered = [.. secured.OrderBy(table => table.Schema ?? DefaultSchema, StringComparer.Ordinal).ThenBy(table => table.Name, StringComparer.Ordinal)];

        // Before anything is written, and an aggregate's own table before its entities': theirs follow it, so the
        // table a refusal names is the one whose rules to change.
        foreach (var table in covered.OrderBy(table => roots.Contains(table) ? 0 : 1))
        {
            foreach (var (role, commands) in privileges.On(table))
            {
                if ((commands.Contains("UPDATE") || commands.Contains("DELETE")) && !commands.Contains("SELECT"))
                {
                    throw new InvalidOperationException(
                        $"The policies let {Symbolic(role, names)} change or remove rows of {table.DisplayName()} and not read them. " +
                        "Entity Framework finds the row it changes by reading it, and Postgres asks the privilege to read for that, so such a rule could never run. " +
                        "Add a rule that lets that role read those rows, or take the rule that lets it write them away.");
                }
            }
        }

        // Everybody a privilege could have been given to before: the callers' roles, the bookkeeping role, and PUBLIC.
        List<string> callers = [.. new[] { names.Anonymous, names.User, names.SystemIn }.Concat(names.TokenDatabaseRoles).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        var revoked = "PUBLIC, " + string.Join(", ", (system is null ? callers : [.. callers.Append(system).Order(StringComparer.Ordinal)]).Select(role => sql.DelimitIdentifier(role)));

        var usage = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        void Uses(StoreObjectIdentifier table, string role)
        {
            var schema = table.Schema ?? DefaultSchema;
            if (!usage.TryGetValue(schema, out var roles))
            {
                usage[schema] = roles = new SortedSet<string>(StringComparer.Ordinal);
            }

            roles.Add(role);
        }

        var tables = new StringBuilder();
        foreach (var table in covered)
        {
            var qualified = sql.DelimitIdentifier(table.Name, table.Schema ?? DefaultSchema);
            tables.Append('\n').Append("REVOKE ALL ON TABLE ").Append(qualified).Append(" FROM ").Append(revoked).Append(";\n");

            var adding = new List<string>();
            foreach (var (role, commands) in privileges.On(table))
            {
                var identifier = sql.DelimitIdentifier(role);
                Uses(table, role);

                List<string> whole = [.. TableCommands.Where(commands.Contains)];
                if (whole.Count > 0)
                {
                    tables.Append("GRANT ").Append(string.Join(", ", whole)).Append(" ON TABLE ").Append(qualified).Append(" TO ").Append(identifier).Append(";\n");
                }

                if (commands.Contains("UPDATE") && UpdatableColumns(context, table) is { Count: > 0 } columns)
                {
                    tables.Append("GRANT UPDATE (").Append(string.Join(", ", columns.Select(column => sql.DelimitIdentifier(column))))
                        .Append(") ON TABLE ").Append(qualified).Append(" TO ").Append(identifier).Append(";\n");
                }

                if (commands.Contains("INSERT"))
                {
                    adding.Add(role);
                }
            }

            tables.Append(SequenceStatements(context, table, qualified, revoked, adding, sql));
        }

        // Whoever saves writes the event's rows in the same transaction: the user's role and the scoped system
        // role, and every role the policies let write a table here. Never the bookkeeping role, which saves nothing.
        List<string> saving = [.. new[] { names.User, names.SystemIn }.Concat(privileges.Writers)
            .Where(role => !string.Equals(role, system, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        List<ToolkitTable> own = [.. ToolkitTablesOf(context).Where(toolkit => !secured.Contains(toolkit.Table))];
        tables.Append(FormerHoldersStatement([.. own.Select(toolkit => sql.DelimitIdentifier(toolkit.Table.Name, toolkit.Table.Schema ?? DefaultSchema))]));

        foreach (var toolkit in own)
        {
            var table = toolkit.Table;
            var qualified = sql.DelimitIdentifier(table.Name, table.Schema ?? DefaultSchema);
            void Grant(string what, IReadOnlyList<string> roles)
            {
                foreach (var role in roles)
                {
                    Uses(table, role);
                }

                tables.Append("GRANT ").Append(what).Append(" ON TABLE ").Append(qualified).Append(" TO ").Append(string.Join(", ", roles.Select(role => sql.DelimitIdentifier(role)))).Append(";\n");
            }

            tables.Append('\n');
            switch (toolkit.Kind)
            {
                case ToolkitTableKind.Outbox:
                    tables.Append("-- The outbox takes a row from whoever saves, and nobody but the bookkeeping reads, marks or deletes one.\n")
                        .Append("REVOKE ALL ON TABLE ").Append(qualified).Append(" FROM ").Append(revoked).Append(";\n");
                    Grant("INSERT", saving);
                    if (system is not null)
                    {
                        // The bookkeeping says how a delivery went and no more: the event stays as it was raised.
                        Grant("SELECT, DELETE", [system]);
                        List<string> delivery = [.. EntitiesIn(context, table)
                            .SelectMany(entity => KeptRows.OutboxDeliveryProperties.Select(name => entity.FindProperty(name)?.GetColumnName(table)))
                            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
                        if (delivery.Count > 0)
                        {
                            Grant("UPDATE (" + string.Join(", ", delivery.Select(column => sql.DelimitIdentifier(column))) + ")", [system]);
                        }
                    }

                    break;

                case ToolkitTableKind.Inbox:
                    tables.Append("-- The inbox is written by the handlers of integration events, which run as the scoped system role.\n")
                        .Append("REVOKE ALL ON TABLE ").Append(qualified).Append(" FROM ").Append(revoked).Append(";\n");
                    Grant("SELECT, INSERT", [names.SystemIn]);
                    if (system is not null)
                    {
                        Grant("SELECT, INSERT, UPDATE, DELETE", [system]);
                    }

                    break;

                default:
                    tables.Append("-- A table that only grows takes a row from whoever saves; its guard refuses every change to one.\n")
                        .Append("REVOKE ALL ON TABLE ").Append(qualified).Append(" FROM ").Append(revoked).Append(";\n");
                    Grant("INSERT", saving);
                    if (system is not null && toolkit.KeepForSeconds is not null)
                    {
                        // What deleting the rows that are old enough asks: finding them by their age, and their key.
                        List<string> found = [.. toolkit.KeyColumns.Append(toolkit.RecordedAtColumn!).Distinct(StringComparer.Ordinal)];
                        Grant("SELECT (" + string.Join(", ", found.Select(column => sql.DelimitIdentifier(column))) + "), DELETE", [system]);
                    }

                    break;
            }
        }

        var statements = new StringBuilder()
            .Append('\n')
            .Append("-- Privileges, from the policies above: what a table gave the roles of this file before is taken back, and a").Append('\n')
            .Append("-- role then gets the commands a permissive policy allows it, and no more. UPDATE is granted on the columns").Append('\n')
            .Append("-- that may change.").Append('\n');
        foreach (var (schema, roles) in usage)
        {
            statements.Append("GRANT USAGE ON SCHEMA ").Append(sql.DelimitIdentifier(schema)).Append(" TO ").Append(string.Join(", ", roles.Select(role => sql.DelimitIdentifier(role)))).Append(";\n");
        }

        statements.Append(tables);
        if (system is not null)
        {
            statements.Append(MigrationHistoryStatement(context, system, sql));
        }

        return statements.ToString();
    }

    /// <summary>
    /// Takes back what a role no file names any more still holds on the toolkit's own tables,
    /// <paramref name="tables"/> as a statement names them: the bookkeeping role of an earlier file after
    /// <see cref="PostgresRowLevelSecurityOptions.SystemRole"/> changed, or the role of a token role since
    /// unmapped. Those tables have no policies, so a privilege is their only lock, and the role the application
    /// logs in as may still switch to a role it was once granted. Empty without such a table.
    /// </summary>
    /// <remarks>
    /// The holders are found where the script runs, in the tables' and their columns' privileges. Left alone
    /// are the tables' owner and every role that can log in, bypasses row level security or is a superuser: a
    /// script refuses a bookkeeping role, a scoped system role or the role of a token role that is one, so what
    /// such a role holds the host gave it, the role a host's own background work runs as for one. The roles this
    /// file names are revoked from by name on each table, whatever they are.
    /// </remarks>
    private static string FormerHoldersStatement(IReadOnlyList<string> tables)
    {
        if (tables.Count == 0)
        {
            return string.Empty;
        }

        return Blocks(1, new StringBuilder()
            .Append('\n')
            .Append("-- The toolkit's own tables have no policies, so a privilege is their only lock. What an earlier file gave a").Append('\n')
            .Append("-- role this one no longer names is taken back too: from every role that holds a privilege on them, cannot").Append('\n')
            .Append("-- log in and is held to row level security.").Append('\n')
            .Append("DO $ddd$").Append('\n')
            .Append("DECLARE").Append('\n')
            .Append("    held record;").Append('\n')
            .Append("BEGIN").Append('\n')
            .Append("    FOR held IN").Append('\n')
            .Append("        SELECT DISTINCT c.oid AS relation, acl.grantee").Append('\n')
            .Append("        FROM pg_catalog.pg_class c").Append('\n')
            .Append("        CROSS JOIN LATERAL (").Append('\n')
            .Append("            SELECT whole.grantee FROM pg_catalog.aclexplode(c.relacl) whole").Append('\n')
            .Append("            UNION ALL").Append('\n')
            .Append("            SELECT part.grantee FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) part").Append('\n')
            .Append("            WHERE a.attrelid = c.oid AND NOT a.attisdropped) acl").Append('\n')
            .Append("        JOIN pg_catalog.pg_roles holder ON holder.oid = acl.grantee").Append('\n')
            .Append("        WHERE c.oid IN (").Append(string.Join(", ", tables.Select(table => Literal(table) + "::pg_catalog.regclass"))).Append(')').Append('\n')
            .Append("          AND acl.grantee <> c.relowner").Append('\n')
            .Append("          AND NOT (holder.rolcanlogin OR holder.rolbypassrls OR holder.rolsuper)").Append('\n')
            .Append("    LOOP").Append('\n')
            .Append("        EXECUTE pg_catalog.format('REVOKE ALL ON TABLE %s FROM %s', held.relation::pg_catalog.regclass, held.grantee::pg_catalog.regrole);").Append('\n')
            .Append("    END LOOP;").Append('\n')
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .ToString());
    }

    /// <summary>
    /// The columns of <paramref name="table"/> an <c>UPDATE</c> may write, in order: every column a property of
    /// the entity types in the table is stored in, owned types and value objects stored inline included, but
    /// those of a key, those Entity Framework refuses or ignores a change of after the row was added, and those
    /// the database computes. That is Entity Framework's own notion, so what C# would not change the database
    /// does not let change either. A column of JSON is written as a whole.
    /// </summary>
    private static List<string> UpdatableColumns(DbContext context, StoreObjectIdentifier table)
    {
        var changing = new HashSet<string>(StringComparer.Ordinal);
        var kept = new HashSet<string>(StringComparer.Ordinal);

        void Collect(ITypeBase type)
        {
            if (type.IsMappedToJson())
            {
                if (type.GetContainerColumnName() is { } container)
                {
                    changing.Add(container);
                }

                return;
            }

            foreach (var property in type.GetProperties())
            {
                if (property.GetColumnName(table) is not { } column)
                {
                    continue;
                }

                var changes = !property.IsKey()
                    && property.GetAfterSaveBehavior() == PropertySaveBehavior.Save
                    && property.GetComputedColumnSql(table) is null;
                (changes ? changing : kept).Add(column);
            }

            foreach (var complex in type.GetComplexProperties())
            {
                Collect(complex.ComplexType);
            }
        }

        foreach (var entity in EntitiesIn(context, table))
        {
            Collect(entity);
        }

        // A column two properties share, a key an owned type stored inline has as well, changes only when both say so.
        return [.. changing.Except(kept, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>The entity types of <paramref name="context"/>'s model whose rows are in <paramref name="table"/>.</summary>
    private static IEnumerable<IEntityType> EntitiesIn(DbContext context, StoreObjectIdentifier table)
        => context.Model.GetEntityTypes().Where(entity => entity.GetTableName() is not null && TableOf(entity) == table);

    /// <summary>
    /// For a table with a whole-number column the database fills when a row is added: every privilege on the
    /// sequences its columns own taken back, and <c>USAGE</c> on them given to the roles that may add rows. A
    /// serial column takes its next value as the role that inserts; an identity column asks no privilege, and
    /// gets the same lines, which then change nothing. Empty for a table without such a column.
    /// </summary>
    /// <remarks>
    /// The sequences are found where the script runs: Postgres names them itself, after the table and the column
    /// and cut to fit, so the model does not know their names.
    /// </remarks>
    private static string SequenceStatements(DbContext context, StoreObjectIdentifier table, string qualified, string revoked, IReadOnlyList<string> adding, ISqlGenerationHelper sql)
    {
        var generated = EntitiesIn(context, table)
            .SelectMany(entity => entity.GetProperties())
            .Any(property => property.GetColumnName(table) is not null && property.ValueGenerated.HasFlag(ValueGenerated.OnAdd) && IsWholeNumber(property));
        if (!generated)
        {
            return string.Empty;
        }

        var block = new StringBuilder()
            .Append("DO $ddd$").Append('\n')
            .Append("DECLARE").Append('\n')
            .Append("    owned pg_catalog.regclass;").Append('\n')
            .Append("BEGIN").Append('\n')
            .Append("    FOR owned IN").Append('\n')
            .Append("        SELECT d.objid::pg_catalog.regclass").Append('\n')
            .Append("        FROM pg_catalog.pg_depend d").Append('\n')
            .Append("        JOIN pg_catalog.pg_class s ON s.oid = d.objid AND s.relkind = 'S'").Append('\n')
            .Append("        WHERE d.classid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.refclassid = 'pg_catalog.pg_class'::pg_catalog.regclass").Append('\n')
            .Append("          AND d.refobjid = ").Append(Literal(qualified)).Append("::pg_catalog.regclass AND d.deptype IN ('a', 'i')").Append('\n')
            .Append("    LOOP").Append('\n')
            .Append("        EXECUTE pg_catalog.format('REVOKE ALL ON SEQUENCE %s FROM %s', owned, ").Append(Literal(revoked)).Append(");").Append('\n');
        if (adding.Count > 0)
        {
            block.Append("        EXECUTE pg_catalog.format('GRANT USAGE ON SEQUENCE %s TO %s', owned, ")
                .Append(Literal(string.Join(", ", adding.Select(role => sql.DelimitIdentifier(role))))).Append(");").Append('\n');
        }

        return Blocks(1, block
            .Append("    END LOOP;").Append('\n')
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .ToString());
    }

    /// <summary>Whether <paramref name="property"/> is stored as a whole number, which is what a sequence fills.</summary>
    private static bool IsWholeNumber(IProperty property)
    {
        var stored = property.GetTypeMapping().Converter?.ProviderClrType ?? property.ClrType;
        stored = Nullable.GetUnderlyingType(stored) ?? stored;
        return stored == typeof(int) || stored == typeof(long) || stored == typeof(short);
    }

    /// <summary>
    /// Lets the bookkeeping role read the context's migration history, where the database has one: a database
    /// made from the model alone has none, and several contexts usually share one.
    /// </summary>
    private static string MigrationHistoryStatement(DbContext context, string system, ISqlGenerationHelper sql)
    {
        var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
        var schema = relational.MigrationsHistoryTableSchema ?? DefaultSchema;
        var history = sql.DelimitIdentifier(relational.MigrationsHistoryTableName ?? HistoryTableName, schema);
        var role = sql.DelimitIdentifier(system);

        return Blocks(1, new StringBuilder()
            .Append('\n')
            .Append("-- The bookkeeping role reads which migrations ran, where the database keeps a history of them.").Append('\n')
            .Append("DO $ddd$").Append('\n')
            .Append("BEGIN").Append('\n')
            .Append("    IF pg_catalog.to_regclass(").Append(Literal(history)).Append(") IS NOT NULL THEN").Append('\n')
            .Append("        GRANT USAGE ON SCHEMA ").Append(sql.DelimitIdentifier(schema)).Append(" TO ").Append(role).Append(';').Append('\n')
            .Append("        GRANT SELECT ON TABLE ").Append(history).Append(" TO ").Append(role).Append(';').Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .ToString());
    }

    /// <summary>The table Entity Framework keeps its migration history in unless the context's options name another.</summary>
    private const string HistoryTableName = "__EFMigrationsHistory";

    /// <summary>
    /// The guard of the context's tables that only grow, an event log: the trigger function
    /// <c>ddd.refuse_changes_to_kept_rows()</c>, and on each such table a trigger before every <c>UPDATE</c> and
    /// <c>DELETE</c> of a row and one before <c>TRUNCATE</c>. Empty for a context without such a table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An update and a truncate are refused, and so is a delete, unless the table says how long it keeps a row
    /// and the row is older than that by the database's clock. That holds for every role: a trigger fires for
    /// the table's owner and for a superuser too, where a privilege or a policy would stop neither. Both
    /// triggers are made to fire always, so a session in replication mode, which skips ordinary triggers, does
    /// not get past them. A refusal is SQLSTATE <see cref="KeptRowsSqlState"/> with the message
    /// <see cref="KeptRowsRefusal"/>.
    /// </para>
    /// <para>
    /// The statements can run again, and every script ends with them, so the guard follows the model: a table
    /// that keeps its rows for longer or shorter than before gets its new period with the next script. The
    /// function is made or replaced only when it is missing or differs, so a role that does not own it may run
    /// the script once somebody else has. It needs Postgres 14 or later, which replaces a trigger in place.
    /// </para>
    /// </remarks>
    private static string KeptRowsStatements(DbContext context, ISqlGenerationHelper sql)
    {
        List<ToolkitTable> kept = [.. ToolkitTablesOf(context).Where(table => table.Kind == ToolkitTableKind.Kept)];
        if (kept.Count == 0)
        {
            return string.Empty;
        }

        // Left executable by PUBLIC: Postgres calls a trigger function as a trigger only, so that gives nobody
        // anything, and whoever makes a trigger with it has to be allowed to execute it.
        var statements = new StringBuilder(Blocks(1, new StringBuilder()
            .Append('\n')
            .Append("-- The rows of a table that only grows do not change. An update and a truncate are refused for every role,").Append('\n')
            .Append("-- the table's owner and a superuser included, and so is a delete, until a row is older than its table keeps it.").Append('\n')
            .Append("DO $ddd$").Append('\n')
            .Append("DECLARE").Append('\n')
            .Append("    body constant text := $function$").Append(KeptRowsBody).Append("$function$;").Append('\n')
            .Append("BEGIN").Append('\n')
            .Append("    IF pg_catalog.to_regnamespace('ddd') IS NULL THEN").Append('\n')
            .Append("        CREATE SCHEMA ddd;").Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("    IF NOT EXISTS (SELECT FROM pg_catalog.pg_proc").Append('\n')
            .Append("                   WHERE oid = pg_catalog.to_regprocedure(").Append(Literal(KeptRowsSignature)).Append(") AND prosrc = body) THEN").Append('\n')
            .Append("        EXECUTE ").Append(Literal(KeptRowsHeader)).Append(" || pg_catalog.quote_literal(body);").Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .ToString()));

        foreach (var table in kept)
        {
            var qualified = sql.DelimitIdentifier(table.Table.Name, table.Table.Schema ?? DefaultSchema);
            var keepFor = table.KeepForSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

            statements
                .Append("CREATE OR REPLACE TRIGGER ").Append(KeptRowsTrigger).Append(" BEFORE UPDATE OR DELETE ON ").Append(qualified).Append('\n')
                .Append("    FOR EACH ROW EXECUTE FUNCTION ").Append(KeptRowsFunction).Append('(').Append(Literal(keepFor)).Append(", ").Append(Literal(table.RecordedAtColumn!)).Append(");").Append('\n')
                .Append("ALTER TABLE ").Append(qualified).Append(" ENABLE ALWAYS TRIGGER ").Append(KeptRowsTrigger).Append(';').Append('\n')
                .Append("CREATE OR REPLACE TRIGGER ").Append(KeptRowsTruncateTrigger).Append(" BEFORE TRUNCATE ON ").Append(qualified).Append('\n')
                .Append("    FOR EACH STATEMENT EXECUTE FUNCTION ").Append(KeptRowsFunction).Append("();").Append('\n')
                .Append("ALTER TABLE ").Append(qualified).Append(" ENABLE ALWAYS TRIGGER ").Append(KeptRowsTruncateTrigger).Append(';').Append('\n');
        }

        return statements.ToString();
    }
}
