using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Together.Tests;

/// <summary>
/// Who reads which seats is the application's to say, with a read rule on its seat class in the place of Tenancy's
/// default, and a member list joined to Tenancy admits a seat by asking Tenancy's seats as the caller. So under a
/// narrower read a member list takes the seats its caller reads, and refuses another as it refuses a seat that does
/// not exist, telling the caller nothing about a seat it may not see. On Postgres alone, where the rule is a policy, in
/// the campus' own schemas and in schemas Postgres keeps only in double quotes.
/// </summary>
public abstract class SeatReadRuleTests(CampusPostgresServer postgres)
{
    /// <summary>The campus's own rules, and one that lets a member read the people of its own part of the college.</summary>
    private static readonly IReadOnlyList<RowAccessRule> WithThePeopleOfTheirUnits =
    [
        .. CampusPostgres.Rules,
        RowAccessRule.For<Seat>("Members read the people of their units", RowOperations.Read, MembersReadThePeopleOfTheirUnits.RowAccessSql),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Tenancy's schema as the access files name it: <c>"tenancy"</c>, <c>"Tenancy"</c>.</summary>
    private string Tenancy => postgres.Quoted(postgres.Schemas.Tenancy);

    [Fact]
    public async Task Under_a_narrower_read_of_the_seats_a_member_list_takes_the_seats_its_caller_reads_and_one_that_manages_seats_takes_any()
    {
        using var campus = await postgres.CreateAsync();
        var data = campus.Scenario;

        // By default every member reads every seat of the college: Owen, at Physics, makes Ned, at Arts, a member.
        await AdmitAsync(campus, data.Owen, data.NedSeat);

        foreach (var script in CampusPostgres.AccessScripts(rules: WithThePeopleOfTheirUnits, schemas: postgres.Schemas))
        {
            await CampusPostgres.ExecuteAsync(campus.ConnectionString!, script, Cancellation);
        }

        // Now Owen reads the people placed at Physics and below, and Ned is none of them: refused as no seat is.
        (await FluentActions.Awaiting(() => campus.Services.AsAsync(data.Owen, CampusScenario.InAlder, provider =>
                new Admission<CourseId, SeatId, CourseRoleId>(provider, CourseMembership.Codes).RequireMemberAsync(data.NedSeat, Cancellation).AsTask()))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(CourseMembership.Codes[MembershipRefusals.MemberNotActive]);
        (await FluentActions.Awaiting(() => campus.Services.AsAsync(data.Owen, CampusScenario.InAlder, provider =>
                new Admission<LabId, SeatId, RoleId>(provider, LabMembership.Codes).RequireMemberAsync(data.NedSeat, Cancellation).AsTask()))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(LabMembership.Codes[MembershipRefusals.MemberNotActive]);

        // Tom, placed at Physics as well, he still makes a member; Pia, at Arts, makes Ned one; and Ada, who manages
        // the college's seats, reads every seat whatever the rule says, and makes anyone a member.
        await AdmitAsync(campus, data.Owen, data.TomSeat);
        await AdmitAsync(campus, data.Pia, data.NedSeat);
        await AdmitAsync(campus, data.Ada, data.NedSeat);

        // Past the application the rule hides Ned's seat from Owen, as a row and through tenant_seats(), which modules
        // and the admission read. That the seat is there it does not hide: the placements keep Tenancy's own read.
        await using var owen = await UserSession.BeginAsync(campus.ConnectionString!, data.Owen, data.Alder);
        (await owen.ListAsync<Guid>($"SELECT \"Id\" FROM {Tenancy}.\"Seats\"")).Should().NotContain(data.NedSeat.Value).And.Contain(data.TomSeat.Value);
        (await owen.ListAsync<Guid>($"SELECT \"Id\" FROM {Tenancy}.tenant_seats()")).Should().NotContain(data.NedSeat.Value).And.Contain(data.TomSeat.Value);
        (await owen.ListAsync<Guid>($"SELECT \"SeatId\" FROM {Tenancy}.\"SeatPlacements\"")).Should().Contain(data.NedSeat.Value);
    }

    [Fact]
    public async Task A_read_rule_on_the_seats_may_ask_who_works_on_the_same_courses_although_the_courses_ask_tenancy_in_turn()
    {
        IReadOnlyList<RowAccessRule> rules =
        [
            .. CampusPostgres.Rules,
            RowAccessRule.For<Seat>("Members read the people they work with", RowOperations.Read, MembersReadThePeopleTheyWorkWith.RowAccessSql),
        ];

        // Tenancy's file runs first, since the courses' functions ask who the calling seat is: its policy on the seats
        // asks the courses' function through one of Tenancy's own, made before the function it asks is there.
        var scripts = CampusPostgres.AccessScripts(rules: rules, schemas: postgres.Schemas);
        var forwarder = Tenancy + ".courses_fellow_members()";
        scripts[0].Should().Contain("SET check_function_bodies = off;\nCREATE OR REPLACE FUNCTION " + forwarder + " RETURNS SETOF uuid\n")
            .And.Contain("SELECT * FROM " + postgres.Quoted(postgres.Schemas.Campus) + ".courses_fellow_members()\n")
            .And.Contain("ARRAY(SELECT " + forwarder + ")");
        scripts[1].Should().Contain("CREATE OR REPLACE FUNCTION " + postgres.Quoted(postgres.Schemas.Campus) + ".courses_fellow_members() RETURNS SETOF uuid");

        using var campus = await postgres.CreateAsync();
        var data = campus.Scenario;
        foreach (var script in scripts)
        {
            await CampusPostgres.ExecuteAsync(campus.ConnectionString!, script, Cancellation);
        }

        // Tom tutors Optics: he reads the people on it, and not Ned, who is on no course, nor Ada, who is on none of his.
        await using (var tom = await UserSession.BeginAsync(campus.ConnectionString!, data.Tom, data.Alder))
        {
            (await tom.ListAsync<Guid>($"SELECT \"Id\" FROM {Tenancy}.\"Seats\""))
                .Should().Contain([data.TomSeat.Value, data.PiaSeat.Value, data.DeeSeat.Value]).And.NotContain([data.NedSeat.Value, data.AdaSeat.Value]);
        }

        // Ned works with nobody, and reads his own seat alone; Ada manages the college's seats, and reads them all.
        await using (var ned = await UserSession.BeginAsync(campus.ConnectionString!, data.Ned, data.Alder))
        {
            (await ned.ListAsync<Guid>($"SELECT \"Id\" FROM {Tenancy}.\"Seats\"")).Should().Equal(data.NedSeat.Value);
        }

        await using (var ada = await UserSession.BeginAsync(campus.ConnectionString!, data.Ada, data.Alder))
        {
            (await ada.ListAsync<Guid>($"SELECT \"Id\" FROM {Tenancy}.\"Seats\"")).Should().Contain([data.NedSeat.Value, data.TomSeat.Value]);
        }

        // And the files run again over a database that has them, as the next export's would.
        foreach (var script in scripts)
        {
            await CampusPostgres.ExecuteAsync(campus.ConnectionString!, script, Cancellation);
        }
    }

    /// <summary>Requires <paramref name="person"/>, in Alder, to be able to make <paramref name="seat"/> a member of a course and of a lab.</summary>
    private static async Task AdmitAsync(CampusApp campus, CampusPerson person, SeatId seat)
        => await campus.Services.AsAsync(person, CampusScenario.InAlder, async provider =>
        {
            await new Admission<CourseId, SeatId, CourseRoleId>(provider, CourseMembership.Codes).RequireMemberAsync(seat, Cancellation);
            await new Admission<LabId, SeatId, RoleId>(provider, LabMembership.Codes).RequireMemberAsync(seat, Cancellation);
        });
}

/// <summary>The rule in the campus' own schemas, <c>tenancy</c> and <c>campus</c>.</summary>
public sealed class SeatReadRuleTestsOnPostgres(CampusPostgres postgres) : SeatReadRuleTests(postgres);

/// <summary>
/// The rule in <c>"Tenancy"</c> and <c>"Campus_Life"</c>: the access files written again over a database that has
/// them already, which find what they made before by the names the catalog keeps, capitals and all.
/// </summary>
public sealed class SeatReadRuleTestsInQuotedSchemas(QuotedCampusPostgres postgres) : SeatReadRuleTests(postgres);
