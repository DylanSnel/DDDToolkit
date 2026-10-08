using System.Data;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// A token's role claim picks the database role only through a list the host wrote: the role of a signed-in
/// user, the anonymous caller's, and the token roles it mapped. Anything else is refused before a context
/// connects. None of this opens a database: the interceptor is asked what it would set, on a connection that
/// keeps what it is sent, and the scripts are read as text. <c>TokenRolePostgresTests</c> runs the same on Postgres.
/// </summary>
public sealed class TokenRoleTests
{
    private const string Analyst = "analyst";

    private const string AnalystRole = "desk_analyst";

    private static readonly Guid Ada = Guid.Parse("ada00000-0000-4000-8000-000000000001");

    private static readonly RowAccessRule AnalystsReadEveryTicket = RowAccessRule.For<Ticket>(
        "Analysts read every ticket", RowOperations.Read, "TRUE", RowAccessRoles.Token(Analyst));

    private static readonly RowAccessRule ScopedWork = RowAccessRule.For<Ticket>(
        "Scoped work reads every ticket", RowOperations.Read, "TRUE", RowAccessRoles.SystemIn);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void A_token_without_a_role_claim_counts_as_authenticated()
    {
        var withoutAClaim = Callers.FromClaims(ClaimsOf(role: null));
        withoutAClaim.Role.Should().BeNull("the token says nothing about a role");

        SetFor(withoutAClaim, new()).Should().Be(("authenticated", ClaimsOf(role: null)), "it is a signed-in user, with the claims it was signed with");
        SetFor(Caller.User(Ada), new()).Should().Be(("authenticated", $$"""{"sub":"{{Ada}}","role":"authenticated"}"""), "and so is a user made without a token");
        SetFor(Callers.FromClaims(ClaimsOf("authenticated")), new()).Role.Should().Be("authenticated");

        // PostgREST's name for a signed-in user means the host's role for one, whatever the host calls it.
        var renamed = new PostgresRowLevelSecurityOptions { UserRole = "members" };
        SetFor(withoutAClaim, renamed).Role.Should().Be("members");
        SetFor(Callers.FromClaims(ClaimsOf("authenticated")), renamed).Role.Should().Be("members");

        // Mapped, authenticated is a token role like any other, and no claim still counts as it.
        var mapped = new PostgresRowLevelSecurityOptions { TokenRoles = { ["authenticated"] = "desk_member" } };
        SetFor(withoutAClaim, mapped).Role.Should().Be("desk_member");
        SetFor(Callers.FromClaims(ClaimsOf("authenticated")), mapped).Role.Should().Be("desk_member");
    }

    [Fact]
    public void A_host_without_a_list_runs_every_kind_of_caller_as_before()
    {
        var options = new PostgresRowLevelSecurityOptions { SystemRole = SupabaseRowLevelSecurity.ServiceRole };
        options.TokenRoles.Should().BeEmpty();
        options.UnknownTokenRole.Should().Be(UnknownTokenRole.Refuse);

        SetFor(Callers.FromClaims(ClaimsOf("authenticated")), options).Should().Be(("authenticated", ClaimsOf("authenticated")));
        SetFor(Caller.Anonymous, options).Should().Be(("anon", """{"role":"anon"}"""));
        SetFor(Caller.System, options).Should().Be(("service_role", """{"role":"service_role"}"""));
        SetFor(Caller.System, new()).Should().Be(("none", ""), "without a system role, background work stays the login role");
        SetFor(Caller.SystemIn("desk"), options).Should().Be(("ddd_system_in", """{"role":"ddd_system_in","scope":"desk"}"""));

        var connection = new RecordedConnection();
        var interceptor = new PostgresRowLevelSecurityInterceptor(new CallerOfTheTest { Current = Caller.User(Ada) }, new PostgresRowLevelSecurityOptions());
        interceptor.ConnectionOpened(connection, null!);
        connection.Statements.Should().ContainSingle().Which.Sql.Should().Be(
            "SELECT set_config('role', $1, false), set_config('request.jwt.claims', $2, false), " +
            "set_config('request.jwt.claim.sub', '', false), set_config('request.jwt.claim.role', '', false), " +
            "set_config('request.jwt.claim.email', '', false), set_config('request.jwt.claim', '', false)",
            "the statement is what it was");
    }

    [Fact]
    public void A_mapped_token_role_is_given_its_database_role_and_the_tokens_own_claims()
    {
        var options = new PostgresRowLevelSecurityOptions { TokenRoles = { [Analyst] = AnalystRole, ["examiner"] = AnalystRole } };

        SetFor(Callers.FromClaims(ClaimsOf(Analyst)), options).Should().Be((AnalystRole, ClaimsOf(Analyst)), "the role is the mapped one, and the claims say what the token says");
        SetFor(Caller.User(Ada, Analyst), options).Should().Be((AnalystRole, $$"""{"sub":"{{Ada}}","role":"analyst"}"""));
        SetFor(Callers.FromClaims(ClaimsOf("examiner")), options).Role.Should().Be(AnalystRole, "two token roles may run as one role");
        SetFor(Callers.FromClaims(ClaimsOf("authenticated")), options).Role.Should().Be("authenticated", "a map leaves the signed-in user as it was");
    }

    [Fact]
    public void A_role_claim_that_names_the_users_or_the_anonymous_callers_role_runs_as_that_role()
    {
        var renamed = new PostgresRowLevelSecurityOptions { UserRole = "members", AnonymousRole = "visitors" };

        SetFor(Callers.FromClaims(ClaimsOf("members")), renamed).Should().Be(("members", ClaimsOf("members")), "the role is on the list as the user's own");
        SetFor(Callers.FromClaims(ClaimsOf("visitors")), renamed).Should().Be(("visitors", ClaimsOf("visitors")), "the least any caller gets, with the token's own claims as PostgREST would send them");
        SetFor(Callers.FromClaims(ClaimsOf("anon")), renamed).Role.Should().Be("visitors", "PostgREST's name for a caller who did not sign in means the host's role for one");
        SetFor(Callers.FromClaims(ClaimsOf("anon")), new()).Should().Be(("anon", ClaimsOf("anon")));

        // A token never runs as more than the list gives it: naming the anonymous role is never the user's role.
        SetFor(Callers.FromClaims(ClaimsOf("anon")), new()).Role.Should().NotBe("authenticated");
    }

    public static TheoryData<string> RolesOnNoList() => new(
        "service_role",
        "postgres",
        "authenticator",
        "ddd_system_in",
        "desk_app",
        "none",
        "NONE",
        "Authenticated",
        "authenticated ",
        " analyst",
        "ANALYST",
        "",
        " ",
        "pg_read_all_data",
        "public",
        AnalystRole,
        "@token:analyst",
        "@user",
        "analyst\", false), set_config('role', 'postgres");

    [Theory]
    [MemberData(nameof(RolesOnNoList))]
    public async Task An_unknown_token_role_is_refused_before_any_statement(string forged)
    {
        // Every role a token could name to get more than a signed-in user gets: the system's, the scoped
        // system's, the login role, a mapped role by its database name, and near misses of the ones listed.
        var options = new PostgresRowLevelSecurityOptions { SystemRole = SupabaseRowLevelSecurity.ServiceRole, TokenRoles = { [Analyst] = AnalystRole } };
        var caller = new CallerOfTheTest { Current = Callers.FromClaims(ClaimsOf(forged)) };
        var interceptor = new PostgresRowLevelSecurityInterceptor(caller, options);

        // Through a context: the query never connects.
        await using var context = DeskContext.Create(interceptor: interceptor);
        var refused = (await FluentActions.Awaiting(() => context.Tickets.CountAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;

        refused.Code.Should().Be(ToolkitRefusals.RoleNotAllowed).And.Be("access.role-not-allowed");
        refused.Kind.Should().Be(RefusalKind.NotPermitted);
        refused.Arguments.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Role"] = forged });
        refused.Message.Should().Be($"The role this sign-in carries gives no access here: {forged}.");
        context.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed, "it was refused before connecting, so nothing ran as any role");
        FluentActions.Invoking(() => context.Tickets.Count()).Should().Throw<RefusalException>("the query without await is refused the same")
            .Which.Code.Should().Be(ToolkitRefusals.RoleNotAllowed);

        // And on a connection that is open already: nothing is sent down it.
        var connection = new RecordedConnection();
        FluentActions.Invoking(() => interceptor.ConnectionOpened(connection, null!)).Should().Throw<RefusalException>();
        connection.Statements.Should().BeEmpty("no statement set a role for it, not even the anonymous caller's");
        connection.State.Should().Be(ConnectionState.Closed, "a connection the caller could not be set on is closed rather than left as the login role");
    }

    [Theory]
    [InlineData("""["authenticated"]""")]
    [InlineData("""["analyst","authenticated"]""")]
    [InlineData("""{"name":"authenticated"}""")]
    [InlineData("true")]
    [InlineData("0")]
    public void A_role_claim_that_is_not_text_is_a_role_on_no_list(string role)
    {
        // A list of roles, an object, a number: whatever an identity provider or a forger puts there, it is not
        // read as a token without a role claim, which would be a signed-in user.
        var options = new PostgresRowLevelSecurityOptions { TokenRoles = { [Analyst] = AnalystRole } };
        var caller = Callers.FromClaims($$"""{"sub":"{{Ada}}","role":{{role}}}""");
        var interceptor = new PostgresRowLevelSecurityInterceptor(new CallerOfTheTest { Current = caller }, options);

        var connection = new RecordedConnection();
        var refused = FluentActions.Invoking(() => interceptor.ConnectionOpening(connection, null!, default)).Should().Throw<RefusalException>().Which;

        refused.Code.Should().Be(ToolkitRefusals.RoleNotAllowed);
        refused.Arguments["Role"].Should().Be(role);
        connection.Statements.Should().BeEmpty();
        SetFor(Callers.FromClaims($$"""{"sub":"{{Ada}}","role":null}"""), options).Role.Should().Be("authenticated", "null is no claim at all");
    }

    [Fact]
    public void An_unknown_token_role_that_runs_as_anonymous_leaves_the_tokens_claims_behind()
    {
        var options = new PostgresRowLevelSecurityOptions { UnknownTokenRole = UnknownTokenRole.Anonymous, TokenRoles = { [Analyst] = AnalystRole } };

        SetFor(Callers.FromClaims(ClaimsOf("intern")), options).Should().Be(("anon", """{"role":"anon"}"""), "the database sees a caller who did not sign in: no user id, no claim of the token's");
        SetFor(Callers.FromClaims(ClaimsOf("service_role")), options).Should().Be(("anon", """{"role":"anon"}"""), "a role that names the system's is unknown like any other");
        SetFor(Callers.FromClaims(ClaimsOf(Analyst)), options).Role.Should().Be(AnalystRole, "a role on the list is not unknown");
        SetFor(Callers.FromClaims(ClaimsOf("authenticated")), options).Role.Should().Be("authenticated");

        var renamed = new PostgresRowLevelSecurityOptions { UnknownTokenRole = UnknownTokenRole.Anonymous, AnonymousRole = "visitors" };
        SetFor(Callers.FromClaims(ClaimsOf("intern")), renamed).Should().Be(("visitors", """{"role":"visitors"}"""));
    }

    [Fact]
    public void An_unknown_token_role_that_runs_as_anonymous_is_an_anonymous_caller_to_the_settings_of_a_module()
    {
        // A module's setting made from the caller, as a policy of that module would read it. A user who runs as
        // an anonymous caller must not reach the database through it either.
        var options = new PostgresRowLevelSecurityOptions { UnknownTokenRole = UnknownTokenRole.Anonymous, TokenRoles = { [Analyst] = AnalystRole } };
        var caller = new CallerOfTheTest { Current = Callers.FromClaims(ClaimsOf("intern")) };
        var interceptor = new PostgresRowLevelSecurityInterceptor(caller, options, [new SettingOfTheCaller()]);

        var connection = new RecordedConnection();
        interceptor.ConnectionOpening(connection, null!, default);
        interceptor.ConnectionOpened(connection, null!);

        connection.Statements.Should().ContainSingle().Which.Parameters.Should().Equal(
            ["anon", """{"role":"anon"}""", SettingOfTheCaller.Name, "Anonymous of nobody"],
            "the role, the claims and the module's setting all say a caller who did not sign in");

        // Asked again before a transaction on the open connection, the answer is the same, so nothing is sent.
        ((Microsoft.EntityFrameworkCore.Diagnostics.IDbTransactionInterceptor)interceptor).TransactionStarting(connection, null!, default);
        connection.Statements.Should().ContainSingle("the caller is the one the connection was opened for, and still an anonymous one to the settings");

        // A role on a list is the user it names, to the settings as to the database.
        caller.Current = Callers.FromClaims(ClaimsOf(Analyst));
        var mapped = new RecordedConnection();
        interceptor.ConnectionOpened(mapped, null!);
        mapped.Statements.Should().ContainSingle().Which.Parameters.Should().Equal(AnalystRole, ClaimsOf(Analyst), SettingOfTheCaller.Name, $"User of {Ada}");

        caller.Current = Caller.Anonymous;
        var nobody = new RecordedConnection();
        interceptor.ConnectionOpened(nobody, null!);
        nobody.Statements.Should().ContainSingle().Which.Parameters.Should().Equal(
            ["anon", """{"role":"anon"}""", SettingOfTheCaller.Name, "Anonymous of nobody"],
            "which is exactly what a request without a user gets");
    }

    public static TheoryData<string, string, string?, string?, string> UnmappableRoles() => new()
    {
        // token role, mapped role, SystemRole, SystemInRole, what the refusal says
        { Analyst, "service_role", "service_role", "ddd_system_in", "TokenRoles maps the token role 'analyst' to 'service_role', which is the SystemRole*" },
        { Analyst, "ddd_system_in", null, "ddd_system_in", "The token role 'analyst' is mapped to 'ddd_system_in'. That is the scoped system role*" },
        { Analyst, "desk_scoped", null, "desk_scoped", "The token role 'analyst' is mapped to 'desk_scoped'. That is the scoped system role*" },
        { Analyst, "ddd_system_in", null, null, "The token role 'analyst' is mapped to 'ddd_system_in'. That is the scoped system role*" },
        { Analyst, "anon", null, "ddd_system_in", "The token role 'analyst' is mapped to 'anon'. That is the role of callers who did not sign in*" },
        { Analyst, "PUBLIC", null, "ddd_system_in", "The token role 'analyst' is mapped to 'PUBLIC'. PUBLIC is every role there is*" },
        { Analyst, "none", null, "ddd_system_in", "The token role 'analyst' is mapped to 'none'. 'none' is no role*" },
        { Analyst, "", null, "ddd_system_in", "The token role 'analyst' is mapped to ''. A role has a name*" },
        { Analyst, "@user", null, "ddd_system_in", "The token role 'analyst' is mapped to '@user'. '@user' starts with '@'*" },
        { Analyst, "pg_read_all_data", null, "ddd_system_in", "The token role 'analyst' is mapped to 'pg_read_all_data'. 'pg_read_all_data' starts with pg_*" },
        { Analyst, "desk$analyst", null, "ddd_system_in", "The token role 'analyst' is mapped to 'desk$analyst'. 'desk$analyst' has a '$'*" },
        { "", AnalystRole, null, "ddd_system_in", "A token role mapped to 'desk_analyst' has no name*" },
        { "  ", AnalystRole, null, "ddd_system_in", "A token role mapped to 'desk_analyst' has no name*" },
    };

    [Theory]
    [MemberData(nameof(UnmappableRoles))]
    public void A_mapped_token_role_cannot_be_the_system_or_the_scoped_system_role(string tokenRole, string role, string? systemRole, string? systemInRole, string says)
    {
        PostgresRowLevelSecurityOptions Options()
        {
            var options = new PostgresRowLevelSecurityOptions { SystemRole = systemRole, SystemInRole = systemInRole };
            options.TokenRoles[tokenRole] = role;
            return options;
        }

        // Wherever the options are taken, they are refused: by the registration, by an interceptor made by hand,
        // by the setup script and by the roles a script is written with.
        foreach (var taken in (Action[])
            [
                () => new ServiceCollection().AddPostgresRowLevelSecurity(options =>
                {
                    options.SystemRole = systemRole;
                    options.SystemInRole = systemInRole;
                    options.TokenRoles[tokenRole] = role;
                }),
                () => _ = new PostgresRowLevelSecurityInterceptor(new CallerOfTheTest(), Options()),
                () => PostgresRowAccess.SetupScript(Options()),
                () => RowAccessRoleNames.From(Options()),
            ])
        {
            taken.Should().Throw<ArgumentException>().WithMessage(says).Which.ParamName.Should().Be(nameof(PostgresRowLevelSecurityOptions.TokenRoles));
        }
    }

    [Fact]
    public void A_token_role_may_be_mapped_to_the_users_role_and_two_to_one_role()
    {
        var options = new PostgresRowLevelSecurityOptions { TokenRoles = { ["member"] = "authenticated", [Analyst] = AnalystRole, ["examiner"] = AnalystRole } };

        SetFor(Callers.FromClaims(ClaimsOf("member")), options).Role.Should().Be("authenticated");

        // The user's role is the host's to make, and no check of a role of its own is written for it.
        var setup = PostgresRowAccess.SetupScript(options);
        setup.Should().Contain("GRANT \"anon\", \"authenticated\", \"ddd_system_in\", \"desk_analyst\" TO CURRENT_USER;", "each role is made once");
        setup.Should().NotContain("The role authenticated exists");
    }

    [Fact]
    public void The_roles_of_a_script_refuse_a_token_role_mapped_to_another_callers_role_where_they_are_used()
    {
        // Set one at a time, as a with-expression does, a mapping is checked on its own; whether it clashes with
        // the others is known only where the roles are used.
        var onItsOwn = () => RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { [Analyst] = "PUBLIC" } };
        onItsOwn.Should().Throw<ArgumentException>().WithMessage("The token role 'analyst' is mapped to 'PUBLIC'*").Which.ParamName.Should().Be("TokenRoles");
        var unnamed = () => RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { [" "] = AnalystRole } };
        unnamed.Should().Throw<ArgumentException>().WithMessage("A token role mapped to 'desk_analyst' has no name*");
        var none = () => RowAccessRoleNames.Default with { TokenRoles = null! };
        none.Should().Throw<ArgumentNullException>();

        foreach (var shared in (string[])["ddd_system_in", "anon"])
        {
            var roles = RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { [Analyst] = shared } };

            FluentActions.Invoking(() => roles.Resolve(RowAccessRoles.User)).Should().Throw<InvalidOperationException>().WithMessage($"The token role 'analyst' is mapped to '{shared}'. That is the *");
            FluentActions.Invoking(() => ScriptFor(roles, DeskRules.Owners)).Should().Throw<InvalidOperationException>().WithMessage($"The token role 'analyst' is mapped to '{shared}'*");
        }

        // Changed together, in either order, only where it ends counts.
        var moved = RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { [Analyst] = "ddd_system_in" }, SystemIn = "desk_scoped" };
        moved.Resolve(RowAccessRoles.Token(Analyst)).Should().Be("ddd_system_in", "the scoped system role is another by now");
    }

    [Fact]
    public void The_export_writes_a_mapped_token_role_by_its_database_name()
    {
        RowAccessRoles.Token(Analyst).Should().Be("@token:analyst").And.Be(RowAccessRoles.TokenPrefix + Analyst, "a rule's To, which takes constants, writes it as the prefix and the token role");
        FluentActions.Invoking(() => RowAccessRoles.Token(" ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => RowAccessRoles.Token(null!)).Should().Throw<ArgumentException>();

        var roles = RowAccessRoleNames.From(new PostgresRowLevelSecurityOptions { TokenRoles = { [Analyst] = AnalystRole } });
        roles.Resolve(RowAccessRoles.Token(Analyst)).Should().Be(AnalystRole);
        roles.TokenRoles.Should().Equal(new Dictionary<string, string> { [Analyst] = AnalystRole });

        var script = ScriptFor(roles, DeskRules.Owners, AnalystsReadEveryTicket);

        script.Should().Contain(
            "\n" +
            "CREATE POLICY \"Analysts read every ticket (select) for desk_analyst\" ON desk.\"Tickets\" FOR SELECT TO desk_analyst\n" +
            "    USING (TRUE);\n" +
            "COMMENT ON POLICY \"Analysts read every ticket (select) for desk_analyst\" ON desk.\"Tickets\" IS 'DDDToolkit row access rule';\n",
            "the rule's policy is for the role the token role is mapped to");
        script.Should().Contain("CREATE POLICY \"TicketComment (select) for desk_analyst\" ON desk.\"TicketComment\" FOR SELECT TO desk_analyst\n", "and the tickets' entities follow, as for any role");
        script.Should().NotContain("@token").And.NotContain("TO analyst\n", "the token role's own name is nowhere in the script");
        script.Should().Contain(
            "    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'desk_analyst') THEN\n" +
            "        BEGIN\n" +
            "            CREATE ROLE desk_analyst NOLOGIN NOINHERIT;\n" +
            "        EXCEPTION WHEN duplicate_object OR unique_violation THEN\n" +
            "            NULL; -- made by a script that ran at the same time\n" +
            "        END;\n" +
            "    END IF;\n" +
            "    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'desk_analyst'\n" +
            "               AND (rolbypassrls OR rolsuper OR rolcanlogin)) THEN\n" +
            "        RAISE EXCEPTION USING MESSAGE = 'The role desk_analyst exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.';\n" +
            "    END IF;\n" +
            "    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_class c\n" +
            "               WHERE scoped.rolname = 'desk_analyst' AND c.relkind IN ('r', 'p')\n" +
            "                 AND pg_catalog.pg_has_role(scoped.oid, c.relowner, 'USAGE')) THEN\n" +
            "        RAISE EXCEPTION USING MESSAGE = 'The role desk_analyst has the privileges of a role that owns tables, and Postgres does not hold an owner to row level security. It must neither own a table nor be granted a role that does.';\n" +
            "    END IF;\n" +
            "    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_roles callers\n" +
            "               WHERE scoped.rolname = 'desk_analyst' AND callers.rolname IN ('authenticated', 'anon')\n" +
            "                 AND pg_catalog.pg_has_role(callers.oid, scoped.oid, 'USAGE')) THEN\n" +
            "        RAISE EXCEPTION USING MESSAGE = 'The role desk_analyst is granted to authenticated or anon, so their callers would get every policy written for it. Grant it only to the role the application logs in as.';\n" +
            "    END IF;\n" +
            "    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_roles callers\n" +
            "               WHERE scoped.rolname = 'desk_analyst' AND callers.rolname IN ('authenticated', 'anon')\n" +
            "                 AND pg_catalog.pg_has_role(scoped.oid, callers.oid, 'USAGE')) THEN\n" +
            "        RAISE EXCEPTION USING MESSAGE = 'The role desk_analyst has the privileges of authenticated or anon, so the holder of a token mapped to it would get every policy and every privilege written for those callers. Take that grant back: a mapped role has what is written for it and no more.';\n" +
            "    END IF;\n" +
            "    IF NOT pg_catalog.pg_has_role(CURRENT_USER, 'desk_analyst'::pg_catalog.name,\n" +
            "            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN\n" +
            "        BEGIN\n" +
            "            GRANT desk_analyst TO CURRENT_USER;\n" +
            "        EXCEPTION WHEN unique_violation THEN\n" +
            "            NULL; -- granted by a script that ran at the same time\n" +
            "        END;\n" +
            "    END IF;\n",
            "the role a policy names is made where it is missing, refused where it could void or spread its policies or has another caller's, and granted to whoever runs the script");
        script.Should().Contain("        GRANT USAGE ON SCHEMA ddd TO desk_analyst;\n");
        script.IndexOf("CREATE ROLE desk_analyst", StringComparison.Ordinal).Should().BeLessThan(
            script.IndexOf("GRANT USAGE ON SCHEMA ddd TO desk_analyst", StringComparison.Ordinal), "the role exists before anything is granted to it");
    }

    [Fact]
    public void A_script_makes_a_mapped_role_only_when_a_policy_names_it()
    {
        var mapped = RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { [Analyst] = AnalystRole } };

        ScriptFor(mapped, DeskRules.Owners).Should().Be(ScriptFor(RowAccessRoleNames.Default, DeskRules.Owners), "no policy is for the mapped role, so the script is what it is without the map");

        // The scoped system role is held apart from every mapped role, whether a policy of this script names one or not.
        var scoped = ScriptFor(mapped, DeskRules.Owners, ScopedWork);
        scoped.Should().Contain("WHERE scoped.rolname = 'ddd_system_in' AND callers.rolname IN ('authenticated', 'anon', 'desk_analyst')\n")
            .And.Contain("'The role ddd_system_in is granted to authenticated, anon or desk_analyst, so their callers would get every policy written for it. Grant it only to the role the application logs in as.'");
        scoped.Should().NotContain("CREATE ROLE desk_analyst", "that script names the scoped system role alone");
        scoped.Should().NotContain("pg_catalog.pg_has_role(scoped.oid, callers.oid, 'USAGE')", "and only a mapped role is kept from having a caller's privileges: no token picks the scoped system role");
        ScriptFor(RowAccessRoleNames.Default, DeskRules.Owners, ScopedWork).Should().Contain("callers.rolname IN ('authenticated', 'anon')\n")
            .And.NotContain(AnalystRole, "without a map the check is what it was");

        // A token role mapped to the user's role is that role: its rule joins the user's policy, and nothing is made.
        var members = RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { ["member"] = "authenticated" } };
        var joined = ScriptFor(members, DeskRules.Owners, RowAccessRule.For<Ticket>("Members read every ticket", RowOperations.Read, "TRUE", RowAccessRoles.Token("member")));
        joined.Should().Contain("-- Tickets (select) for authenticated asks the rules 'Members read every ticket' and 'Owners have their tickets': a row one of them allows is allowed.\n")
            .And.NotContain("CREATE ROLE");
    }

    [Fact]
    public void A_rule_for_an_unmapped_token_role_is_refused_by_the_export()
    {
        var unmapped = () => ScriptFor(RowAccessRoleNames.Default, DeskRules.Owners, AnalystsReadEveryTicket);
        unmapped.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Analysts read every ticket' is for '@token:analyst', which no policy can be for. '@token:analyst' is the token role 'analyst', which is mapped to no database role, so no query ever runs as it. " +
            "Map it in PostgresRowLevelSecurityOptions.TokenRoles and write the script with RowAccessRoleNames.From(options), or, for the Supabase export, add 'token:analyst=<role>' to SupabaseRowAccessRoles.");

        // A token role is matched as the token spells it.
        var otherCase = () => ScriptFor(RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { ["Analysts"] = AnalystRole } }, AnalystsReadEveryTicket);
        otherCase.Should().Throw<InvalidOperationException>().WithMessage("The rule 'Analysts read every ticket' is for '@token:analyst'*mapped to no database role*");

        var resolve = () => RowAccessRoleNames.Default.Resolve(RowAccessRoles.Token(Analyst));
        resolve.Should().Throw<ArgumentException>().WithMessage("'@token:analyst' is the token role 'analyst', which is mapped to no database role*");
        var noName = () => RowAccessRoleNames.Default.Resolve(RowAccessRoles.TokenPrefix);
        noName.Should().Throw<ArgumentException>().WithMessage("'@token:' is the token role '', which is mapped to no database role*");

        // A contribution that writes for a token role nobody mapped is refused the same way, naming what it wrote.
        using var model = DeskContext.Create();
        var contributed = () => PostgresRowAccess.Script(model, [DeskRules.Owners], [DeskRules.IsWatcher], new RowAccessExport { Contributions = [new AnalystDesk()] });
        contributed.Should().Throw<InvalidOperationException>().WithMessage(
            $"The policy 'Analysts read the tickets' of the row access contribution {typeof(AnalystDesk).FullName} is for '@token:analyst', which no policy or grant can be for. '@token:analyst' is the token role 'analyst', which is mapped to no database role*");
        PostgresRowAccess.Script(model, [DeskRules.Owners], [DeskRules.IsWatcher], new RowAccessExport
        {
            Roles = RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { [Analyst] = AnalystRole } },
            Contributions = [new AnalystDesk()],
        }).Should().Contain("ON desk.\"Tickets\" FOR SELECT TO desk_analyst\n", "mapped, a contribution's policy is for the mapped role as a rule's is");
    }

    [Fact]
    public void The_setup_script_makes_each_mapped_role_and_holds_it_as_it_holds_the_scoped_system_role()
    {
        var setup = PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions { TokenRoles = { [Analyst] = AnalystRole } });

        setup.Should().Contain(
            "    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'desk_analyst') THEN\n" +
            "        BEGIN\n" +
            "            CREATE ROLE \"desk_analyst\" NOLOGIN NOINHERIT;\n",
            "made as the others are, without a login");
        setup.Should().Contain("'The role desk_analyst exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.'")
            .And.Contain("'The role desk_analyst has the privileges of a role that owns tables")
            .And.Contain("'The role desk_analyst is granted to authenticated or anon, so their callers would get every policy written for it.");
        setup.Should().Contain(
            "    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_roles callers\n" +
            "               WHERE scoped.rolname = 'desk_analyst' AND callers.rolname IN ('authenticated', 'anon')\n" +
            "                 AND pg_catalog.pg_has_role(scoped.oid, callers.oid, 'USAGE')) THEN\n" +
            "        RAISE EXCEPTION USING MESSAGE = 'The role desk_analyst has the privileges of authenticated or anon, so the holder of a token mapped to it would get every policy and every privilege written for those callers. Take that grant back: a mapped role has what is written for it and no more.';\n" +
            "    END IF;\n",
            "and the other way round: the mapped role has neither a signed-in user's privileges nor an anonymous caller's");
        setup.Should().Contain("'The role ddd_system_in is granted to authenticated, anon or desk_analyst", "the scoped system role's privileges must not reach the holders of a token either");
        setup.Split("pg_catalog.pg_has_role(scoped.oid, callers.oid, 'USAGE')").Should().HaveCount(2, "that is asked of the mapped role alone, not of the scoped system role");
        setup.Should().Contain("GRANT \"anon\", \"authenticated\", \"ddd_system_in\", \"desk_analyst\" TO CURRENT_USER;", "the login role may switch to it");
        setup.Should().Contain("GRANT USAGE ON SCHEMA ddd TO \"anon\", \"authenticated\", \"ddd_system_in\", \"desk_analyst\";", "and it may ask the caller functions");
        PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions { TokenRoles = { [Analyst] = AnalystRole } }, "desk_app")
            .Should().Contain("GRANT \"anon\", \"authenticated\", \"ddd_system_in\", \"desk_analyst\" TO \"desk_app\";");

        PostgresRowAccess.SetupScript().Should().Be(PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions()))
            .And.NotContain(AnalystRole)
            .And.NotContain("pg_catalog.pg_has_role(scoped.oid, callers.oid, 'USAGE')")
            .And.Contain("'The role ddd_system_in is granted to authenticated or anon, so their callers", "without a map the script is what it was");
    }

    [Fact]
    public void Role_names_are_compared_by_the_roles_they_name()
    {
        var one = RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { [Analyst] = AnalystRole, ["examiner"] = "desk_examiner" } };
        var other = RowAccessRoleNames.From(new PostgresRowLevelSecurityOptions { TokenRoles = { ["examiner"] = "desk_examiner", [Analyst] = AnalystRole } });

        one.Should().Be(other, "the same mappings, in whatever order they were added");
        one.GetHashCode().Should().Be(other.GetHashCode());
        one.Should().NotBe(RowAccessRoleNames.Default).And.NotBe(one with { TokenRoles = new Dictionary<string, string> { [Analyst] = AnalystRole } });
        one.Should().NotBe(one with { TokenRoles = new Dictionary<string, string> { [Analyst] = "desk_examiner", ["examiner"] = AnalystRole } }, "the same roles mapped the other way round are other mappings");
        (RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string>() }).Should().Be(RowAccessRoleNames.Default);
        one.ToString().Should().Be("RowAccessRoleNames { User = authenticated, Anonymous = anon, SystemIn = ddd_system_in, TokenRoles = [analyst=desk_analyst, examiner=desk_examiner] }");

        // A map handed over is copied: changing it afterwards changes no roles.
        var map = new Dictionary<string, string> { [Analyst] = AnalystRole };
        var kept = RowAccessRoleNames.Default with { TokenRoles = map };
        map[Analyst] = "postgres";
        map["intern"] = "postgres";
        kept.TokenRoles.Should().Equal(new Dictionary<string, string> { [Analyst] = AnalystRole });
        (kept.TokenRoles as IDictionary<string, string>)?.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void The_interceptor_keeps_the_roles_it_was_built_with()
    {
        var options = new PostgresRowLevelSecurityOptions { TokenRoles = { [Analyst] = AnalystRole } };
        var caller = new CallerOfTheTest { Current = Callers.FromClaims(ClaimsOf(Analyst)) };
        var interceptor = new PostgresRowLevelSecurityInterceptor(caller, options);

        // Whatever is done to the host's options afterwards, checked or not, reaches no connection.
        options.TokenRoles[Analyst] = "none";
        options.TokenRoles["intern"] = "postgres";
        options.UserRole = "none";
        options.UnknownTokenRole = UnknownTokenRole.Anonymous;

        var connection = new RecordedConnection();
        interceptor.ConnectionOpened(connection, null!);
        connection.Statements.Should().ContainSingle().Which.Parameters[0].Should().Be(AnalystRole);

        caller.Current = Callers.FromClaims(ClaimsOf("intern"));
        FluentActions.Invoking(() => interceptor.ConnectionOpened(new RecordedConnection(), null!)).Should().Throw<RefusalException>("the map, and what an unknown role gets, are those it was built with");
        caller.Current = Caller.User(Ada);
        var user = new RecordedConnection();
        interceptor.ConnectionOpened(user, null!);
        user.Statements.Should().ContainSingle().Which.Parameters[0].Should().Be("authenticated");

        FluentActions.Invoking(() => new PostgresRowLevelSecurityInterceptor(caller, new PostgresRowLevelSecurityOptions { UnknownTokenRole = (UnknownTokenRole)7 }))
            .Should().Throw<ArgumentOutOfRangeException>("what an unknown role gets is one of the two");
    }

    [Fact]
    public void The_registration_takes_the_token_roles_of_the_host()
    {
        using var provider = new ServiceCollection()
            .AddPostgresRowLevelSecurity(options => options.TokenRoles[Analyst] = AnalystRole)
            .BuildServiceProvider();

        provider.GetRequiredService<PostgresRowLevelSecurityOptions>().TokenRoles.Should().Equal(new Dictionary<string, string> { [Analyst] = AnalystRole });
        provider.GetRequiredService<PostgresRowLevelSecurityInterceptor>().Should().NotBeNull();

        using var supabase = new ServiceCollection()
            .AddSupabaseRowLevelSecurity(options => options.TokenRoles[Analyst] = AnalystRole)
            .BuildServiceProvider();
        RowAccessRoleNames.From(supabase.GetRequiredService<PostgresRowLevelSecurityOptions>()).Resolve(RowAccessRoles.Token(Analyst)).Should().Be(AnalystRole);
    }

    /// <summary>The role and the claims the interceptor sets on a connection it opened for <paramref name="caller"/>.</summary>
    private static (string Role, string Claims) SetFor(Caller caller, PostgresRowLevelSecurityOptions options)
    {
        var interceptor = new PostgresRowLevelSecurityInterceptor(new CallerOfTheTest { Current = caller }, options);
        var connection = new RecordedConnection();

        interceptor.ConnectionOpening(connection, null!, default);
        interceptor.ConnectionOpened(connection, null!);

        var parameters = connection.Statements.Should().ContainSingle("the role and the claims are one statement").Which.Parameters;
        return (parameters[0]!, parameters[1]!);
    }

    /// <summary>The claims of a token signed for Ada with <paramref name="role"/>, or with no role claim at all.</summary>
    private static string ClaimsOf(string? role)
        => role is null
            ? $$"""{"sub":"{{Ada}}","aud":"authenticated"}"""
            : $$"""{"sub":"{{Ada}}","aud":"authenticated","role":{{System.Text.Json.JsonSerializer.Serialize(role)}}}""";

    /// <summary>The script for the desk with <paramref name="rules"/> and <paramref name="roles"/>, which reads no database.</summary>
    private static string ScriptFor(RowAccessRoleNames roles, params RowAccessRule[] rules)
    {
        using var model = DeskContext.Create();
        return PostgresRowAccess.Script(model, rules, [DeskRules.IsWatcher], new RowAccessExport { Roles = roles });
    }

    /// <summary>A module's setting that says who is calling: the kind of caller and the user's id.</summary>
    private sealed class SettingOfTheCaller : IRowLevelSecuritySettings
    {
        public const string Name = "desk.caller";

        public IReadOnlyCollection<string> Names { get; } = [Name];

        public IEnumerable<KeyValuePair<string, string>> For(Caller caller)
            => [new(Name, $"{caller.Kind} of {caller.UserId?.ToString() ?? "nobody"}")];
    }

    /// <summary>A contribution, as a package would write one, whose policy is for the analysts token role.</summary>
    private sealed class AnalystDesk : IRowAccessContribution
    {
        public string Owner => "analystdesk";

        public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
            => context.Model.FindEntityType(typeof(Ticket)) is { } tickets
                ? new([], [new ContributedPolicy(tickets, "Analysts read the tickets", "SELECT", RowAccessRoles.Token(Analyst), "true", null)], [])
                : null;
    }
}
