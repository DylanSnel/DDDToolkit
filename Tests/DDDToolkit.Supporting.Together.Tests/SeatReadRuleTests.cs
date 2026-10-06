using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Together.Tests;

/// <summary>
/// Who reads which seats is the application's to say, with a read rule on its seat class in the place of Tenancy's
/// default, and a member list joined to Tenancy admits a seat by asking Tenancy's seats as the caller. So under a
/// narrower read a member list takes the seats its caller reads, and refuses another as it refuses a seat that does
/// not exist, telling the caller nothing about a seat it may not see. On Postgres alone, where the rule is a policy.
/// </summary>
public sealed class SeatReadRuleTests(CampusPostgres postgres)
{
    /// <summary>The campus's own rules, and one that lets a member read the people of its own part of the college.</summary>
    private static readonly IReadOnlyList<RowAccessRule> WithThePeopleOfTheirUnits =
    [
        .. CampusPostgres.Rules,
        RowAccessRule.For<Seat>("Members read the people of their units", RowOperations.Read, MembersReadThePeopleOfTheirUnits.RowAccessSql),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Under_a_narrower_read_of_the_seats_a_member_list_takes_the_seats_its_caller_reads_and_one_that_manages_seats_takes_any()
    {
        using var campus = await postgres.CreateAsync();
        var data = campus.Scenario;

        // By default every member reads every seat of the college: Owen, at Physics, makes Ned, at Arts, a member.
        await AdmitAsync(campus, data.Owen, data.NedSeat);

        foreach (var script in CampusPostgres.AccessScripts(rules: WithThePeopleOfTheirUnits))
        {
            await CampusPostgres.ExecuteAsync(campus.ConnectionString!, script, Cancellation);
        }

        // Now Owen reads the people placed at Physics and below, and Ned is none of them: refused as no seat is.
        (await FluentActions.Awaiting(() => campus.Services.AsAsync(data.Owen, CampusScenario.InAlder, provider =>
                provider.GetRequiredService<MemberAdmission<CourseId, SeatId, CourseRoleId>>().RequireMemberAsync(data.NedSeat, Cancellation).AsTask()))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(CourseMembership.Codes[MembershipRefusals.MemberNotActive]);
        (await FluentActions.Awaiting(() => campus.Services.AsAsync(data.Owen, CampusScenario.InAlder, provider =>
                provider.GetRequiredService<MemberAdmission<LabId, SeatId, RoleId>>().RequireMemberAsync(data.NedSeat, Cancellation).AsTask()))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(LabMembership.Codes[MembershipRefusals.MemberNotActive]);

        // Tom, placed at Physics as well, he still makes a member; Pia, at Arts, makes Ned one; and Ada, who manages
        // the college's seats, reads every seat whatever the rule says, and makes anyone a member.
        await AdmitAsync(campus, data.Owen, data.TomSeat);
        await AdmitAsync(campus, data.Pia, data.NedSeat);
        await AdmitAsync(campus, data.Ada, data.NedSeat);

        // Past the application the rule hides Ned's seat from Owen, as a row and through tenant_seats(), which modules
        // and the admission read. That the seat is there it does not hide: the placements keep Tenancy's own read.
        await using var owen = await CallerSession.BeginAsync(campus.ConnectionString!, data.Owen, data.Alder);
        (await owen.ListAsync<Guid>("SELECT \"Id\" FROM tenancy.\"Seats\"")).Should().NotContain(data.NedSeat.Value).And.Contain(data.TomSeat.Value);
        (await owen.ListAsync<Guid>("SELECT \"Id\" FROM tenancy.tenant_seats()")).Should().NotContain(data.NedSeat.Value).And.Contain(data.TomSeat.Value);
        (await owen.ListAsync<Guid>("SELECT \"SeatId\" FROM tenancy.\"SeatPlacements\"")).Should().Contain(data.NedSeat.Value);
    }

    /// <summary>Requires <paramref name="person"/>, in Alder, to be able to make <paramref name="seat"/> a member of a course and of a lab.</summary>
    private static async Task AdmitAsync(CampusApp campus, CampusPerson person, SeatId seat)
        => await campus.Services.AsAsync(person, CampusScenario.InAlder, async provider =>
        {
            await provider.GetRequiredService<MemberAdmission<CourseId, SeatId, CourseRoleId>>().RequireMemberAsync(seat, Cancellation);
            await provider.GetRequiredService<MemberAdmission<LabId, SeatId, RoleId>>().RequireMemberAsync(seat, Cancellation);
        });
}
