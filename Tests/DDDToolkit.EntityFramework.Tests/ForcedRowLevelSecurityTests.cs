using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Forcing row level security: a script writes <c>FORCE</c> after every <c>ENABLE</c> when it is asked to, and is
/// what it was when it is not. On a forced table the owner is held to the policies too, unless it may bypass
/// them, which is what the functions that run as their owner then rest on, and what the start-up check asks.
/// </summary>
public sealed class ForcedRowLevelSecurityTests(ExplicitCallersPostgres postgres)
{
    private static int roles;

    /// <summary>The apiary's roles: the defaults, and the role its rangers' tokens are mapped to.</summary>
    private static readonly RowAccessRoleNames Mapped = RowAccessRoleNames.Default with
    {
        TokenRoles = new Dictionary<string, string> { [ApiaryRules.Ranger] = ApiaryRules.RangerRole },
    };

    /// <summary>The apiary's rules with the one that asks an access function about a hive's boxes.</summary>
    private static readonly RowAccessRule[] WithHoney = [.. ApiaryRules.All, ApiaryRules.VisitorsOfHoney];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A role name no other test uses: roles are the server's, and every test class shares one.</summary>
    private static string NewRole(string what) => $"apiary_{what}_{Interlocked.Increment(ref roles)}_{Guid.NewGuid():N}"[..40];

    [Fact]
    public void Force_follows_every_enable_when_asked()
    {
        // The tables of the rules and of the aggregates' entities, a table a contribution keeps to itself, and a
        // table without a rule that a contribution writes a policy for.
        var script = Script(Forced(true));

        var lines = script.Split('\n');
        var enabled = lines.Select((line, index) => (line, index)).Where(each => each.line.EndsWith(" ENABLE ROW LEVEL SECURITY;", StringComparison.Ordinal)).ToList();

        enabled.Select(each => each.line).Should().Equal(
            [
                "ALTER TABLE apiary.\"Hives\" ENABLE ROW LEVEL SECURITY;",
                "ALTER TABLE apiary.\"HiveBox\" ENABLE ROW LEVEL SECURITY;",
                "ALTER TABLE ddd.\"InboxMessages\" ENABLE ROW LEVEL SECURITY;",
                "ALTER TABLE ddd.\"OutboxMessages\" ENABLE ROW LEVEL SECURITY;",
            ]);
        foreach (var (line, index) in enabled)
        {
            lines[index + 1].Should().Be(line.Replace(" ENABLE ", " FORCE ", StringComparison.Ordinal), "the owner is held to the policies of every table the script turns them on for");
        }

        lines.Count(line => line.Contains(" FORCE ROW LEVEL SECURITY", StringComparison.Ordinal)).Should().Be(enabled.Count, "and of no other table");
    }

    [Fact]
    public void Without_force_every_access_file_is_unchanged()
    {
        var plain = Script(Forced(false));

        plain.Should().NotContain("FORCE");
        Script(Forced(null)).Should().Be(plain, "leaving the option out is turning it off");

        // The forced script is the plain one with those lines in it, and nothing else differs.
        string.Join('\n', Script(Forced(true)).Split('\n').Where(line => !line.EndsWith(" FORCE ROW LEVEL SECURITY;", StringComparison.Ordinal))).Should().Be(plain);
    }

    [Fact]
    public async Task A_forced_table_hides_its_rows_from_an_owner_without_bypass()
    {
        foreach (var force in (bool[])[false, true])
        {
            var database = await ApiaryDatabase.CreateAsync(postgres, configure: export => With(export, force));
            await SeedAsync(database);

            // The role that runs the migrations and owns the tables, where that is not a superuser: Supabase's own
            // may bypass row level security, a role of your own making usually may not.
            var owner = NewRole("owner");
            await database.RunAsOwnerAsync(
                $"""
                CREATE ROLE {owner} NOLOGIN;
                GRANT USAGE ON SCHEMA apiary TO {owner};
                ALTER TABLE apiary."Hives" OWNER TO {owner};
                """,
                Cancellation);

            await using var connection = new NpgsqlConnection(database.OwnerConnectionString);
            await connection.OpenAsync(Cancellation);
            await ExecuteAsync(connection, $"SET ROLE {owner}");

            (await ScalarAsync(connection, """SELECT count(*) FROM apiary."Hives" """)).Should().Be(
                force ? "0" : "2",
                force ? "forced, the table holds its owner to its policies, and none is for the owner" : "Postgres exempts a table's owner from the table's policies");

            await ExecuteAsync(connection, "RESET ROLE");
            await ExecuteAsync(connection, $"ALTER TABLE apiary.\"Hives\" OWNER TO postgres; DROP OWNED BY {owner}; DROP ROLE {owner};");
        }
    }

    [Fact]
    public async Task A_definer_function_whose_owner_bypasses_still_reads_a_forced_table()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, configure: export => With(export, force: true), rules: WithHoney, functions: [ApiaryRules.HasHoney]);
        await SeedAsync(database);
        await using var host = database.BuildHost();

        (await database.ListAsOwnerAsync("SELECT c.relname || ' ' || c.relforcerowsecurity::text FROM pg_catalog.pg_class c WHERE c.relnamespace = 'apiary'::regnamespace AND c.relkind = 'r' ORDER BY 1", Cancellation))
            .Should().Equal("HiveBox true", "Hives true");

        // Both hives are closed to visitors, and one carries honey. The policy asks the function, the function
        // reads the boxes as its owner, and its owner may bypass row level security, forced or not.
        await ApiaryDatabase.AsAsync(host, Caller.Anonymous, async context =>
        {
            (await context.Hives.Select(hive => hive.Label).ToListAsync(Cancellation)).Should().Equal("With honey");
        });

        await using var scope = host.CreateAsyncScope();
        var check = () => PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(scope.ServiceProvider.GetRequiredService<ApiaryContext>(), Cancellation);
        await check.Should().NotThrowAsync("the superuser made the functions, and a superuser is held to no policy");
    }

    [Fact]
    public async Task The_definer_owner_check_names_a_function_whose_owner_cannot_bypass()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, configure: export => With(export, force: true), rules: WithHoney, functions: [ApiaryRules.HasHoney]);
        await SeedAsync(database);
        await using var host = database.BuildHost();

        // The tables and the function handed to a role that owns them and may not bypass row level security.
        var owner = NewRole("owner");
        await database.RunAsOwnerAsync(
            $"""
            CREATE ROLE {owner} NOLOGIN;
            GRANT USAGE ON SCHEMA apiary TO {owner};
            ALTER TABLE apiary."Hives" OWNER TO {owner};
            ALTER TABLE apiary."HiveBox" OWNER TO {owner};
            ALTER FUNCTION apiary.has_honey(uuid) OWNER TO {owner};
            """,
            Cancellation);

        // Without a word: the function now reads the forced tables under its owner's policies, of which there are
        // none, and answers that no hive carries honey.
        await ApiaryDatabase.AsAsync(host, Caller.Anonymous, async context =>
        {
            (await context.Hives.CountAsync(Cancellation)).Should().Be(0, "the visitors' rule still stands, and asks a function that finds no row");
        });

        await using (var scope = host.CreateAsyncScope())
        {
            var check = () => PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(scope.ServiceProvider.GetRequiredService<ApiaryContext>(), Cancellation);

            (await check.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(
                "Functions in the schemas of 'ApiaryContext' run as their owner, and their owner is held to row level security, so on a table that forces it they would read no row:\n" +
                $"- apiary.has_honey(uuid) is owned by {owner}. Fix: ALTER FUNCTION apiary.has_honey(uuid) OWNER TO <a role with BYPASSRLS>; or ALTER ROLE {owner} BYPASSRLS;");
        }

        // Once the owner may bypass, the function answers again, and the check passes.
        await database.RunAsOwnerAsync($"ALTER ROLE {owner} BYPASSRLS;", Cancellation);
        await ApiaryDatabase.AsAsync(host, Caller.Anonymous, async context =>
        {
            (await context.Hives.Select(hive => hive.Label).ToListAsync(Cancellation)).Should().Equal("With honey");
        });
        await using (var scope = host.CreateAsyncScope())
        {
            await PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(scope.ServiceProvider.GetRequiredService<ApiaryContext>(), Cancellation);
        }

        await database.RunAsOwnerAsync($"DROP OWNED BY {owner}; DROP ROLE {owner};", Cancellation);
    }

    [Fact]
    public async Task The_definer_owner_check_passes_while_nothing_is_forced_and_names_the_function_once_a_table_is()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, configure: export => With(export, force: false), rules: WithHoney, functions: [ApiaryRules.HasHoney]);
        await SeedAsync(database);
        await using var host = database.BuildHost();

        // A database made by a role of its own that may not bypass row level security, as most are: it owns the
        // tables and the function, and forces nothing.
        var owner = NewRole("owner");
        await database.RunAsOwnerAsync(
            $"""
            CREATE ROLE {owner} NOLOGIN;
            GRANT USAGE ON SCHEMA apiary TO {owner};
            ALTER TABLE apiary."Hives" OWNER TO {owner};
            ALTER TABLE apiary."HiveBox" OWNER TO {owner};
            ALTER FUNCTION apiary.has_honey(uuid) OWNER TO {owner};
            """,
            Cancellation);

        try
        {
            // The function reads the boxes as their owner, which Postgres exempts from their policies: it answers.
            await ApiaryDatabase.AsAsync(host, Caller.Anonymous, async context =>
            {
                (await context.Hives.Select(hive => hive.Label).ToListAsync(Cancellation)).Should().Equal("With honey");
            });

            await using var scope = host.CreateAsyncScope();
            var check = () => PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(scope.ServiceProvider.GetRequiredService<ApiaryContext>(), Cancellation);
            await check.Should().NotThrowAsync("nothing is forced, so an owner that cannot bypass is all the function needs");

            // One forced table is enough: the function now reads it under its owner's policies.
            await database.RunAsOwnerAsync("""ALTER TABLE apiary."HiveBox" FORCE ROW LEVEL SECURITY;""", Cancellation);
            await ApiaryDatabase.AsAsync(host, Caller.Anonymous, async context =>
            {
                (await context.Hives.CountAsync(Cancellation)).Should().Be(0, "the function finds no box");
            });

            (await check.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(
                "Functions in the schemas of 'ApiaryContext' run as their owner, and their owner is held to row level security, so on a table that forces it they would read no row:\n" +
                $"- apiary.has_honey(uuid) is owned by {owner}. Fix: ALTER FUNCTION apiary.has_honey(uuid) OWNER TO <a role with BYPASSRLS>; or ALTER ROLE {owner} BYPASSRLS;");
        }
        finally
        {
            await database.RunAsOwnerAsync($"DROP OWNED BY {owner}; DROP ROLE {owner};", Cancellation);
        }
    }

    /// <summary>
    /// An export for the apiary with a contribution that keeps the inbox to itself and writes a policy on the
    /// outbox, a table no rule is about; with row level security forced, not forced, or the option left out.
    /// </summary>
    private static RowAccessExport Forced(bool? force)
    {
        IRowAccessContribution[] contributions =
        [
            new SpotContribution("apiary", context => new(
                [],
                [new ContributedPolicy(context.Model.FindEntityType(typeof(OutboxMessage))!, "Scoped work adds events", "INSERT", RowAccessRoles.SystemIn, null, "TRUE")],
                [],
                [context.Model.FindEntityType(typeof(InboxMessage))!])),
        ];

        return force is { } forced
            ? new RowAccessExport { Roles = Mapped, Contributions = contributions, ForceRowLevelSecurity = forced }
            : new RowAccessExport { Roles = Mapped, Contributions = contributions };
    }

    /// <summary><paramref name="export"/> with row level security forced or not; an export's settings are set when it is made.</summary>
    private static RowAccessExport With(RowAccessExport export, bool force)
        => new() { Roles = export.Roles, WriteGrants = export.WriteGrants, ForceRowLevelSecurity = force };

    private static string Script(RowAccessExport export)
    {
        using var model = ApiaryContext.ForScripts();
        return PostgresRowAccess.Script(model, ApiaryRules.All, [], export);
    }

    /// <summary>Two hives closed to visitors, one of which carries a honey box, written as the superuser.</summary>
    private static async Task SeedAsync(ApiaryDatabase database)
    {
        await using var seed = database.ModelContext();
        var withHoney = new Hive(HiveId.CreateSequential(), number: 1, "With honey", ApiaryDatabase.AliceId, isOpen: false);
        withHoney.Stack("brood");
        withHoney.Stack("honey");
        var without = new Hive(HiveId.CreateSequential(), number: 2, "Without", ApiaryDatabase.AliceId, isOpen: false);
        without.Stack("brood");
        seed.Hives.AddRange(withHoney, without);
        await seed.SaveChangesAsync(Cancellation);
    }

    private static async Task<string?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync(Cancellation), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }
}
