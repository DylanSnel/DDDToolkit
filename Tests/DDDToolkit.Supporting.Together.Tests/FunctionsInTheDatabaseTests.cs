using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.Postgres;
using DDDToolkit.Supporting.Tenancy.Postgres;
using Npgsql;

namespace DDDToolkit.Supporting.Together.Tests;

/// <summary>
/// The two domains in the database itself, on Postgres: the functions Membership writes for the courses and the
/// labs ask Tenancy's, by the logical names the resources' rules give, and answer what the access questions
/// answer in C#. Nothing joins the two packages' SQL but those names, and the one export that writes both. Each
/// claim holds in the campus' own schemas and in schemas Postgres keeps only in double quotes.
/// </summary>
public abstract class FunctionsInTheDatabaseTests(CampusPostgresServer postgres)
{
    /// <summary>The courses' schema as SQL names it, with the dot a name in it follows: <c>campus.</c>, <c>"Campus_Life".</c>.</summary>
    private string Campus => postgres.Delimited(postgres.Schemas.Campus) + ".";

    /// <summary>Tenancy's schema as SQL names it: <c>tenancy</c>, <c>"Tenancy"</c>.</summary>
    private string Tenancy => postgres.Delimited(postgres.Schemas.Tenancy);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_functions_of_the_courses_answer_what_the_access_questions_answer()
    {
        using var campus = await postgres.CreateAsync();
        var data = campus.Scenario;
        var functions = CourseMembership.Rules.Functions;

        foreach (var asking in data.Everybody)
        {
            // The tenant the connection names is the application's word: named for everybody, a suspended seat included.
            await using var session = await CallerSession.BeginAsync(campus.ConnectionString!, asking.Person, data.TenantNamed(asking.College));

            var seen = data.CoursesSeenBy(asking).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>($"SELECT id FROM {Campus}{functions.Seen}() AS id")).Should().BeEquivalentTo(seen, "of the courses {0} sees", asking);
            (await session.ListAsync<Guid>($"SELECT id FROM {Campus}{functions.AsMember}() AS id"))
                .Should().BeEquivalentTo(data.CoursesAsMember(asking).Select(id => id.Value), "of the courses {0} is on", asking);

            foreach (var key in CampusScenario.CourseKeysAsked)
            {
                var heldInCSharp = await campus.Services.AsAsync(asking.Person, asking.College, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<CourseId>>().Reach(key);
                    return await provider.Campus().Courses.Within(reach).Select(course => course.Id.Value).ToListAsync(Cancellation);
                });
                var held = data.CoursesHeldBy(asking, key).Select(id => id.Value).ToList();

                (await session.ListAsync<Guid>($"SELECT id FROM {Campus}{functions.HeldOn}($1) AS id", key)).Should().BeEquivalentTo(held, "of the courses {0} holds {1} on", asking, key);
                heldInCSharp.Should().BeEquivalentTo(held, "of the courses {0} holds {1} on", asking, key);

                // Through a role alone: one of the college's roles of courses, in use, for a key a member's role can give.
                (await session.ListAsync<Guid>($"SELECT id FROM {Campus}{functions.AsMemberWith}($1) AS id", key))
                    .Should().BeEquivalentTo(data.CoursesWithARoleFor(asking, key).Select(id => id.Value), "of the courses {0} holds {1} on through a role", asking, key);
            }
        }
    }

    [Fact]
    public async Task The_functions_of_the_labs_answer_what_the_access_questions_answer()
    {
        using var campus = await postgres.CreateAsync();
        var data = campus.Scenario;
        var functions = LabMembership.Rules.Functions;

        foreach (var asking in data.Everybody)
        {
            await using var session = await CallerSession.BeginAsync(campus.ConnectionString!, asking.Person, data.TenantNamed(asking.College));

            (await session.ListAsync<Guid>($"SELECT id FROM {Campus}{functions.Seen}() AS id"))
                .Should().BeEquivalentTo(data.LabsSeenBy(asking).Select(id => id.Value), "of the labs {0} sees", asking);
            (await session.ListAsync<Guid>($"SELECT id FROM {Campus}{functions.AsMember}() AS id"))
                .Should().BeEquivalentTo(data.LabsAsMember(asking).Select(id => id.Value), "of the labs {0} works in", asking);

            foreach (var key in CampusScenario.LabKeysAsked)
            {
                var heldInCSharp = await campus.Services.AsAsync(asking.Person, asking.College, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<LabId>>().Reach(key);
                    return await provider.Campus().Labs.Within(reach).Select(lab => lab.Id.Value).ToListAsync(Cancellation);
                });
                var held = data.LabsHeldBy(asking, key).Select(id => id.Value).ToList();

                (await session.ListAsync<Guid>($"SELECT id FROM {Campus}{functions.HeldOn}($1) AS id", key)).Should().BeEquivalentTo(held, "of the labs {0} holds {1} on", asking, key);
                heldInCSharp.Should().BeEquivalentTo(held, "of the labs {0} holds {1} on", asking, key);

                // Through a role alone: one of Tenancy's, in use in the caller's college, for a key a member's role can give.
                (await session.ListAsync<Guid>($"SELECT id FROM {Campus}{functions.AsMemberWith}($1) AS id", key))
                    .Should().BeEquivalentTo(data.LabsWithARoleFor(asking, key).Select(id => id.Value), "of the labs {0} holds {1} on through a role", asking, key);
            }
        }
    }

    [Fact]
    public async Task Past_the_application_a_seat_reads_what_the_functions_answer_and_a_seat_that_does_not_count_reads_nothing()
    {
        using var campus = await postgres.CreateAsync();
        var data = campus.Scenario;

        async Task<List<string>> CoursesAsync(CampusPerson person, TenantId? tenant)
        {
            await using var session = await CallerSession.BeginAsync(campus.ConnectionString!, person, tenant);
            return await session.ListAsync<string>($"SELECT \"Title\" FROM {Campus}\"Courses\" ORDER BY 1");
        }

        // A tutor reads the course it is on; the dean what is given below Science; the administrator the college's.
        (await CoursesAsync(data.Tom, data.Alder)).Should().Equal("Optics");
        (await CoursesAsync(data.Dee, data.Alder)).Should().Equal("Optics");
        (await CoursesAsync(data.Ada, data.Alder)).Should().Equal("Optics", "Poetry");
        (await CoursesAsync(data.Bea, data.Birch)).Should().Equal("Latin");

        // The same person through another seat, a seat that is suspended, a tenant somebody has no seat in, and no tenant at all.
        (await CoursesAsync(data.Tom, data.Birch)).Should().BeEmpty();
        (await CoursesAsync(data.Sue, data.Alder)).Should().BeEmpty();
        (await CoursesAsync(data.Bea, data.Alder)).Should().BeEmpty();
        (await CoursesAsync(data.Ada, null)).Should().BeEmpty();

        // The members and the roles they hold follow the course; the roles of courses are the college's to read.
        await using var tom = await CallerSession.BeginAsync(campus.ConnectionString!, data.Tom, data.Alder);
        (await tom.ListAsync<long>($"SELECT count(*) FROM {Campus}\"CourseTutors\"")).Should().ContainSingle().Which.Should().Be(5, "the owner and the four on Optics");
        (await tom.ListAsync<long>($"SELECT count(*) FROM {Campus}\"CourseRoles\"")).Should().ContainSingle().Which.Should().Be(3, "Alder's three, and none of Birch's");
        (await tom.ListAsync<string>($"SELECT \"Name\" FROM {Campus}\"Labs\"")).Should().Equal("Laser lab");
    }

    [Fact]
    public async Task The_functions_ask_tenancys_own_by_the_names_they_have_where_they_are_defined()
    {
        using var campus = await postgres.CreateAsync();

        await using var connection = new NpgsqlConnection(campus.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT p.proname || ': ' || p.prosrc FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = '{postgres.Schemas.Campus}' ORDER BY 1
            """,
            connection);
        var bodies = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(Cancellation))
        {
            while (await reader.ReadAsync(Cancellation))
            {
                bodies.Add(reader.GetString(0));
            }
        }

        string Body(string name) => bodies.Single(body => body.StartsWith(name + ":", StringComparison.Ordinal));

        // Who the caller is as a member: the calling seat, as Tenancy answers it, in Tenancy's own schema.
        Body("courses_as_member").Should().Contain($"m.\"MemberId\" = (SELECT {Tenancy}.caller_seat())");
        Body("labs_as_member").Should().Contain($"m.\"MemberId\" = (SELECT {Tenancy}.caller_seat())");

        // Which roles give a key: the rows of the courses' own role class, and Tenancy's answer for a lab, each behind
        // what the rules let a member's role give.
        Body("courses_as_member_with").Should()
            .Contain($"\"{postgres.Schemas.Campus}\".\"CourseRoles\"")
            .And.Contain("$1 IN ('courses.see', 'courses.grade', 'courses.outline')")
            .And.NotContain("roles_with_key");
        Body("labs_as_member_with").Should()
            .Contain("$1 IN ('labs.see', 'labs.equip', 'labs.calibrate')")
            .And.Contain($"h.\"RoleId\" IN (SELECT giving.role FROM {Tenancy}.roles_with_key($1) AS giving(role))");

        // Reach from above: where the resource sits, against the units where the calling seat holds the key.
        Body("courses_i_see").Should().Contain($"r.\"UnitId\" IN (SELECT reached.place FROM {Tenancy}.units_where_i_hold('courses.see') AS reached(place))");
        Body("courses_where_i_hold").Should()
            .Contain($"r.\"UnitId\" IN (SELECT reached.place FROM {Tenancy}.units_where_i_hold($1) AS reached(place))")
            .And.Contain($"r.\"OwnerSeatId\" = (SELECT {Tenancy}.caller_seat())");
        Body("labs_where_i_hold").Should().Contain($"{Tenancy}.units_where_i_hold($1)").And.NotContain("\"OwnerSeatId\"", "a lab states no key of its own, so nothing is held by owning one");
    }

    [Fact]
    public void The_access_files_of_both_domains_are_written_together_and_tenancys_come_first()
    {
        var scripts = CampusPostgres.AccessScripts(schemas: postgres.Schemas);

        // One export, two contexts: Tenancy's file defines what the courses' file asks, so it runs first.
        scripts.Should().HaveCount(2);
        scripts[0].Should().Contain($"FUNCTION {Tenancy}.caller_seat()").And.Contain($"FUNCTION {Tenancy}.units_where_i_hold(key text)").And.NotContain("courses_i_see");
        scripts[1].Should().Contain($"FUNCTION {Campus}courses_i_see()").And.Contain($"FUNCTION {Campus}labs_where_i_hold(text)").And.NotContain($"FUNCTION {Tenancy}.");

        // Written with the resources' contributions and without Tenancy's, as for an application that leaves
        // Tenancy's out, nothing is written: the names the rules give are defined nowhere in what the files are
        // written with.
        FluentActions.Invoking(() => CampusPostgres.AccessScripts([new MembershipRowAccessContribution<CourseTutor>(CourseMembership.Rules), new MembershipRowAccessContribution<LabTechnician>(LabMembership.Rules)], schemas: postgres.Schemas))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*asks the function tenancy/*, and none of the functions this is written with is called that*");

        // And one whose rules name a function Tenancy does not have is told which.
        var misnamed = new MembershipRules(
            "courses",
            keys: [CourseKeys.See],
            members: MemberSource.Resolved("tenancy/calling_seat"),
            seeKey: CourseKeys.See,
            memberKeys: MemberKeys.Only(CourseKeys.See),
            rolesKept: true);
        FluentActions.Invoking(() => CampusPostgres.AccessScripts([new TenancyRowAccessContribution(CampusCatalogue.Built), new MembershipRowAccessContribution<CourseTutor>(misnamed), new MembershipRowAccessContribution<LabTechnician>(LabMembership.Rules)], schemas: postgres.Schemas))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*asks the function tenancy/calling_seat, and none of the functions this is written with is called that*");
    }

    [Fact]
    public async Task The_start_up_checks_of_both_domains_pass_over_the_one_database()
    {
        using var campus = await postgres.CreateAsync();

        // Membership's: the functions of the courses and of the labs are in place, written from the rules each is registered with.
        await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(campus.Services.Provider, Cancellation);

        // Tenancy's: its policies, functions and triggers are in place in the schema its tables are in, written from
        // the catalogue the application runs with, the courses' and the labs' keys among it.
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(campus.Services.Provider, Cancellation);
    }

    [Fact]
    public async Task Tenancys_checks_of_its_system_work_pass_in_its_schema_and_find_a_grant_made_there_by_hand()
    {
        using var campus = await postgres.CreateAsync();

        // The reads before any tenant is known, found by their schema and name and asked by Entity Framework through
        // the function's name as the provider writes it; and the scoped system role, kept to its tenant.
        await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(campus.Services.Provider, Cancellation);
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(campus.Services.Provider, Cancellation);

        // A function of Tenancy's that runs as its owner, given to every role by hand: the check looks for such grants
        // in the schema by the name the catalog keeps, capitals and all, and finds it.
        await CampusPostgres.ExecuteAsync(campus.ConnectionString!, $"GRANT EXECUTE ON FUNCTION {Tenancy}.caller_seat() TO PUBLIC", Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(campus.Services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*- every role may execute {Tenancy}.caller_seat(), which runs as its owner: REVOKE EXECUTE ON FUNCTION {Tenancy}.caller_seat() FROM PUBLIC;*");
    }

    [Fact]
    public async Task The_definer_owner_check_finds_the_functions_of_both_schemas_once_a_table_there_is_forced()
    {
        using var campus = await postgres.CreateAsync();
        using var scope = campus.Services.Provider.CreateScope();
        var tenancy = scope.ServiceProvider.GetRequiredService<TenancyContext>();
        var courses = scope.ServiceProvider.GetRequiredService<CampusContext>();

        // The login role made the functions and owns the tables they read, as on a Postgres of your own, and Postgres
        // exempts a table's owner from its policies while the table does not force them: nothing to say.
        await PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(tenancy, Cancellation);
        await PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(courses, Cancellation);

        // One forced table in each schema, and the functions there that run as that owner would read it under the
        // owner's policies: found by the schema the catalog keeps, and named as SQL names them.
        await CampusPostgres.ExecuteAsync(
            campus.ConnectionString!,
            $"ALTER TABLE {Tenancy}.\"Seats\" FORCE ROW LEVEL SECURITY; ALTER TABLE {Campus}\"Courses\" FORCE ROW LEVEL SECURITY;",
            Cancellation);
        await FluentActions.Awaiting(() => PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(tenancy, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Functions in the schemas of 'TenancyContext' run as their owner*- {Tenancy}.caller_seat() is owned by {CampusPostgres.LoginRole}. Fix: ALTER FUNCTION {Tenancy}.caller_seat() OWNER TO*");
        await FluentActions.Awaiting(() => PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(courses, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Functions in the schemas of 'CampusContext' run as their owner*- {Campus}courses_i_see() is owned by {CampusPostgres.LoginRole}. Fix: ALTER FUNCTION {Campus}courses_i_see() OWNER TO*");
    }

    [Fact]
    public async Task The_policies_check_names_a_function_of_tenancys_written_from_another_catalogue_as_sql_names_it()
    {
        using var campus = await postgres.CreateAsync();

        // The live keys as another catalogue would write them, in Tenancy's schema.
        await CampusPostgres.ExecuteAsync(
            campus.ConnectionString!,
            $"CREATE OR REPLACE FUNCTION {Tenancy}.key_is_live(key text) RETURNS boolean LANGUAGE sql IMMUTABLE AS $$ SELECT true $$",
            Cancellation);

        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(campus.Services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"The function {Tenancy}.key_is_live in the database was written from another catalogue: the live keys are *", "a name a developer can look up or paste into SQL as it is");
    }

    [Fact]
    public void Every_function_the_access_files_name_is_named_in_its_schema_as_entity_framework_names_that_schema()
    {
        var scripts = CampusPostgres.AccessScripts(schemas: postgres.Schemas);

        // Where Tenancy's file makes a function, where the courses' functions ask it, and where each file finds the
        // functions it made before, by the schema and the name the catalog keeps: as they are, capitals and all.
        scripts[0].Should().Contain($"CREATE OR REPLACE FUNCTION {Tenancy}.caller_seat()")
            .And.Contain($"({RowAccessModel.Literal(postgres.Schemas.Tenancy)}, 'caller_seat')")
            .And.Contain($"EXECUTE FUNCTION \"{postgres.Schemas.Tenancy}\".rights_follow_grants('grants')", "Tenancy's triggers name their functions with the schema quoted, as every name of the model is");
        scripts[1].Should().Contain($"(SELECT {Tenancy}.caller_seat())")
            .And.Contain($"({RowAccessModel.Literal(postgres.Schemas.Campus)}, 'courses_i_see')");

        // A schema Postgres would fold to lower case without its quotes never goes without them.
        foreach (var schema in new[] { postgres.Schemas.Tenancy, postgres.Schemas.Campus }.Where(schema => postgres.Delimited(schema) != schema))
        {
            var unquoted = new Regex(@"(?<![\w""'.])" + Regex.Escape(schema) + @"\.\w+\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            scripts.Should().AllSatisfy(script => unquoted.Matches(script).Select(match => match.Value).Should().BeEmpty("{0} is a schema Postgres keeps only in double quotes", schema));
        }
    }
}

/// <summary>The functions in the campus' own schemas, <c>tenancy</c> and <c>campus</c>, which SQL writes as they are.</summary>
public sealed class FunctionsInTheDatabaseTestsOnPostgres(CampusPostgres postgres) : FunctionsInTheDatabaseTests(postgres);

/// <summary>The functions in <c>"Tenancy"</c> and <c>"Campus_Life"</c>, schemas Postgres keeps only in double quotes.</summary>
public sealed class FunctionsInTheDatabaseTestsInQuotedSchemas(QuotedCampusPostgres postgres) : FunctionsInTheDatabaseTests(postgres);
