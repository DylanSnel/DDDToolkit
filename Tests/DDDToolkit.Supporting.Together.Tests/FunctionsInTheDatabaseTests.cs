using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.Postgres;
using DDDToolkit.Supporting.Tenancy.Postgres;
using Npgsql;

namespace DDDToolkit.Supporting.Together.Tests;

/// <summary>
/// The two domains in the database itself, on Postgres: the functions Membership writes for the courses and the
/// labs ask Tenancy's, by the logical names the resources' rules give, and answer what the access questions
/// answer in C#. Nothing joins the two packages' SQL but those names, and the one export that writes both.
/// </summary>
public sealed class FunctionsInTheDatabaseTests(CampusPostgres postgres)
{
    private const string Campus = CampusContext.Schema + ".";

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
            WHERE n.nspname = '{CampusContext.Schema}' ORDER BY 1
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
        Body("courses_as_member").Should().Contain("m.\"MemberId\" = (SELECT tenancy.caller_seat())");
        Body("labs_as_member").Should().Contain("m.\"MemberId\" = (SELECT tenancy.caller_seat())");

        // Which roles give a key: the rows of the courses' own role class, and Tenancy's answer for a lab, each behind
        // what the rules let a member's role give.
        Body("courses_as_member_with").Should()
            .Contain("\"campus\".\"CourseRoles\"")
            .And.Contain("$1 IN ('courses.see', 'courses.grade', 'courses.outline')")
            .And.NotContain("roles_with_key");
        Body("labs_as_member_with").Should()
            .Contain("$1 IN ('labs.see', 'labs.equip', 'labs.calibrate')")
            .And.Contain("h.\"RoleId\" IN (SELECT giving.role FROM tenancy.roles_with_key($1) AS giving(role))");

        // Reach from above: where the resource sits, against the units where the calling seat holds the key.
        Body("courses_i_see").Should().Contain("r.\"UnitId\" IN (SELECT reached.place FROM tenancy.units_where_i_hold('courses.see') AS reached(place))");
        Body("courses_where_i_hold").Should()
            .Contain("r.\"UnitId\" IN (SELECT reached.place FROM tenancy.units_where_i_hold($1) AS reached(place))")
            .And.Contain("r.\"OwnerSeatId\" = (SELECT tenancy.caller_seat())");
        Body("labs_where_i_hold").Should().Contain("tenancy.units_where_i_hold($1)").And.NotContain("\"OwnerSeatId\"", "a lab states no key of its own, so nothing is held by owning one");
    }

    [Fact]
    public void The_access_files_of_both_domains_are_written_together_and_tenancys_come_first()
    {
        var scripts = CampusPostgres.AccessScripts();

        // One export, two contexts: Tenancy's file defines what the courses' file asks, so it runs first.
        scripts.Should().HaveCount(2);
        scripts[0].Should().Contain("FUNCTION tenancy.caller_seat()").And.Contain("FUNCTION tenancy.units_where_i_hold(key text)").And.NotContain("courses_i_see");
        scripts[1].Should().Contain("FUNCTION campus.courses_i_see()").And.Contain("FUNCTION campus.labs_where_i_hold(text)").And.NotContain("FUNCTION tenancy.");

        // A host that lists the resources' contributions and forgets Tenancy's writes nothing: the names the rules
        // give are defined nowhere in what the files are written with.
        FluentActions.Invoking(() => CampusPostgres.AccessScripts([new CourseMembershipFunctions(), new LabMembershipFunctions()]))
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
        FluentActions.Invoking(() => CampusPostgres.AccessScripts([new CampusTenancyRowAccess(), new MembershipRowAccessContribution<CourseTutor>(misnamed), new LabMembershipFunctions()]))
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
}
