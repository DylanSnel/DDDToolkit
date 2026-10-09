using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Together.Tests;

/// <summary>
/// The two supporting domains in one application: a member of a course or of a lab is a seat of the college,
/// and a role held in the organization where the resource sits, or above it, reaches the resource. Neither
/// package knows the other: what joins them is the class that answers Membership's questions from Tenancy's,
/// and every claim here is asked through the packages' own questions, on SQLite and on Postgres.
/// </summary>
public abstract class SeatsAsMembersTests(CampusDatabases databases)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_seat_on_the_member_list_holds_the_keys_of_the_roles_it_holds_there()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;
        var tom = new Asking(data.Tom, CampusScenario.InAlder);
        var pia = new Asking(data.Pia, CampusScenario.InAlder);

        // A course's roles are kept for it: the tutor's gives seeing and grading, and nothing else.
        (await HoldOnAsync(campus, tom, data.Optics, CourseKeys.Grade))!.Via.Should().Be(MemberVia.Members);
        (await HoldOnAsync(campus, tom, data.Optics, CourseKeys.See))!.Via.Should().Be(MemberVia.Members);
        (await HoldOnAsync(campus, tom, data.Optics, CourseKeys.Outline))!.Via.Should().BeNull("a tutor sees the course and does not write its outline");
        (await HoldOnAsync(campus, tom, data.Poetry, CourseKeys.See)).Should().BeNull("Tom is on Optics alone, and holds nothing in the organization");

        // And for as long as the member holds the role: Pia convenes until next week.
        var outline = await HoldOnAsync(campus, pia, data.Optics, CourseKeys.Outline);
        outline!.Via.Should().Be(MemberVia.Members);
        outline.Until.Should().BeCloseTo(data.NextWeek, TimeSpan.FromMilliseconds(1));

        // A lab's roles are the college's own: the demonstrator's gives seeing and equipping in the lab it is held in,
        // and its key of Tenancy's own is no key a lab's member gets by it.
        (await HoldInAsync(campus, tom, data.Laser, LabKeys.Equip))!.Via.Should().Be(MemberVia.Members);
        (await HoldInAsync(campus, tom, data.Laser, LabKeys.Calibrate))!.Via.Should().BeNull();
        (await HoldInAsync(campus, tom, data.Laser, TenancyKeys.HistoryView))!.Via.Should().BeNull("a role given in a lab gives the keys of a lab, whatever else it holds in the organization");
        (await HoldInAsync(campus, tom, data.Darkroom, LabKeys.See)).Should().BeNull();
    }

    [Fact]
    public async Task A_role_held_at_a_unit_above_reaches_the_resource_and_via_says_so()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;
        var dee = new Asking(data.Dee, CampusScenario.InAlder);
        var ada = new Asking(data.Ada, CampusScenario.InAlder);

        // The dean of Science grades what is given at Physics, below it, without teaching on it: from above, with no end known here.
        var grade = await HoldOnAsync(campus, dee, data.Optics, CourseKeys.Grade);
        (grade!.Via, grade.Until).Should().Be((MemberVia.Above, (DateTimeOffset?)null));

        // She is on Optics as well, with no role: what she holds both ways she holds as a member.
        (await HoldOnAsync(campus, dee, data.Optics, CourseKeys.See))!.Via.Should().Be(MemberVia.Members);
        (await HoldOnAsync(campus, dee, data.Optics, CourseKeys.Outline))!.Via.Should().BeNull("the dean's role does not hold the key");

        // Poetry is given at Arts, beside Science: not below where she holds anything.
        (await HoldOnAsync(campus, dee, data.Poetry, CourseKeys.See)).Should().BeNull();

        // The administrator holds every key of the catalogue at the root, so on every course and lab of the college.
        (await HoldOnAsync(campus, ada, data.Optics, CourseKeys.Outline))!.Via.Should().Be(MemberVia.Above);
        (await HoldOnAsync(campus, ada, data.Poetry, CourseKeys.See))!.Via.Should().Be(MemberVia.Above);
        (await HoldInAsync(campus, ada, data.Darkroom, LabKeys.Calibrate))!.Via.Should().Be(MemberVia.Above);

        // A lab is reached the same way.
        (await HoldInAsync(campus, dee, data.Laser, LabKeys.Equip))!.Via.Should().Be(MemberVia.Above);
        (await HoldInAsync(campus, dee, data.Laser, LabKeys.Calibrate))!.Via.Should().BeNull();
        (await HoldInAsync(campus, dee, data.Darkroom, LabKeys.See)).Should().BeNull();

        // Moved to Arts, Optics is no longer below where the dean holds anything: she still sees it, as a member, and grades it no more.
        await campus.Services.ChangeAsync(data.Alder, async context => (await context.Courses.SingleAsync(course => course.Id == data.Optics, Cancellation)).GiveAt(data.Arts));
        (await HoldOnAsync(campus, dee, data.Optics, CourseKeys.Grade))!.Via.Should().BeNull();
    }

    [Fact]
    public async Task A_key_the_catalogue_does_not_know_is_held_by_nobody_above_and_asking_it_is_no_mistake()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;

        // Dissolving a course is its owner's alone: no role of courses may give it, and the catalogue does not know it, so
        // no role of the organization holds it. Tenancy itself would take a question about such a key for a typo.
        var asAdministrator = await HoldOnAsync(campus, new Asking(data.Ada, CampusScenario.InAlder), data.Optics, CourseKeys.Dissolve);
        asAdministrator.Should().NotBeNull("the administrator sees the course");
        asAdministrator!.Via.Should().BeNull();

        (await HoldOnAsync(campus, new Asking(data.Owen, CampusScenario.InAlder), data.Optics, CourseKeys.Dissolve))!.Via.Should().Be(MemberVia.Members);
        (await HoldInAsync(campus, new Asking(data.Owen, CampusScenario.InAlder), data.Laser, "labs.unheard-of"))!.Via.Should().BeNull();
    }

    [Fact]
    public async Task The_owner_holds_every_key_of_a_course_by_owning_it_and_in_a_lab_what_the_colleges_role_for_owners_gives()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;
        var owen = new Asking(data.Owen, CampusScenario.InAlder);

        // A course states its keys, so its owner holds each of them, with no end, whatever a role gives.
        foreach (var key in CourseMembership.Rules.Keys)
        {
            var held = await HoldOnAsync(campus, owen, data.Optics, key);
            (held!.Via, held.Until).Should().Be((MemberVia.Members, (DateTimeOffset?)null), "the owner holds {0}", key);
        }

        // A lab states none, so its owner holds what the college's role for a lab's chief gives: found by its pack, asked of Tenancy.
        (await HoldInAsync(campus, owen, data.Laser, LabKeys.Calibrate))!.Via.Should().Be(MemberVia.Members);
        (await HoldInAsync(campus, owen, data.Laser, CourseKeys.Grade))!.Via.Should().BeNull();

        // Which role that is, the admission of each resource answers: the package for a course, Tenancy for a lab.
        await campus.Services.AsAsync(data.Owen, CampusScenario.InAlder, async provider =>
        {
            (await provider.GetRequiredService<MemberAdmission<CourseId, SeatId, CourseRoleId>>().OwnerRoleAsync(Cancellation))
                .Should().Be(data.AlderCourseRoles[MembershipRules.DefaultOwnerRole]);
            (await provider.GetRequiredService<MemberAdmission<LabId, SeatId, RoleId>>().OwnerRoleAsync(Cancellation)).Should().Be(data.AlderLabChief);
        });
        await campus.Services.AsAsync(data.Bea, CampusScenario.InBirch, async provider =>
            (await provider.GetRequiredService<MemberAdmission<LabId, SeatId, RoleId>>().OwnerRoleAsync(Cancellation)).Should().Be(data.BirchLabChief, "each college has its own"));
    }

    [Fact]
    public async Task A_seat_of_another_tenant_reaches_nothing()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;
        var bea = new Asking(data.Bea, CampusScenario.InBirch);
        var tomInBirch = new Asking(data.Tom, CampusScenario.InBirch);

        // Birch's administrator holds every key in Birch, and Alder's courses and labs are not there for her.
        (await HoldOnAsync(campus, bea, data.Latin, CourseKeys.Grade))!.Via.Should().Be(MemberVia.Members);
        (await HoldOnAsync(campus, bea, data.Optics, CourseKeys.See)).Should().BeNull();
        (await HoldInAsync(campus, bea, data.Laser, LabKeys.See)).Should().BeNull();
        (await SeenCoursesAsync(campus, bea)).Should().Equal(data.Latin);

        // Tom tutors Optics through his seat in Alder. Asking in Birch he is another seat, which is on no list.
        (await HoldOnAsync(campus, tomInBirch, data.Optics, CourseKeys.See)).Should().BeNull();
        (await SeenCoursesAsync(campus, tomInBirch)).Should().BeEmpty();

        // The question that refuses says what it says of a course that does not exist.
        var refused = await FluentActions.Awaiting(() => campus.Services.CoursesAsync(data.Bea, CampusScenario.InBirch, access => access.RequireAsync(data.Optics, CourseKeys.See, Cancellation)))
            .Should().ThrowAsync<RefusalException>();
        refused.Which.Code.Should().Be(CourseMembership.Codes[MembershipRefusals.NotFound]);
    }

    [Fact]
    public async Task A_suspended_seat_reaches_nothing_and_is_refused_as_tenancy_refuses_it()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;
        var sue = new Asking(data.Sue, CampusScenario.InAlder);

        // Sue tutors Optics and is a dean of Science, and her seat is suspended: every row still applies, and she is nobody.
        foreach (var key in CampusScenario.CourseKeysAsked)
        {
            (await HoldOnAsync(campus, sue, data.Optics, key)).Should().BeNull("a suspended seat holds {0} nowhere", key);
        }

        (await SeenCoursesAsync(campus, sue)).Should().BeEmpty();
        (await HoldInAsync(campus, sue, data.Laser, LabKeys.See)).Should().BeNull();

        // Where a question refuses, she is refused before anything is read, with Tenancy's own word for it.
        campus.Commands.Reset();
        await RefusedAsync(campus, sue, TenancyRefusals.SeatSuspended);
        await RefusedAsync(campus, new Asking(data.Ivo, CampusScenario.InAlder), TenancyRefusals.NotSeated);
        await RefusedAsync(campus, new Asking(data.Bea, CampusScenario.InAlder), TenancyRefusals.NotSeated);
        await RefusedAsync(campus, new Asking(data.Ada, null), TenancyRefusals.TenantRequired);

        // With her seat active again she counts again: nothing about her was kept.
        await campus.Services.BySystemInAsync(data.Alder, provider => provider.Seats().ReactivateAsync(data.SueSeat, Cancellation));
        (await HoldOnAsync(campus, sue, data.Optics, CourseKeys.Grade))!.Via.Should().Be(MemberVia.Members);
        (await HoldInAsync(campus, sue, data.Laser, LabKeys.Equip))!.Via.Should().Be(MemberVia.Above);
    }

    [Fact]
    public async Task Everybody_holds_on_every_course_and_lab_what_the_two_domains_say_together()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;

        foreach (var asking in data.Everybody)
        {
            (await SeenCoursesAsync(campus, asking)).Should().BeEquivalentTo(data.CoursesSeenBy(asking), "of the courses {0} sees", asking);
            (await SeenLabsAsync(campus, asking)).Should().BeEquivalentTo(data.LabsSeenBy(asking), "of the labs {0} sees", asking);

            foreach (var key in CampusScenario.CourseKeysAsked)
            {
                var held = await campus.Services.AsAsync(asking.Person, asking.College, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<CourseId>>().Reach(key);
                    return await provider.Campus().Courses.Within(reach).Select(course => course.Id).ToListAsync(Cancellation);
                });
                held.Should().BeEquivalentTo(data.CoursesHeldBy(asking, key), "of the courses {0} holds {1} on", asking, key);
            }

            foreach (var key in CampusScenario.LabKeysAsked)
            {
                var held = await campus.Services.AsAsync(asking.Person, asking.College, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<LabId>>().Reach(key);
                    return await provider.Campus().Labs.Within(reach).Select(lab => lab.Id).ToListAsync(Cancellation);
                });
                held.Should().BeEquivalentTo(data.LabsHeldBy(asking, key), "of the labs {0} holds {1} on", asking, key);
            }
        }

        // And how each key is held on the one course everybody is asked about.
        foreach (var asking in data.Everybody.Where(asking => data.CoursesSeenBy(asking).Contains(data.Optics)))
        {
            foreach (var key in CampusScenario.CourseKeysAsked)
            {
                (await HoldOnAsync(campus, asking, data.Optics, key))!.Via.Should().Be(data.ViaOn(asking, data.Optics, key), "of how {0} holds {1} on Optics", asking, key);
            }
        }
    }

    [Fact]
    public async Task Each_question_is_one_statement_with_tenancys_answer_inside_it()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;
        var commands = campus.Commands;

        // What a caller holds on one course: the members, the roles of courses and where the key is held above, read at once.
        MemberHold<CourseId>? grade = null;
        await campus.Services.AsAsync(data.Dee, CampusScenario.InAlder, async provider =>
        {
            commands.Reset();
            grade = await provider.GetRequiredService<IMemberQuestions<CourseId>>().HoldAsync(data.Optics, CourseKeys.Grade, Cancellation);
            commands.Count.Should().Be(1, "how the key is held, until when, and the course's version are one statement");
            commands.Commands[0].Should().Contain("CourseTutors").And.Contain("CourseRoles").And.Contain(campus.ReadModel, "where the dean holds the key is a subquery of the statement");
        });
        grade!.Via.Should().Be(MemberVia.Above);

        // What a lab's member holds: which of the college's roles give the key is Tenancy's answer, in the same statement.
        await campus.Services.AsAsync(data.Tom, CampusScenario.InAlder, async provider =>
        {
            commands.Reset();
            var equip = await provider.GetRequiredService<IMemberQuestions<LabId>>().HoldAsync(data.Laser, LabKeys.Equip, Cancellation);
            commands.Count.Should().Be(1);
            commands.Commands[0].Should().Contain("LabTechnicianRoles");
            equip!.Via.Should().Be(MemberVia.Members);
        });

        // The keys held on many, and a page of the application's own with a reach in it: one statement each.
        await campus.Services.AsAsync(data.Ada, CampusScenario.InAlder, async provider =>
        {
            commands.Reset();
            var keys = await provider.GetRequiredService<IMemberQuestions<CourseId>>().KeysOnAsync([data.Optics, data.Poetry, data.Latin], CampusScenario.CourseKeysAsked, Cancellation);
            commands.Count.Should().Be(1);
            keys.Keys.Should().BeEquivalentTo([data.Optics, data.Poetry], "Latin is another college's");

            // What is held above is whatever the organization gives there: every key of the catalogue for the administrator,
            // another module's among them, and never the one the catalogue does not know.
            keys.Values.Should().OnlyContain(held => held.SetEquals(new[] { CourseKeys.See, CourseKeys.Grade, CourseKeys.Outline, LabKeys.Equip, TenancyKeys.HistoryView }));

            commands.Reset();
            var seen = provider.GetRequiredService<IMemberQuestions<CourseId>>().Reach(CourseKeys.See);
            var titles = await provider.Campus().Courses.Within(seen).OrderBy(course => course.Title).Select(course => course.Title).ToListAsync(Cancellation);
            titles.Should().Equal("Optics", "Poetry");
            commands.Count.Should().Be(1);
        });

    }

    [Fact]
    public async Task The_questions_are_answered_on_a_context_of_their_own_where_the_host_has_a_factory()
    {
        using var campus = await databases.CreateAsync(CampusContexts.FromAPool);
        var data = campus.Scenario;

        // Where the dean holds the key is asked on the context the statement runs on, which is not the request's own here.
        await campus.Services.AsAsync(data.Dee, CampusScenario.InAlder, async provider =>
        {
            var requests = provider.Campus();
            campus.Commands.Reset();
            var grade = await provider.GetRequiredService<IMemberQuestions<CourseId>>().HoldAsync(data.Optics, CourseKeys.Grade, Cancellation);

            grade!.Via.Should().Be(MemberVia.Above);
            campus.Commands.Sent.Should().ContainSingle().Which.Context.Should().NotBeSameAs(requests);
        });

        foreach (var asking in data.Everybody)
        {
            (await SeenCoursesAsync(campus, asking)).Should().BeEquivalentTo(data.CoursesSeenBy(asking), "of the courses {0} sees", asking);
        }
    }

    [Fact]
    public async Task A_host_with_a_factory_of_its_own_and_no_context_of_a_request_is_answered_on_contexts_of_the_factorys()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;
        var host = campus.AnotherHost(CampusContexts.FromAFactoryAlone);

        await host.AsAsync(data.Owen, CampusScenario.InAlder, async provider =>
        {
            provider.GetService<CampusContext>().Should().BeNull("this host registers a factory for the context, and none for a request");

            // What the admission asks of the organization is asked outside a statement of the resource's: whether a seat
            // is active, which of the college's roles there are, and which is the owner's.
            var courses = provider.GetRequiredService<MemberAdmission<CourseId, SeatId, CourseRoleId>>();
            var labs = provider.GetRequiredService<MemberAdmission<LabId, SeatId, RoleId>>();

            await courses.RequireMemberAsync(data.NedSeat, Cancellation);
            (await FluentActions.Awaiting(() => labs.RequireMemberAsync(data.SueSeat, Cancellation).AsTask()).Should().ThrowAsync<RefusalException>())
                .Which.Code.Should().Be(LabMembership.Codes[MembershipRefusals.MemberNotActive]);

            await labs.RequireRoleAsync(data.AlderDemonstrator, Cancellation);
            (await FluentActions.Awaiting(() => labs.RequireRoleAsync(data.BirchLabChief, Cancellation).AsTask()).Should().ThrowAsync<RefusalException>())
                .Which.Code.Should().Be(LabMembership.Codes[MembershipRefusals.RoleNotForMembers]);

            (await labs.OwnerRoleAsync(Cancellation)).Should().Be(data.AlderLabChief);
            (await courses.OwnerRoleAsync(Cancellation)).Should().Be(data.AlderCourseRoles[MembershipRules.DefaultOwnerRole]);
        });

        // And what a caller holds, where the organization's answer is inside the statement.
        (await host.CoursesAsync(data.Dee, CampusScenario.InAlder, access => access.HoldAsync(data.Optics, CourseKeys.Grade, Cancellation)))!.Via.Should().Be(MemberVia.Above);
        (await host.LabsAsync(data.Tom, CampusScenario.InAlder, access => access.HoldAsync(data.Laser, LabKeys.Equip, Cancellation)))!.Via.Should().Be(MemberVia.Members);
    }

    [Fact]
    public async Task Only_an_active_seat_of_the_college_is_made_a_member_and_only_a_role_of_the_college_is_given_in_a_lab()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;

        await campus.Services.AsAsync(data.Owen, CampusScenario.InAlder, async provider =>
        {
            var courses = provider.GetRequiredService<MemberAdmission<CourseId, SeatId, CourseRoleId>>();
            var labs = provider.GetRequiredService<MemberAdmission<LabId, SeatId, RoleId>>();

            // A member is a seat, so whether somebody can be made one is whether that seat is active in this college.
            await courses.RequireMemberAsync(data.NedSeat, Cancellation);
            await labs.RequireMemberAsync(data.NedSeat, Cancellation);
            foreach (var seat in new[] { data.SueSeat, data.BeaSeat, data.TomInBirch, SeatId.CreateSequential() })
            {
                (await FluentActions.Awaiting(() => courses.RequireMemberAsync(seat, Cancellation).AsTask()).Should().ThrowAsync<RefusalException>())
                    .Which.Code.Should().Be(CourseMembership.Codes[MembershipRefusals.MemberNotActive], "a suspended seat, a seat of another college and no seat at all are not made members");
                (await FluentActions.Awaiting(() => labs.RequireMemberAsync(seat, Cancellation).AsTask()).Should().ThrowAsync<RefusalException>())
                    .Which.Code.Should().Be(LabMembership.Codes[MembershipRefusals.MemberNotActive]);
            }

            // A lab's roles are the college's: one of its roles in use is given, and another college's is not.
            await labs.RequireRoleAsync(data.AlderDemonstrator, Cancellation);
            await labs.RequireRoleAsync(data.AlderDean, Cancellation);
            (await FluentActions.Awaiting(() => labs.RequireRoleAsync(data.BirchLabChief, Cancellation).AsTask()).Should().ThrowAsync<RefusalException>())
                .Which.Code.Should().Be(LabMembership.Codes[MembershipRefusals.RoleNotForMembers]);

            // A course's roles are its own, kept for the college: Tenancy's roles are none of them, and neither are another college's.
            await courses.RequireRoleAsync(data.AlderCourseRoles[CourseMembership.Tutor], Cancellation);
            (await FluentActions.Awaiting(() => courses.RequireRoleAsync(data.BirchCourseRoles[CourseMembership.Tutor], Cancellation).AsTask()).Should().ThrowAsync<RefusalException>())
                .Which.Code.Should().Be(CourseMembership.Codes[MembershipRefusals.RoleNotForMembers]);
        });

        // A role of the college's that it archived is given no more, and gives nothing where it is still held.
        await campus.Services.BySystemInAsync(data.Alder, provider => provider.Roles().ArchiveAsync(data.AlderDemonstrator, Cancellation));
        await campus.Services.AsAsync(data.Owen, CampusScenario.InAlder, async provider =>
            (await FluentActions.Awaiting(() => provider.GetRequiredService<MemberAdmission<LabId, SeatId, RoleId>>().RequireRoleAsync(data.AlderDemonstrator, Cancellation).AsTask())
                .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(LabMembership.Codes[MembershipRefusals.RoleNotForMembers]));
        var tom = new Asking(data.Tom, CampusScenario.InAlder);
        (await HoldInAsync(campus, tom, data.Laser, LabKeys.Equip))!.Via.Should().BeNull("the demonstrator's role is archived");
        (await HoldInAsync(campus, tom, data.Laser, LabKeys.See))!.Via.Should().Be(MemberVia.Members, "he still works in the lab");
    }

    [Fact]
    public async Task A_college_that_archived_its_role_for_a_labs_owner_has_none_and_the_owner_holds_what_working_there_gives()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;
        var owen = new Asking(data.Owen, CampusScenario.InAlder);

        // A lab states no key of its own, so its owner holds what the college's role for owners gives, and nothing by owning.
        (await HoldInAsync(campus, owen, data.Laser, LabKeys.Calibrate))!.Via.Should().Be(MemberVia.Members);

        // That role is the college's to archive, like any of its roles. It gives nothing from then on, where it is still held too.
        await campus.Services.BySystemInAsync(data.Alder, provider => provider.Roles().ArchiveAsync(data.AlderLabChief, Cancellation));

        await campus.Services.AsAsync(data.Owen, CampusScenario.InAlder, async provider =>
        {
            var labs = provider.GetRequiredService<MemberAdmission<LabId, SeatId, RoleId>>();

            // Which role an owner holds is asked for a role in use: there is none now, so no lab is opened and no owner named.
            (await labs.FindOwnerRoleAsync(Cancellation)).Should().BeNull();
            (await FluentActions.Awaiting(() => labs.OwnerRoleAsync(Cancellation).AsTask()).Should().ThrowAsync<RefusalException>())
                .Which.Code.Should().Be(LabMembership.Codes[MembershipRefusals.NoOwnerRole]);
            (await FluentActions.Awaiting(() => labs.RequireRoleAsync(data.AlderLabChief, Cancellation).AsTask()).Should().ThrowAsync<RefusalException>())
                .Which.Code.Should().Be(LabMembership.Codes[MembershipRefusals.RoleNotForMembers]);
        });

        // The owner is still in the lab, and sees it. What only the role gave, he holds no more.
        (await HoldInAsync(campus, owen, data.Laser, LabKeys.See))!.Via.Should().Be(MemberVia.Members);
        (await HoldInAsync(campus, owen, data.Laser, LabKeys.Calibrate))!.Via.Should().BeNull("the role an owner holds is archived, and a lab's owner holds nothing by owning");

        // A course states its keys, so its owner holds them by owning, whatever becomes of a role.
        (await HoldOnAsync(campus, owen, data.Optics, CourseKeys.Dissolve))!.Via.Should().Be(MemberVia.Members);

        // And each college has its own role: Birch's is in use as it was.
        await campus.Services.AsAsync(data.Bea, CampusScenario.InBirch, async provider =>
            (await provider.GetRequiredService<MemberAdmission<LabId, SeatId, RoleId>>().OwnerRoleAsync(Cancellation)).Should().Be(data.BirchLabChief));
    }

    [Fact]
    public async Task System_work_in_a_college_holds_every_key_on_what_names_its_scope_and_reaches_nothing_of_another_college()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;

        // The courses' rules name the scope the application's own work on them runs in: in a college, that
        // work holds every key on every course there.
        await campus.Services.BySystemInAsync(
            data.Alder,
            async provider =>
            {
                var courses = provider.GetRequiredService<IMemberQuestions<CourseId>>();
                (await courses.HoldAsync(data.Optics, CourseKeys.Dissolve, Cancellation))!.Via.Should().Be(MemberVia.System);
                (await courses.HoldAsync(data.Latin, CourseKeys.See, Cancellation)).Should().BeNull("Tenancy keeps the application's own work in a college to that college");
                (await provider.Campus().Courses.Within(courses.Reach(CourseKeys.Grade)).Select(course => course.Id).ToListAsync(Cancellation))
                    .Should().BeEquivalentTo([data.Optics, data.Poetry]);

                // A lab's rules name no scope: the same work is nobody's member there and nothing above the
                // rules. It holds nothing on a lab, of its own college or another.
                var labs = provider.GetRequiredService<IMemberQuestions<LabId>>();
                (await labs.HoldAsync(data.Laser, LabKeys.See, Cancellation)).Should().BeNull();
                labs.Reach(LabKeys.Equip).Everything.Should().BeFalse();
                (await provider.Campus().Labs.Within(labs.Reach(LabKeys.See)).CountAsync(Cancellation)).Should().Be(0);
            },
            CampusServices.Scope);

        // Work in another scope, Tenancy's own, holds nothing on a course either: a scope is a module's own.
        await campus.Services.BySystemInAsync(
            data.Alder,
            async provider =>
            {
                var courses = provider.GetRequiredService<IMemberQuestions<CourseId>>();
                (await courses.HoldAsync(data.Optics, CourseKeys.See, Cancellation)).Should().BeNull();
                courses.Reach(CourseKeys.Grade).Everything.Should().BeFalse();
            });
    }

    private static Task<MemberHold<CourseId>?> HoldOnAsync(CampusApp campus, Asking asking, CourseId course, string key)
        => campus.Services.CoursesAsync(asking.Person, asking.College, access => access.HoldAsync(course, key, Cancellation));

    private static Task<MemberHold<LabId>?> HoldInAsync(CampusApp campus, Asking asking, LabId lab, string key)
        => campus.Services.LabsAsync(asking.Person, asking.College, access => access.HoldAsync(lab, key, Cancellation));

    private static Task<List<CourseId>> SeenCoursesAsync(CampusApp campus, Asking asking)
        => campus.Services.AsAsync(asking.Person, asking.College, async provider =>
        {
            var see = provider.GetRequiredService<IMemberQuestions<CourseId>>().KeyReach([]).See;
            return await provider.Campus().Courses.Within(see).Select(course => course.Id).ToListAsync(Cancellation);
        });

    private static Task<List<LabId>> SeenLabsAsync(CampusApp campus, Asking asking)
        => campus.Services.AsAsync(asking.Person, asking.College, async provider =>
        {
            var see = provider.GetRequiredService<IMemberQuestions<LabId>>().KeyReach([]).See;
            return await provider.Campus().Labs.Within(see).Select(lab => lab.Id).ToListAsync(Cancellation);
        });

    /// <summary>Asserts that the questions that refuse answer <paramref name="asking"/> with Tenancy's own refusal, for a course and for a lab, and read nothing for it.</summary>
    private static async Task RefusedAsync(CampusApp campus, Asking asking, string code)
    {
        var data = campus.Scenario;
        campus.Commands.Reset();

        (await FluentActions.Awaiting(() => campus.Services.AsAsync(asking.Person, asking.College, async provider =>
            {
                campus.Commands.Reset();
                return await provider.GetRequiredService<IMemberQuestions<CourseId>>().RequireAsync(data.Optics, CourseKeys.See, Cancellation);
            }))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(code, "of {0}", asking);
        campus.Commands.Count.Should().Be(0, "somebody who is nobody is refused before anything is read");

        (await FluentActions.Awaiting(() => campus.Services.LabsAsync(asking.Person, asking.College, access => access.ViaAsync(data.Laser, LabKeys.See, Cancellation)))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(code);
    }
}

/// <summary>The claims on SQLite, in memory: Tenancy's read model is views over its tables.</summary>
public sealed class SeatsAsMembersTestsOnSqlite() : SeatsAsMembersTests(CampusDatabases.Sqlite);

/// <summary>The claims on Postgres, under row level security: Tenancy's read model is its read functions, and every statement runs as the caller.</summary>
public sealed class SeatsAsMembersTestsOnPostgres(CampusPostgres postgres) : SeatsAsMembersTests(postgres);
