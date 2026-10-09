using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The privileges a script writes when it is asked to, read from its own policies: which role gets which
/// command on which table, which columns an update may write, and what the toolkit's own tables give. None of
/// it needs a database to read; <see cref="RowAccessPrivilegePostgresTests"/> runs the same scripts.
/// </summary>
public sealed class RowAccessPrivilegeTests
{
    private const string Hives = "apiary.\"Hives\"";

    private const string Boxes = "apiary.\"HiveBox\"";

    private const string Outbox = "ddd.\"OutboxMessages\"";

    private const string Inbox = "ddd.\"InboxMessages\"";

    /// <summary>The apiary's roles: the defaults, and the role its rangers' tokens are mapped to.</summary>
    private static readonly RowAccessRoleNames Roles = RowAccessRoleNames.Default with
    {
        TokenRoles = new Dictionary<string, string> { [ApiaryRules.Ranger] = ApiaryRules.RangerRole },
    };

    private const string Everybody = "PUBLIC, anon, apiary_ranger, authenticated, ddd_system_in";

    [Fact]
    public void Without_the_option_the_export_writes_no_privileges()
    {
        var plain = Script(new RowAccessExport { Roles = Roles });

        plain.Should().NotContain("-- Privileges").And.NotContain(" ON TABLE ").And.NotContain("REVOKE ALL ON TABLE").And.NotContain("ON SCHEMA apiary");
        plain.Should().NotContain("CREATE ROLE ddd_system_in", "no policy of the apiary names the scoped system role, and nothing else asks for it");

        // The bookkeeping role changes nothing in a script that never names it: a host whose system caller runs
        // as a role of another kind gets the script it always got.
        Script(new RowAccessExport { Roles = Roles with { System = "service_role" } }).Should().Be(plain);
    }

    [Fact]
    public void Privileges_follow_the_permissive_policies_per_table_command_and_role()
    {
        var script = Script(new RowAccessExport { Roles = Roles, WriteGrants = true });

        GrantsOn(script, Hives).Should().Equal(
            [
                $"GRANT SELECT ON TABLE {Hives} TO anon;",
                $"GRANT SELECT ON TABLE {Hives} TO apiary_ranger;",
                $"GRANT SELECT, INSERT, DELETE ON TABLE {Hives} TO authenticated;",
                $"GRANT UPDATE (\"IsOpen\", \"Keeper\", \"Label\", \"Version\") ON TABLE {Hives} TO authenticated;",
            ],
            "a keeper does anything with a hive, a visitor and a ranger read, and each gets exactly that");
        script.Should().Contain("GRANT USAGE ON SCHEMA apiary TO anon, apiary_ranger, authenticated;\n", "a role with a privilege on a table needs its schema");

        // What a contribution's permissive policy allows counts as a rule's does; a restrictive one only narrows.
        var contributed = Script(new RowAccessExport
        {
            Roles = Roles,
            WriteGrants = true,
            Contributions =
            [
                new SpotContribution("apiary", context => new(
                    [],
                    [
                        new ContributedPolicy(SpotContribution.Of<Hive>(context), "Scoped work reads every hive", "SELECT", RowAccessRoles.SystemIn, "TRUE", null),
                        new ContributedPolicy(SpotContribution.Of<Hive>(context), "Closed to visitors at night", "ALL", RowAccessRoles.Anonymous, "FALSE", "FALSE", Restrictive: true),
                        new ContributedPolicy(SpotContribution.Of<Hive>(context), "Closed to scoped work that writes", "INSERT", RowAccessRoles.SystemIn, null, "FALSE", Restrictive: true),
                    ],
                    [])),
            ],
        });

        GrantsOn(contributed, Hives).Should().Equal(
            [
                $"GRANT SELECT ON TABLE {Hives} TO anon;",
                $"GRANT SELECT ON TABLE {Hives} TO apiary_ranger;",
                $"GRANT SELECT, INSERT, DELETE ON TABLE {Hives} TO authenticated;",
                $"GRANT UPDATE (\"IsOpen\", \"Keeper\", \"Label\", \"Version\") ON TABLE {Hives} TO authenticated;",
                $"GRANT SELECT ON TABLE {Hives} TO ddd_system_in;",
            ],
            "the restrictive policies gave the visitors and the scoped work nothing");
    }

    [Fact]
    public void Update_is_granted_on_every_column_but_keys_and_those_fixed_after_insert()
    {
        var script = Script(new RowAccessExport { Roles = Roles, WriteGrants = true });

        script.Should().Contain($"GRANT UPDATE (\"IsOpen\", \"Keeper\", \"Label\", \"Version\") ON TABLE {Hives} TO authenticated;\n", "the hive's id is its key, and its number is fixed once it stands");
        script.Should().Contain($"GRANT UPDATE (\"Kind\") ON TABLE {Boxes} TO authenticated;\n", "a box's own id and the hive it stands on are its key");

        // A ledger with every kind of column a table has: what Entity Framework would not change, the database does not let change.
        using var ledger = LedgerContext.Create();
        var entries = PostgresRowAccess.Script(
            ledger,
            [RowAccessRule.For<Entry>("Bookkeepers keep the ledger", RowOperations.All, "TRUE", RowAccessRoles.User)],
            [],
            new RowAccessExport { WriteGrants = true });

        entries.Should().Contain(
            "GRANT UPDATE (\"Folio\", \"Memo\", \"Net\", \"Period_From\", \"Period_Until\", \"Reason\", \"Serial\", \"Tax\", \"Trail\") ON TABLE ledger.\"Entries\" TO authenticated;\n",
            "not the key, the alternate key, the column fixed after insert, the computed column or the discriminator; a value object stored inline by its columns, one stored as JSON as its one column");
        entries.Should().Contain("GRANT SELECT, INSERT, DELETE ON TABLE ledger.\"Entries\" TO authenticated;\n", "the other commands are the table's");
    }

    [Fact]
    public void A_role_without_a_permissive_policy_on_a_table_gets_nothing_there()
    {
        var script = Script(new RowAccessExport { Roles = Roles, WriteGrants = true });

        script.Should().Contain($"REVOKE ALL ON TABLE {Hives} FROM {Everybody};\n", "what the table gave before is taken back from every role a caller runs as");
        GrantsOn(script, Hives).Should().NotContain(grant => grant.EndsWith(" TO ddd_system_in;", StringComparison.Ordinal), "no policy on the hives is for the scoped system role");
        GrantsOn(script, Hives).Where(grant => grant.EndsWith(" TO anon;", StringComparison.Ordinal)).Should().Equal([$"GRANT SELECT ON TABLE {Hives} TO anon;"], "a visitor reads and no more");

        // A table a contribution keeps to itself, with no policy at all: row level security and no privilege.
        var kept = Script(
            new RowAccessExport
            {
                Roles = Roles,
                WriteGrants = true,
                Contributions = [new SpotContribution("apiary", context => new([], [], [], [context.Model.FindEntityType(typeof(InboxMessage))!]))],
            });

        kept.Should().Contain($"REVOKE ALL ON TABLE {Inbox} FROM {Everybody};\n");
        GrantsOn(kept, Inbox).Should().BeEmpty("a table somebody claims follows its policies, and this one has none");
    }

    [Fact]
    public void Entity_tables_get_what_their_policies_allow()
    {
        var script = Script(new RowAccessExport { Roles = Roles, WriteGrants = true });

        GrantsOn(script, Boxes).Should().Equal(
            [
                $"GRANT SELECT ON TABLE {Boxes} TO anon;",
                $"GRANT SELECT ON TABLE {Boxes} TO apiary_ranger;",
                $"GRANT SELECT, INSERT, DELETE ON TABLE {Boxes} TO authenticated;",
                $"GRANT UPDATE (\"Kind\") ON TABLE {Boxes} TO authenticated;",
            ],
            "the boxes are read with their hive and written as the rules let a caller write the hive");
    }

    [Fact]
    public void Outbox_tables_are_insert_only_and_inbox_tables_the_scoped_system_roles()
    {
        var script = Script(new RowAccessExport { Roles = Roles, WriteGrants = true });

        script.Should().Contain($"REVOKE ALL ON TABLE {Outbox} FROM {Everybody};\n").And.Contain($"REVOKE ALL ON TABLE {Inbox} FROM {Everybody};\n");
        GrantsOn(script, Outbox).Should().Equal([$"GRANT INSERT ON TABLE {Outbox} TO authenticated, ddd_system_in;"], "whoever saves adds the event's row, and nobody reads one");
        GrantsOn(script, Inbox).Should().Equal([$"GRANT SELECT, INSERT ON TABLE {Inbox} TO ddd_system_in;"], "the handlers of integration events run as the scoped system role");
        script.Should().Contain("GRANT USAGE ON SCHEMA ddd TO authenticated, ddd_system_in;\n");
        script.Should().NotContain($"ALTER TABLE {Outbox} ENABLE ROW LEVEL SECURITY").And.NotContain($"ALTER TABLE {Inbox} ENABLE ROW LEVEL SECURITY");

        // A role the policies let write a table of the context saves too, and its save writes the event's row.
        var rangersWrite = Script(
            new RowAccessExport { Roles = Roles, WriteGrants = true },
            [.. ApiaryRules.All, RowAccessRule.For<Hive>("Rangers condemn a hive", RowOperations.Remove, "TRUE", RowAccessRoles.Token(ApiaryRules.Ranger))]);
        GrantsOn(rangersWrite, Outbox).Should().Equal([$"GRANT INSERT ON TABLE {Outbox} TO apiary_ranger, authenticated, ddd_system_in;"]);
    }

    [Fact]
    public void The_bookkeeping_role_marks_an_outbox_row_and_gets_no_other_column_to_update()
    {
        var script = Script(new RowAccessExport { Roles = Roles with { System = "ddd_system" }, WriteGrants = true });

        GrantsOn(script, Outbox).Should().Equal(
            [
                $"GRANT INSERT ON TABLE {Outbox} TO authenticated, ddd_system_in;",
                $"GRANT SELECT, DELETE ON TABLE {Outbox} TO ddd_system;",
                $"GRANT UPDATE (\"Attempts\", \"LastError\", \"NextAttemptAt\", \"ProcessedAt\") ON TABLE {Outbox} TO ddd_system;",
            ],
            "the processor says how a delivery went, and the event's name, payload and version stay as they were raised");

        // The columns are the model's: the four properties, whatever a naming convention calls their columns.
        using var model = ApiaryContext.ForScripts();
        var outbox = model.Model.FindEntityType(typeof(OutboxMessage))!;
        string[] written = [nameof(OutboxMessage.Attempts), nameof(OutboxMessage.LastError), nameof(OutboxMessage.NextAttemptAt), nameof(OutboxMessage.ProcessedAt)];
        written.Should().OnlyContain(name => outbox.FindProperty(name) != null, "the script names the properties the processor writes by their names");
    }

    [Fact]
    public void The_toolkits_own_tables_are_taken_back_from_whoever_an_earlier_file_named()
    {
        var script = Script(new RowAccessExport { Roles = Roles with { System = "ddd_system" }, WriteGrants = true });
        var lines = script.Split('\n');

        // One block for the outbox and the inbox, which have no policies: it finds who holds a privilege on them
        // where the script runs, so a role this file no longer names loses what an earlier file gave it.
        var block = Array.FindIndex(lines, line => line.StartsWith("-- The toolkit's own tables have no policies", StringComparison.Ordinal));
        block.Should().BeGreaterThan(0);
        script.Should().Contain($"        WHERE c.oid IN ('{Inbox}'::pg_catalog.regclass, '{Outbox}'::pg_catalog.regclass)\n", "the tables with policies keep what a role of the host's own holds on them");
        script.Should().Contain("          AND acl.grantee <> c.relowner\n", "the owner's own privileges are not the script's to take");
        script.Should().Contain(
            "          AND NOT (holder.rolcanlogin OR holder.rolbypassrls OR holder.rolsuper)\n",
            "no file gives a privilege to a role that can log in or is past the policies, so what such a role holds the host gave it");
        script.Should().Contain("        EXECUTE pg_catalog.format('REVOKE ALL ON TABLE %s FROM %s', held.relation::pg_catalog.regclass, held.grantee::pg_catalog.regrole);\n");
        script.Split("held.relation::pg_catalog.regclass").Should().HaveCount(2, "one block serves every such table");

        Array.FindIndex(lines, line => line.StartsWith("GRANT ", StringComparison.Ordinal) && (line.Contains($" ON TABLE {Outbox} ", StringComparison.Ordinal) || line.Contains($" ON TABLE {Inbox} ", StringComparison.Ordinal)))
            .Should().BeGreaterThan(block, "what the tables gave before goes first, or the block would take back what this file gives");

        // A contribution that keeps the inbox to itself gives it policies, or none: it is no longer the toolkit's to clear.
        var kept = Script(new RowAccessExport
        {
            Roles = Roles,
            WriteGrants = true,
            Contributions = [new SpotContribution("apiary", context => new([], [], [], [context.Model.FindEntityType(typeof(InboxMessage))!]))],
        });
        kept.Should().Contain($"        WHERE c.oid IN ('{Outbox}'::pg_catalog.regclass)\n");

        // A context without a table of the toolkit's own writes no such block.
        using var ledger = LedgerContext.Create();
        PostgresRowAccess.Script(ledger, LedgerRules, [], new RowAccessExport { WriteGrants = true })
            .Should().NotContain("The toolkit's own tables").And.NotContain("held.relation");
    }

    [Fact]
    public void Privileges_are_revoked_first_and_named_table_by_table()
    {
        var script = Script(new RowAccessExport { Roles = Roles, WriteGrants = true });
        var lines = script.Split('\n');

        script.Should().NotContain("ALL TABLES", "a table the script knows nothing about keeps what it has");
        foreach (var table in (string[])[Hives, Boxes, Outbox, Inbox])
        {
            var revoke = Array.IndexOf(lines, $"REVOKE ALL ON TABLE {table} FROM {Everybody};");
            revoke.Should().BeGreaterThan(0, $"{table} is named on its own");
            Array.FindIndex(lines, line => line.StartsWith("GRANT ", StringComparison.Ordinal) && line.Contains($" ON TABLE {table} ", StringComparison.Ordinal))
                .Should().BeGreaterThan(revoke, $"what {table} gave before goes first");
        }

        var privileges = Array.FindIndex(lines, line => line.StartsWith("-- Privileges, from the policies above", StringComparison.Ordinal));
        privileges.Should().BeGreaterThan(Array.FindLastIndex(lines, line => line.StartsWith("CREATE POLICY ", StringComparison.Ordinal)), "the privileges come after the policies they are read from");

        // The roles the revokes name have to exist where the script runs, so it makes the ones that are the toolkit's.
        script.Should().Contain("        CREATE ROLE ddd_system_in NOLOGIN NOINHERIT;\n").And.Contain("        CREATE ROLE apiary_ranger NOLOGIN NOINHERIT;\n");
    }

    [Fact]
    public void A_role_that_may_change_or_remove_but_not_read_is_refused()
    {
        foreach (var operation in (RowOperations[])[RowOperations.Change, RowOperations.Remove])
        {
            var write = () => Script(
                new RowAccessExport { Roles = Roles, WriteGrants = true },
                [ApiaryRules.Keepers, RowAccessRule.For<Hive>("Rangers write blind", operation, "TRUE", RowAccessRoles.Token(ApiaryRules.Ranger))]);

            write.Should().Throw<InvalidOperationException>()
                .WithMessage("The policies let RowAccessRoles.Token(\"ranger\") change or remove rows of apiary.Hives and not read them.*Add a rule that lets that role read those rows*");
        }

        // Adding rows without reading them is a rule that can run, and so is the same rule without the privileges.
        Script(
            new RowAccessExport { Roles = Roles, WriteGrants = true },
            [ApiaryRules.Keepers, RowAccessRule.For<Hive>("Rangers settle hives", RowOperations.Create, "TRUE", RowAccessRoles.Token(ApiaryRules.Ranger))])
            .Should().Contain($"GRANT INSERT ON TABLE {Hives} TO apiary_ranger;\n");
        var unwritten = () => Script(
            new RowAccessExport { Roles = Roles },
            [ApiaryRules.Keepers, RowAccessRule.For<Hive>("Rangers write blind", RowOperations.Change, "TRUE", RowAccessRoles.Token(ApiaryRules.Ranger))]);
        unwritten.Should().NotThrow("a script that writes no privileges says nothing about them");
    }

    [Fact]
    public void A_table_whose_column_owns_a_sequence_hands_it_to_the_roles_that_add_rows()
    {
        using var ledger = LedgerContext.Create();
        var script = PostgresRowAccess.Script(ledger, LedgerRules, [], new RowAccessExport { WriteGrants = true });

        script.Should().Contain("          AND d.refobjid = 'ledger.\"Entries\"'::pg_catalog.regclass AND d.deptype IN ('a', 'i')\n", "the sequences are the ones the table's columns own");
        script.Should().Contain("        EXECUTE pg_catalog.format('REVOKE ALL ON SEQUENCE %s FROM %s', owned, 'PUBLIC, anon, authenticated, ddd_system_in');\n");
        script.Should().Contain("        EXECUTE pg_catalog.format('GRANT USAGE ON SEQUENCE %s TO %s', owned, 'authenticated');\n", "a bookkeeper adds entries, and a visitor does not");

        Script(new RowAccessExport { Roles = Roles, WriteGrants = true }).Should().NotContain("ON SEQUENCE", "the apiary's keys are made by the application");
    }

    [Fact]
    public void Changing_a_column_fixed_after_insert_throws_in_csharp()
    {
        using var database = new SqliteDatabase();
        using (var create = new ApiaryContext(database.Options<ApiaryContext>()))
        {
            create.Database.EnsureCreated();
            create.Hives.Add(new Hive(HiveId.CreateSequential(), number: 7, "By the hedge", keeper: null, isOpen: true));
            create.SaveChanges();
        }

        using var context = new ApiaryContext(database.Options<ApiaryContext>());
        var hive = context.Hives.Single();
        hive.Rename("By the gate");
        context.SaveChanges();

        hive.Renumber(8);
        var save = () => context.SaveChanges();

        save.Should().Throw<InvalidOperationException>().WithMessage("*'Hive.Number'*", "Entity Framework refuses the change before any statement is sent");
        using var check = new ApiaryContext(database.Options<ApiaryContext>());
        check.Hives.Single().Should().Match<Hive>(row => row.Number == 7 && row.Label == "By the gate");
    }

    private static string Script(RowAccessExport export, IReadOnlyList<RowAccessRule>? rules = null)
    {
        using var model = ApiaryContext.ForScripts();
        return PostgresRowAccess.Script(model, rules ?? ApiaryRules.All, [], export);
    }

    /// <summary>The <c>GRANT … ON TABLE</c> lines of <paramref name="script"/> for <paramref name="table"/>, in the order it writes them.</summary>
    private static List<string> GrantsOn(string script, string table)
        => [.. script.Split('\n').Where(line => line.StartsWith("GRANT ", StringComparison.Ordinal) && line.Contains($" ON TABLE {table} TO ", StringComparison.Ordinal))];

    /// <summary>The ledger's rules: a bookkeeper does anything with an entry, and a visitor reads.</summary>
    internal static readonly RowAccessRule[] LedgerRules =
    [
        RowAccessRule.For<Entry>("Bookkeepers keep the ledger", RowOperations.All, "TRUE", RowAccessRoles.User),
        RowAccessRule.For<Entry>("Visitors read the ledger", RowOperations.Read, "TRUE", RowAccessRoles.Anonymous),
    ];

    // A ledger, to show which columns an update may write: an entry with a key, an alternate key, a column fixed
    // once the row is added, one the database computes, a discriminator, two numbers the database hands out, one
    // from a sequence the column's default asks and one as an identity, and two value objects, one stored inline
    // and one as JSON.

    public class Entry
    {
        public Guid Id { get; set; }

        public string Code { get; set; } = "";

        public Guid BookedBy { get; set; }

        public int Serial { get; set; }

        public long Folio { get; set; }

        public decimal Net { get; set; }

        public decimal Tax { get; set; }

        public decimal Total { get; set; }

        public string? Memo { get; set; }

        public EntryPeriod Period { get; set; } = new();

        public EntryTrail Trail { get; set; } = new();
    }

    public sealed class Correction : Entry
    {
        public string Reason { get; set; } = "";
    }

    public sealed class EntryPeriod
    {
        public DateOnly From { get; set; }

        public DateOnly Until { get; set; }
    }

    public sealed class EntryTrail
    {
        public string Source { get; set; } = "";

        public List<string> Steps { get; set; } = [];
    }

    internal sealed class LedgerContext(DbContextOptions<LedgerContext> options) : DbContext(options)
    {
        public DbSet<Entry> Entries => Set<Entry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("ledger");
            modelBuilder.Entity<Entry>(entry =>
            {
                entry.ToTable("Entries");
                entry.HasAlternateKey(row => row.Code);
                entry.Property(row => row.BookedBy).IsFixedAfterInsert();

                // A serial column takes its next value as the role that inserts, so that role needs the sequence;
                // an identity column asks no privilege of its own.
                entry.Property(row => row.Serial).UseSerialColumn();
                entry.Property(row => row.Folio).UseIdentityByDefaultColumn();
                entry.Property(row => row.Total).HasComputedColumnSql("\"Net\" + \"Tax\"", stored: true);
                entry.HasDiscriminator<string>("Kind").HasValue<Entry>("entry").HasValue<Correction>("correction");
                entry.OwnsOne(row => row.Period);
                entry.OwnsOne(row => row.Trail, trail => trail.ToJson());
            });
        }

        /// <summary>The ledger's model on Npgsql, for a script to be written from: it never connects unless given a database.</summary>
        public static LedgerContext Create(string connectionString = "Host=nowhere.invalid;Database=unused")
            => new(new DbContextOptionsBuilder<LedgerContext>().UseNpgsql(connectionString).Options);
    }
}
