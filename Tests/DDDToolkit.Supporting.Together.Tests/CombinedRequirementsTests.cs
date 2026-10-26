using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Together.Tests;

/// <summary>
/// Requirements that combine cases of both supporting domains, held at the door by one set of checks in which Tenancy's
/// check and the courses' are registered side by side: a permission on a course of Membership's with a permission at a unit of
/// Tenancy's, the caller's own seat or a permission on the course, one caller or another. Each case is decided by its own
/// check, on SQLite and on Postgres, where every statement runs as the caller.
/// </summary>
public abstract class CombinedRequirementsTests(CampusDatabases databases)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Moving_a_course_takes_the_permission_on_the_course_and_the_permission_at_the_unit_it_moves_to()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;

        // Ada administers Alder: every permission on every course, and at every unit.
        await PassesAsync(campus, data.Ada, new CampusRequests.MoveCourse(data.Optics, data.Arts));

        // Dee is the dean of Science: she grades Optics from above, and grades at Physics, below Science; not at Arts.
        await PassesAsync(campus, data.Dee, new CampusRequests.MoveCourse(data.Optics, data.Physics));
        var atArts = await RefusedAsync(campus, data.Dee, new CampusRequests.MoveCourse(data.Optics, data.Arts));
        atArts.Code.Should().Be(TenancyRefusals.NotPermitted, "the first case that fails refuses, with its own reason: the unit's");
        atArts.Arguments["Unit"].Should().Be(data.Arts);

        // Tom tutors Optics, so he grades it, and holds nothing in the organization: the second case refuses him.
        (await RefusedAsync(campus, data.Tom, new CampusRequests.MoveCourse(data.Optics, data.Physics))).Code.Should().Be(TenancyRefusals.NotPermitted);

        // Ned does not see Optics: the course's case refuses him first, as not found, and the unit is never asked.
        campus.Commands.Reset();
        (await RefusedAsync(campus, data.Ned, new CampusRequests.MoveCourse(data.Optics, data.Arts))).Code.Should().Be(CourseMembership.Codes[MembershipRefusals.NotFound]);
    }

    [Fact]
    public async Task A_tutor_takes_themselves_off_a_course_and_somebody_else_only_with_the_permission_on_it()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;

        // Tom leaves Optics himself: his own seat, compared at the door with nothing read once his seat is found, and the
        // course he sees, read once, as the permission's case would read it, so its save is held to what the door read.
        var read = await campus.Services.AsAsync(data.Tom, CampusScenario.InAlder, async provider =>
        {
            campus.Commands.Reset();
            await provider.GetRequiredService<AccessChecks<ICampusRequest>>().RequireAsync(new CampusRequests.LeaveCourse(data.Optics, data.TomSeat), Cancellation);
            Checked<MemberHold<CourseId>>.FindAllInHand().Select(hold => hold.Resource).Should().Equal([data.Optics], "the hold on the course is kept on his own way through too");
            return campus.Commands.Count;
        });
        read.Should().Be(1, "the caller's own is asked first, then whether he sees the course, and the permission is not asked where both hold");

        // Pia convenes Optics, which writes its outline: she takes Tom off with the permission.
        await PassesAsync(campus, data.Pia, new CampusRequests.LeaveCourse(data.Optics, data.TomSeat));

        // Tom may not take Pia off: her seat is not his, and a tutor does not write the outline. One refusal, both reasons.
        var refused = await RefusedAsync(campus, data.Tom, new CampusRequests.LeaveCourse(data.Optics, data.PiaSeat));
        refused.Code.Should().Be(ToolkitRefusals.Refused);
        Reasons(refused).Should().Equal(ToolkitRefusals.NotOwn, CourseMembership.Codes[MembershipRefusals.NotPermitted]);

        // Ned, who does not see Optics, is told it is not found by the permission's case, and not his own seat by the first.
        Reasons(await RefusedAsync(campus, data.Ned, new CampusRequests.LeaveCourse(data.Optics, data.TomSeat)))
            .Should().Equal(ToolkitRefusals.NotOwn, CourseMembership.Codes[MembershipRefusals.NotFound]);

        // Tom's seat in Birch is another seat: in Alder it is not his own.
        Reasons(await RefusedAsync(campus, data.Tom, new CampusRequests.LeaveCourse(data.Optics, data.TomInBirch)))
            .Should().Equal(ToolkitRefusals.NotOwn, CourseMembership.Codes[MembershipRefusals.NotPermitted]);
    }

    [Fact]
    public async Task One_caller_or_another_is_refused_once_with_the_reason_of_each()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;

        // Ada manages seats for the whole of Alder: no operator, and let through by the second case.
        await PassesAsync(campus, data.Ada, new CampusRequests.CollegeAuditLog());

        // Dee is neither an operator nor a manager of seats for the whole college.
        var refused = await RefusedAsync(campus, data.Dee, new CampusRequests.CollegeAuditLog());
        refused.Code.Should().Be(ToolkitRefusals.Refused);
        refused.Kind.Should().Be(RefusalKind.NotPermitted);
        Reasons(refused).Should().Equal(TenancyRefusals.OperatorsOnly, TenancyRefusals.NotPermitted);
        ((IReadOnlyList<RefusalReason>)refused.Arguments[ToolkitRefusals.ReasonsArgument]!)[1].Arguments["Permission"].Should().Be(TenancyPermissions.SeatsManage, "each reason keeps its arguments");
    }

    /// <summary>Holds <paramref name="request"/> to what it requires, as <paramref name="person"/> in Alder, through the campus' set of checks.</summary>
    private static Task PassesAsync(CampusApp campus, CampusPerson person, ICampusRequest request)
        => campus.Services.AsAsync(person, CampusScenario.InAlder, provider => provider.GetRequiredService<AccessChecks<ICampusRequest>>().RequireAsync(request, Cancellation).AsTask());

    /// <summary>The refusal <paramref name="request"/> gets as <paramref name="person"/> in Alder.</summary>
    private static async Task<RefusalException> RefusedAsync(CampusApp campus, CampusPerson person, ICampusRequest request)
        => (await FluentActions.Awaiting(() => PassesAsync(campus, person, request)).Should().ThrowAsync<RefusalException>()).Which;

    /// <summary>The codes of the reasons an <c>access.refused</c> of the door carries, in their order.</summary>
    private static IEnumerable<string> Reasons(RefusalException refused)
        => ((IReadOnlyList<RefusalReason>)refused.Arguments[ToolkitRefusals.ReasonsArgument]!).Select(static reason => reason.Code);
}

/// <summary>The combinations on SQLite, in memory: Tenancy's read model is views over its tables.</summary>
public sealed class CombinedRequirementsTestsOnSqlite() : CombinedRequirementsTests(CampusDatabases.Sqlite);

/// <summary>The combinations on Postgres, under row level security: every statement of the checks runs as the caller.</summary>
public sealed class CombinedRequirementsTestsOnPostgres(CampusPostgres postgres) : CombinedRequirementsTests(postgres);
