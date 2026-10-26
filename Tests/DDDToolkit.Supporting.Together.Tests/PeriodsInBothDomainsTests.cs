using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Together.Tests;

/// <summary>
/// Time in both domains at once: a role given in the organization for a while, which Tenancy keeps, and a
/// place on a member list for a while, which Membership keeps. Each ends at its own moment, and what a caller
/// holds on a resource follows both, to the tick.
/// <para>
/// On SQLite alone, where the application's clock is the only one and a test moves it. On a server the
/// functions and the policies ask the database's own clock, which no test sets.
/// </para>
/// </summary>
public sealed class PeriodsInBothDomainsTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_role_given_in_the_organization_for_a_while_reaches_a_course_and_a_lab_until_the_moment_it_ends()
    {
        using var campus = await CampusDatabases.Sqlite.CreateAsync();
        var data = campus.Scenario;
        var clock = campus.Clock!;
        var ned = new Asking(data.Ned, CampusScenario.InAlder);
        var threeDays = TimeSpan.FromDays(3);

        // Ned has a seat at Arts, is on no list and holds nothing: Poetry and the darkroom, which are there, are not there for him.
        (await HoldOnAsync(campus, ned, data.Poetry, CourseKeys.Grade)).Should().BeNull();
        (await HoldInAsync(campus, ned, data.Darkroom, LabKeys.Equip)).Should().BeNull();

        // He is made a dean of Arts for three days.
        await campus.Services.BySystemInAsync(
            data.Alder,
            provider => provider.Seats().GrantAsync(data.NedSeat, data.Arts, data.AlderDean, until: data.Now + threeDays, reason: null, Cancellation));

        // From then on he grades what is given at Arts. The hold names no end: when a role of the organization ends is
        // Tenancy's to know, so a key held from above is held for as long as the course is seen.
        var grade = await HoldOnAsync(campus, ned, data.Poetry, CourseKeys.Grade);
        (grade!.Via, grade.Until).Should().Be((MemberVia.Above, (DateTimeOffset?)null));
        (await HoldInAsync(campus, ned, data.Darkroom, LabKeys.Equip))!.Via.Should().Be(MemberVia.Above);
        (await HoldOnAsync(campus, ned, data.Optics, CourseKeys.Grade)).Should().BeNull("Optics is given at Physics, which is not below Arts");

        // One tick before the role ends he still does, and passes where a question refuses.
        clock.Advance(threeDays - Tick);
        (await HoldOnAsync(campus, ned, data.Poetry, CourseKeys.Grade))!.Via.Should().Be(MemberVia.Above);
        (await HoldInAsync(campus, ned, data.Darkroom, LabKeys.Equip))!.Via.Should().Be(MemberVia.Above);
        await campus.Services.CoursesAsync(data.Ned, CampusScenario.InAlder, access => access.RequireAsync(data.Poetry, CourseKeys.Grade, Cancellation));

        // At the moment it ends the course and the lab are not there for him: he saw them through that role alone.
        clock.Advance(Tick);
        (await HoldOnAsync(campus, ned, data.Poetry, CourseKeys.Grade)).Should().BeNull();
        (await HoldOnAsync(campus, ned, data.Poetry, CourseKeys.See)).Should().BeNull();
        (await HoldInAsync(campus, ned, data.Darkroom, LabKeys.Equip)).Should().BeNull();
        (await FluentActions.Awaiting(() => campus.Services.CoursesAsync(data.Ned, CampusScenario.InAlder, access => access.RequireAsync(data.Poetry, CourseKeys.Grade, Cancellation)))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(CourseMembership.Codes[MembershipRefusals.NotFound]);

        // His seat is as active as it was: he is refused for the course, and not as nobody.
        await campus.Services.AsAsync(data.Owen, CampusScenario.InAlder, provider =>
            provider.GetRequiredService<MemberAdmission<CourseId, SeatId, CourseRoleId>>().RequireMemberAsync(data.NedSeat, Cancellation).AsTask());
    }

    [Fact]
    public async Task A_place_on_a_member_list_that_ends_leaves_what_the_organization_gives_and_nothing_else()
    {
        using var campus = await CampusDatabases.Sqlite.CreateAsync();
        var data = campus.Scenario;
        var clock = campus.Clock!;
        var ada = new Asking(data.Ada, CampusScenario.InAlder);
        var pia = new Asking(data.Pia, CampusScenario.InAlder);
        var twoDays = TimeSpan.FromDays(2);

        // Ada administers Alder, so she holds every key of the catalogue on Optics from above. She tutors it for two days.
        await campus.Services.ChangeAsync(data.Alder, async context =>
            (await context.Courses.SingleAsync(course => course.Id == data.Optics, Cancellation))
                .TakeOn(data.AdaSeat, data.AlderCourseRoles[CourseMembership.Tutor], MemberPeriod.Between(data.Now, data.Now + twoDays), data.Now, by: data.OwenSeat));

        // What she holds both ways she holds as a member, with no end: the course stays in sight when she is off it.
        var grade = await HoldOnAsync(campus, ada, data.Optics, CourseKeys.Grade);
        (grade!.Via, grade.Until).Should().Be((MemberVia.Members, (DateTimeOffset?)null));
        (await HoldOnAsync(campus, ada, data.Optics, CourseKeys.Outline))!.Via.Should().Be(MemberVia.Above, "a tutor does not write the outline, and an administrator does");

        clock.Advance(twoDays - Tick);
        (await HoldOnAsync(campus, ada, data.Optics, CourseKeys.Grade))!.Via.Should().Be(MemberVia.Members);

        // At the moment her place ends, the organization still gives her the key, and nothing says she tutors.
        clock.Advance(Tick);
        grade = await HoldOnAsync(campus, ada, data.Optics, CourseKeys.Grade);
        (grade!.Via, grade.Until).Should().Be((MemberVia.Above, (DateTimeOffset?)null));
        (await campus.Services.AsAsync(data.Ada, CampusScenario.InAlder, async provider =>
            {
                var reach = provider.GetRequiredService<IMemberQuestions<CourseId>>().Reach(CourseKeys.Grade);
                return await provider.Campus().Courses.Reached(reach).Where(found => found.Resource.Id == data.Optics).Select(found => new { found.AsMember, found.FromAbove }).SingleAsync(Cancellation);
            }))
            .Should().Be(new { AsMember = false, FromAbove = true });

        // Pia convenes Optics until next week, and holds nothing in the organization where it is given. One tick before
        // her place ends she writes its outline, until then; at that moment the course is not there for her.
        clock.Advance(data.NextWeek - clock.Now - Tick);
        var outline = await HoldOnAsync(campus, pia, data.Optics, CourseKeys.Outline);
        (outline!.Via, outline.Until).Should().Be((MemberVia.Members, (DateTimeOffset?)data.NextWeek));

        clock.Advance(Tick);
        (await HoldOnAsync(campus, pia, data.Optics, CourseKeys.Outline)).Should().BeNull();
        (await HoldOnAsync(campus, pia, data.Optics, CourseKeys.See)).Should().BeNull();
        (await HoldOnAsync(campus, pia, data.Poetry, CourseKeys.Dissolve))!.Via.Should().Be(MemberVia.Members, "she owns Poetry, with no end");
    }

    private static Task<MemberHold<CourseId>?> HoldOnAsync(CampusApp campus, Asking asking, CourseId course, string key)
        => campus.Services.CoursesAsync(asking.Person, asking.College, access => access.HoldAsync(course, key, Cancellation));

    private static Task<MemberHold<LabId>?> HoldInAsync(CampusApp campus, Asking asking, LabId lab, string key)
        => campus.Services.LabsAsync(asking.Person, asking.College, access => access.HoldAsync(lab, key, Cancellation));
}
