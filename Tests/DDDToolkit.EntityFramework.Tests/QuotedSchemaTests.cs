using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// A module in a schema Postgres keeps only in double quotes, one with capitals, an underscore or a word of its own:
/// every name a script writes in that schema is delimited as Entity Framework delimits the schema it creates, so the
/// functions, policies, triggers and grants are where the tables are, and a lookup by name compares the names the
/// catalog keeps, capitals and all. A schema Postgres keeps as SQL writes it is written as it always was. None of this
/// opens a database; <see cref="QuotedSchemaPostgresTests"/> runs the scripts.
/// </summary>
public sealed class QuotedSchemaTests : IDisposable
{
    private static readonly RowAccessFunction MemoMembers = RowAccessFunction.For<Memo>("memos/is_member", "TRUE");

    private static readonly RowAccessRule MembersReadMemos = RowAccessRule.For<Memo>("Members read memos", RowOperations.Read, "{call:memos/is_member}");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ddd-quoted-schema-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void A_function_named_relative_to_its_module_is_made_in_a_schema_with_capitals_with_the_schema_quoted()
    {
        using var capitalized = CapitalizedMemosContext.Create();

        var script = PostgresRowAccess.Script(capitalized, [MembersReadMemos], [MemoMembers]);

        script.Should().Contain("CREATE OR REPLACE FUNCTION \"Memos\".is_member(uuid) RETURNS boolean", "Entity Framework created the schema as \"Memos\", and Memos without quotes would be memos");
        script.Should().Contain("COMMENT ON FUNCTION \"Memos\".is_member(uuid) IS 'DDDToolkit access function of CapitalizedMemosContext';");
        script.Should().Contain("REVOKE ALL ON FUNCTION \"Memos\".is_member(uuid) FROM PUBLIC;");
        script.Should().Contain("GRANT EXECUTE ON FUNCTION \"Memos\".is_member(uuid) TO anon, authenticated;", "the policies that ask it are for both roles");
        script.Should().Contain("ALTER TABLE \"Memos\".\"Memos\" ENABLE ROW LEVEL SECURITY;");
        script.Should().Contain("CREATE POLICY \"Members read memos (select) for anon\" ON \"Memos\".\"Memos\" FOR SELECT TO anon\n    USING (\"Memos\".is_member(\"Id\"));", "the rule asks the function by its logical name, and the policy by the name it has in the database");

        // Found again by the schema and the name the catalog keeps: as they are, capitals and all.
        script.Should().Contain("AND (n.nspname, c.relname) IN (('Memos', 'Memos'))", "the policies an earlier script made on the module's tables are dropped first");
        script.Should().Contain("AND (n.nspname, p.proname) NOT IN (('Memos', 'is_member'))", "a function the script still writes is kept, to be replaced in place");
        script.Should().Contain("AND (n.nspname, p.proname) IN (('Memos', 'is_member'))", "the grants an earlier file gave it are taken back by the same name");

        PostgresRowAccess.Script(capitalized, [], [RowAccessFunction.For<Memo>("memos.is_member", "TRUE")])
            .Should().Contain("CREATE OR REPLACE FUNCTION memos.is_member(uuid) RETURNS boolean", "a function named with its schema is SQL as it is written, in a schema of its own choosing");
    }

    [Fact]
    public void A_contributed_function_is_made_in_the_quoted_schema_of_its_context_and_named_so_where_contributed_sql_asks_it()
    {
        using var capitalized = CapitalizedMemosContext.Create();
        var contribution = new SpotContribution("memos", context => new(
            [
                new ContributedFunction("on_duty", "", "boolean", "SELECT true", GrantTo: [RowAccessRoles.User]),
                new ContributedFunction("duty_or_memo", "", "boolean", "SELECT {fn:memos/on_duty}()", GrantTo: [RowAccessRoles.User]),
            ],
            [new ContributedPolicy(SpotContribution.Of<Memo>(context), "On duty reads memos", "SELECT", RowAccessRoles.User, "(SELECT {fn:memos/duty_or_memo}())", null)],
            []));

        var script = PostgresRowAccess.Script(capitalized, [], [], new RowAccessExport { Contributions = [contribution] });

        script.Should().Contain("CREATE OR REPLACE FUNCTION \"Memos\".on_duty() RETURNS boolean");
        script.Should().Contain("CREATE OR REPLACE FUNCTION \"Memos\".duty_or_memo() RETURNS boolean\n    LANGUAGE sql STABLE SET search_path = '' AS $function$\nSELECT \"Memos\".on_duty()\n$function$;", "a function a contributed body asks is named as the script names it");
        script.Should().Contain("USING ((SELECT \"Memos\".duty_or_memo()))", "and so is one a contributed policy asks");
        script.IndexOf("FUNCTION \"Memos\".on_duty()", StringComparison.Ordinal).Should().BeLessThan(
            script.IndexOf("FUNCTION \"Memos\".duty_or_memo()", StringComparison.Ordinal),
            "a function is made after the functions it asks, which the script finds by the names they have in the database");
        script.Should().Contain("AND (n.nspname, p.proname) NOT IN (('Memos', 'duty_or_memo'), ('Memos', 'on_duty'))");
    }

    [Fact]
    public void A_schema_with_an_underscore_or_that_is_a_word_of_postgres_is_quoted_as_well_and_a_plain_one_is_written_as_it_was()
    {
        using var helpDesk = HelpDeskContext.Create();
        using var orderDesk = OrderDeskContext.Create();
        using var desk = DeskContext.Create();
        RowAccessRule[] rules = [DeskRules.WatchersBySet, DeskRules.OwnersClose];

        var quoted = PostgresRowAccess.Script(helpDesk, rules, [DeskRules.WatchedSet]);
        var word = PostgresRowAccess.Script(orderDesk, rules, [DeskRules.WatchedSet]);
        var plain = PostgresRowAccess.Script(desk, rules, [DeskRules.WatchedSet]);

        quoted.Should().Contain("CREATE OR REPLACE FUNCTION \"Help_Desk\".tickets_i_watch() RETURNS SETOF uuid")
            .And.Contain("\"Id\" = ANY (ARRAY(SELECT \"Help_Desk\".tickets_i_watch()))")
            .And.Contain("CREATE OR REPLACE FUNCTION \"Help_Desk\".tickets_status_column_rule() RETURNS trigger", "a column rule's trigger function is made in the schema of the context too")
            .And.Contain("    EXECUTE FUNCTION \"Help_Desk\".tickets_status_column_rule();")
            .And.Contain("NOT IN (('Help_Desk', 'tickets_i_watch'), ('Help_Desk', 'tickets_status_column_rule'))");
        word.Should().Contain("CREATE OR REPLACE FUNCTION \"order\".tickets_i_watch() RETURNS SETOF uuid", "order without quotes is no schema but a word of SQL")
            .And.Contain("NOT IN (('order', 'tickets_i_watch'), ('order', 'tickets_status_column_rule'))");
        plain.Should().Contain("CREATE OR REPLACE FUNCTION desk.tickets_i_watch() RETURNS SETOF uuid", "desk is a name Postgres keeps as SQL writes it, so it is written as Entity Framework writes it, without quotes")
            .And.Contain("NOT IN (('desk', 'tickets_i_watch'), ('desk', 'tickets_status_column_rule'))");

        Normalized(quoted, "\"Help_Desk\"", "Help_Desk").Should().Be(plain, "the schema is all that differs");
        Normalized(word, "\"order\"", "order").Should().Be(plain);

        static string Normalized(string script, string written, string kept)
            => script.Replace(written + ".", "desk.", StringComparison.Ordinal)
                .Replace("'" + kept + "'", "'desk'", StringComparison.Ordinal)
                .Replace(nameof(HelpDeskContext), nameof(DeskContext), StringComparison.Ordinal)
                .Replace(nameof(OrderDeskContext), nameof(DeskContext), StringComparison.Ordinal);
    }

    [Fact]
    public void A_rule_of_another_module_asks_the_function_in_its_quoted_schema()
    {
        using var capitalized = CapitalizedMemosContext.Create();
        using var bookmarks = BookmarksContext.Create();
        var bookmarkedMemos = RowAccessRule.For<Bookmark>("Bookmarks of members", RowOperations.Read, "{fn:memos/is_member}({col:Id})");

        var names = PostgresRowAccess.FunctionNamesOf([capitalized, bookmarks], [MemoMembers]);
        var scripts = PostgresRowAccess.Scripts([bookmarks, capitalized], [bookmarkedMemos], [MemoMembers], new RowAccessExport());

        names.Should().ContainKey("memos/is_member").WhoseValue.Should().Be("\"Memos\".is_member", "the name a function has in the database, as SQL writes it");
        scripts.Select(each => each.Context).Should().Equal([capitalized, bookmarks], "the module that defines the function goes first, as it does in any schema");
        scripts[1].Script.Should().Contain("USING (\"Memos\".is_member(\"Id\"))");
        scripts[0].Script.Should().Contain("GRANT EXECUTE ON FUNCTION \"Memos\".is_member(uuid) TO anon, authenticated;", "the policies of the other module ask it for both roles");
    }

    [Fact]
    public void Two_names_are_one_function_where_postgres_keeps_one_schema_and_name_for_both()
    {
        using var capitalized = CapitalizedMemosContext.Create();

        string Written(string named) => PostgresRowAccess.Script(capitalized, [], [MemoMembers], new RowAccessExport { FunctionNames = new Dictionary<string, string> { ["bookmarks/is_member"] = named } });

        FluentActions.Invoking(() => Written("\"Memos\".is_member")).Should().Throw<InvalidOperationException>()
            .WithMessage("The functions bookmarks/is_member (named in RowAccessExport.FunctionNames) and memos/is_member (*) would both be \"Memos\".is_member.*", "both are the function is_member of the schema Memos");
        FluentActions.Invoking(() => Written("\"Memos\".IS_MEMBER")).Should().Throw<InvalidOperationException>("a name without quotes is folded, so IS_MEMBER is is_member");
        FluentActions.Invoking(() => Written("\"Memos\".\"IS_MEMBER\"")).Should().NotThrow("a name in double quotes is kept as it is written, and IS_MEMBER is another function");
        FluentActions.Invoking(() => Written("Memos.is_member")).Should().NotThrow("Postgres folds a name without quotes, so this is is_member of the schema memos, another schema");
        FluentActions.Invoking(() => Written("memos.is_member")).Should().NotThrow();
    }

    [Fact]
    public void A_schema_longer_than_postgres_keeps_a_name_is_refused_since_postgres_would_cut_it()
    {
        using var overlong = OverlongMemosContext.Create();

        FluentActions.Invoking(() => PostgresRowAccess.Script(overlong, [], [MemoMembers])).Should().Throw<InvalidOperationException>().WithMessage(
            $"The function memos/is_member would be created in the schema '{OverlongMemosContext.Schema}' of OverlongMemosContext, which is longer than the 63 bytes Postgres keeps of a name. Postgres would cut it, and a script that names the schema in full would no longer find what it made there. Give the schema a shorter name.");
    }

    [Fact]
    public void The_supabase_export_writes_a_module_in_a_schema_with_capitals_with_the_schema_quoted()
    {
        var options = new SupabaseMigrationOptions { TimeProvider = new FixedClock(new DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero)) };
        options.RowLevelSecuritySchemas.Add(CapitalShelfContext.Schema);
        options.RowAccessRules.Add(RowAccessRule.For<SupabaseShelf>("Long shelves are read", RowOperations.Read, "{call:shelves/is_long}"));
        options.RowAccessFunctions.Add(RowAccessFunction.For<SupabaseShelf>("shelves/is_long", "({col:Capacity} > 10)"));

        using (var context = CapitalShelfContext.Create())
        {
            SupabaseMigrations.Export(context, _directory, options).IsInSync.Should().BeTrue();
        }

        var migration = File.ReadAllText(Path.Combine(_directory, "20261023120000_CreateCapitalShelves.capitalshelf.ddd.sql"));
        var access = File.ReadAllText(Directory.GetFiles(_directory, "*_access.capitalshelf.ddd.sql").Single());

        migration.Should().Contain("CREATE TABLE \"Shelf_Life\".\"Shelves\" (", "Entity Framework's own SQL quotes the schema")
            .And.Contain("ALTER TABLE \"Shelf_Life\".\"Shelves\" ENABLE ROW LEVEL SECURITY;", "the schema is one the Data API serves, as RowLevelSecuritySchemas names it")
            .And.Contain("AND (n.nspname, c.relname) IN (('Shelf_Life', 'Shelves'))", "the module's policies come off its tables before its schema changes");
        access.Should().Contain("CREATE OR REPLACE FUNCTION \"Shelf_Life\".is_long(uuid) RETURNS boolean")
            .And.Contain("ALTER TABLE \"Shelf_Life\".\"Shelves\" FORCE ROW LEVEL SECURITY;")
            .And.Contain("USING (\"Shelf_Life\".is_long(\"Id\"))")
            .And.Contain("GRANT USAGE ON SCHEMA \"Shelf_Life\" TO anon, authenticated;")
            .And.Contain("GRANT SELECT ON TABLE \"Shelf_Life\".\"Shelves\" TO anon;")
            .And.Contain("AND (n.nspname, p.proname) IN (('Shelf_Life', 'is_long'))");

        using (var context = CapitalShelfContext.Create())
        {
            SupabaseMigrations.Compare(context, _directory, options).IsInSync.Should().BeTrue("what the export wrote is what it writes again");
        }
    }

    /// <summary>A clock that stays at one moment, so the access file's name is known.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
