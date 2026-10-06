using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Who reads the seats, and who besides the seats managers reads an invitation, are the application's to say: Tenancy
/// writes a default, and a read rule of the application's on its own seat or invitation class takes its place, held to
/// the calling seat's tenant. The rules here are written as an application writes them, without <c>To</c>, and are
/// for signed-in users, as the defaults are. What Tenancy's own work reads stays whatever the rule says, and Tenancy's
/// questions about the caller and about rights answer as they did, so a rule stricter than the default breaks none of
/// its use cases. Every other read, and every write, stays Tenancy's: a rule that would change one is refused when
/// the policies are written.
/// </summary>
public sealed class ReadRulesTests(TenancyPostgres postgres)
{
    private static readonly RowAccessRule OnlyThemselves =
        RowAccessRule.For<HostSeat>("Members read only themselves", RowOperations.Read, MembersReadOnlyThemselves.RowAccessSql);

    private static readonly RowAccessRule PeopleOfTheirUnits =
        RowAccessRule.For<HostSeat>("Members read the people of their units", RowOperations.Read, MembersReadThePeopleOfTheirUnits.RowAccessSql);

    private static readonly RowAccessRule EverySeat =
        RowAccessRule.For<HostSeat>("Members read every seat", RowOperations.Read, MembersReadEverySeat.RowAccessSql);

    private static readonly RowAccessRule InvitationsWhereTheyGiveRoles =
        RowAccessRule.For<HostInvitation>("Grants managers read the invitations at their units", RowOperations.Read, GrantsManagersReadTheInvitationsAtTheirUnits.RowAccessSql);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------------------------------------ the export

    [Fact]
    public void Without_a_read_rule_of_the_applications_the_seats_and_the_invitations_get_tenancys_default_as_it_was()
    {
        var tenancy = TenancyPostgres.AccessScripts()[0];

        tenancy.Should().Contain(
            "CREATE POLICY \"Members and the person read seats (select) for authenticated\" ON tenancy.\"Seats\" FOR SELECT TO authenticated\n" +
            "    USING (\"TenantId\" = (SELECT tenancy.caller_tenant()) OR \"Identity\" = (SELECT ddd.caller_id()));");
        tenancy.Should().Contain("CREATE POLICY \"Seat managers read invitations (select) for authenticated\" ON tenancy.\"Invitations\" FOR SELECT TO authenticated\n");
        tenancy.Should().NotContain("in place of", "nothing changes for an application that writes no read rule");
    }

    [Fact]
    public void A_read_rule_on_the_seat_class_takes_the_place_of_the_default_within_the_tenant_beside_what_tenancys_own_work_reads()
    {
        var tenancy = TenancyPostgres.AccessScripts(rules: [.. WidgetRules.All, OnlyThemselves])[0];

        tenancy.Should().Contain(
            "-- Seats (select) for authenticated asks the rule 'Members read only themselves' (which names no role, so it is for the roles of the default) " +
            "in place of the default 'Members and the person read seats' of the row access contribution " +
            "DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres ");
        tenancy.Should().Contain(
            ", held to what that default holds a rule to, beside what it keeps whatever a rule says: a row one of them allows is allowed.\n" +
            "CREATE POLICY \"Seats (select) for authenticated\" ON tenancy.\"Seats\" FOR SELECT TO authenticated\n" +
            "    USING ((\"Identity\" = (SELECT ddd.caller_id()) OR ((\"TenantId\" = (SELECT tenancy.caller_tenant())) AND ((SELECT tenancy.holds_key('tenancy.seats.manage')) " +
            "OR (SELECT tenancy.holds_key('tenancy.grants.manage')) OR (SELECT tenancy.holds_key('tenancy.units.manage')) OR (SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')))))" +
            " OR ((\"TenantId\" = (SELECT tenancy.caller_tenant())) AND (\"Id\" = (SELECT tenancy.caller_seat()))));",
            "a person's own seats and the managers' read stay, and the rule decides the rest, in the calling seat's tenant");
        tenancy.Should().NotContain("\"Members and the person read seats (select) for authenticated\"", "the default itself is gone");
        tenancy.Should().NotContain("ON tenancy.\"Seats\" FOR SELECT TO anon", "a rule without To is for the signed-in users the default is for, and anonymous callers still read no seat");
        tenancy.Should().Contain("CREATE POLICY \"Managers and the seat change it (update) for authenticated\" ON tenancy.\"Seats\"", "who changes a seat stays Tenancy's");
        tenancy.Should().NotContain("SeatPlacements belongs to the aggregate", "a seat's placements and grants keep Tenancy's own policies");
    }

    [Fact]
    public void A_rule_on_a_class_of_tenancys_is_refused_unless_it_reads_alone_for_signed_in_users_and_the_read_is_a_default()
    {
        var onTheRoles = () => TenancyPostgres.AccessScripts(rules: [RowAccessRule.For<HostRole>("Members read the roles they hold", RowOperations.Read, "TRUE", RowAccessRoles.User)]);
        var changing = () => TenancyPostgres.AccessScripts(rules: [RowAccessRule.For<HostSeat>("Members change their own seat", RowOperations.Read | RowOperations.Change, MembersReadOnlyThemselves.RowAccessSql, RowAccessRoles.User)]);
        var forAnon = () => TenancyPostgres.AccessScripts(rules: [RowAccessRule.For<HostSeat>("Everyone reads every seat", RowOperations.Read, MembersReadEverySeat.RowAccessSql, RowAccessRoles.User, RowAccessRoles.Anonymous)]);

        onTheRoles.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Members read the roles they hold' would add a policy to tenancy.Roles, which the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution keeps to itself: only it writes that table's policies. Leave the rule out*",
            "Tenancy's questions read the roles, so their read is no default");
        changing.Should().Throw<InvalidOperationException>().WithMessage(
            "*It lets a rule of the application take the place of what it lets RowAccessRoles.User read there, and of nothing else: a rule that allows Read alone.*");
        forAnon.Should().Throw<InvalidOperationException>().WithMessage(
            "*and the rule is for RowAccessRoles.Anonymous as well: leave To out, and the rule is for the roles of the default, or set To = [RowAccessRoles.User].",
            "a rule that names the anonymous caller itself asks for a read Tenancy keeps closed");
    }

    // ------------------------------------------------------------------------------------------------ the database

    [Fact]
    public async Task A_rule_that_narrows_who_reads_the_seats_keeps_what_tenancys_own_work_reads_and_every_use_case_still_runs()
    {
        var database = await ForcedAsync(OnlyThemselves);

        // A member reads itself, and the person's own seats in every tenant, which the tenant picker lists.
        (await SeatsReadByAsync(database, Oli, Harbor)).Should().BeEquivalentTo([Oli.Seat.Value, OliInOrchard.Value]);
        (await SeatsReadByAsync(database, Eve, Harbor)).Should().BeEquivalentTo([Eve.Seat.Value]);

        // A seat that manages seats, grants or units anywhere, or roles for the whole tenant, reads every seat of it.
        Guid[] harbor = [Ada.Seat.Value, Hiro.Seat.Value, Seth.Seat.Value, Oli.Seat.Value, Sue.Seat.Value, Eve.Seat.Value];
        foreach (var manager in new[] { Ada, Hiro, Seth })
        {
            (await SeatsReadByAsync(database, manager, Harbor)).Should().BeEquivalentTo(harbor, "{0} manages access, and Tenancy's use cases load the seats {0} acts on", manager.Name);
        }

        // A module reads the seats as the caller: the rule decides what it reads too.
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ListAsync<Guid>("SELECT \"Id\" FROM tenancy.tenant_seats() WHERE \"TenantId\" = 1", Cancellation)).Should().Equal(Oli.Seat.Value);
        }

        // The seat a request acts as is found from the person's own seats, which no rule takes away.
        await using var services = new TenancyServices(database);
        foreach (var (slug, seat) in new[] { ("harbor", Oli.Seat), ("orchard", OliInOrchard) })
        {
            using (Callers.Begin(Caller.User(Oli.Identity)))
            {
                (await services.InScopeAsync(scoped => scoped.GetRequiredService<TenantSelection<TenantId, SeatId>>().ResolveAsync(Caller.User(Oli.Identity), slug, Cancellation)))
                    .Seat.Should().Be(seat);
            }
        }

        // And every command of the use cases runs as it did: none of them loads a seat its caller no longer reads.
        await EveryUseCase.RunAsync(services, Cancellation);
    }

    [Fact]
    public async Task Tenancys_functions_about_the_caller_and_about_rights_answer_what_they_answered_whatever_the_rule_says()
    {
        var byDefault = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await TenancyPostgres.ForceAsync(byDefault, Cancellation);
        var narrowed = await ForcedAsync(OnlyThemselves);

        // They run as their owner: a rule about who reads the seats is no rule about what a seat holds. tenant_seats()
        // is no such question: it reads the seats as its caller, and the test above shows the rule deciding it.
        string[] questions =
        [
            "SELECT coalesce(tenancy.caller_seat()::text, '') || ' ' || coalesce(tenancy.caller_tenant()::text, '')",
            "SELECT coalesce(string_agg(u::text, ',' ORDER BY u::text), '') FROM tenancy.units_where_i_hold('widget.read') u",
            "SELECT coalesce(string_agg(u::text, ',' ORDER BY u::text), '') FROM tenancy.readable_units() u",
            "SELECT coalesce(string_agg(r::text, ',' ORDER BY r::text), '') FROM tenancy.roles_with_key('widget.read') r",
            "SELECT tenancy.holds_key('tenancy.seats.manage')::text || ' ' || tenancy.holds_tenant_wide('tenancy.roles.manage')::text",
            $"SELECT coalesce(string_agg(s.\"SeatId\"::text, ',' ORDER BY s.\"SeatId\"::text), '') FROM tenancy.seats_holding_at('widget.read', '{NorthPier.Value}') s",
            "SELECT coalesce(string_agg(a.\"SeatId\"::text, ',' ORDER BY a.\"SeatId\"::text), '') FROM tenancy.tenant_administrators() a",
            "SELECT coalesce(string_agg(t::text, ',' ORDER BY t::text), '') FROM tenancy.identity_tenants() t",
            "SELECT coalesce(string_agg(s::text, ',' ORDER BY s::text), '') FROM tenancy.seats_in_my_units() s",
        ];

        foreach (var (person, tenant) in new[] { (Ada, Harbor), (Hiro, Harbor), (Seth, Harbor), (Oli, Harbor), (Eve, Harbor), (Oli, Orchard) })
        {
            (await AnswersAsync(narrowed, person, tenant, questions)).Should().Equal(await AnswersAsync(byDefault, person, tenant, questions), "{0} in tenant {1}", person.Name, tenant.Value);
        }
    }

    [Fact]
    public async Task A_rule_by_where_people_are_placed_lets_a_member_read_the_people_of_their_units()
    {
        var database = await ForcedAsync(PeopleOfTheirUnits);

        // Oli is placed at North Pier, where Seth is placed too; Eve at South, with Sue. Neither reads the others, nor
        // Ada at the root above them, whom a rule of the application's own could add.
        (await SeatsReadByAsync(database, Oli, Harbor)).Should().BeEquivalentTo([Oli.Seat.Value, Seth.Seat.Value, OliInOrchard.Value]);
        (await SeatsReadByAsync(database, Eve, Harbor)).Should().BeEquivalentTo([Eve.Seat.Value, Sue.Seat.Value]);

        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ListAsync<Guid>("SELECT s FROM tenancy.seats_in_my_units() s ORDER BY 1", Cancellation)).Should().BeEquivalentTo([Oli.Seat.Value, Seth.Seat.Value]);
        }

        // A unit a seat belongs to is one it is placed at, or one below it: Hiro, placed at North, finds the people of
        // North Pier below it too, and Ada, placed at the root, every placed seat of the tenant, as every seat of a flat
        // tenant would.
        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            (await hiro.ListAsync<Guid>("SELECT s FROM tenancy.seats_in_my_units() s", Cancellation)).Should().BeEquivalentTo([Hiro.Seat.Value, Seth.Seat.Value, Oli.Seat.Value]);
        }

        await using var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation);
        (await ada.ListAsync<Guid>("SELECT s FROM tenancy.seats_in_my_units() s", Cancellation))
            .Should().BeEquivalentTo([Ada.Seat.Value, Hiro.Seat.Value, Seth.Seat.Value, Oli.Seat.Value, Sue.Seat.Value, Eve.Seat.Value]);
    }

    [Fact]
    public async Task A_rule_that_lets_every_row_through_is_held_to_the_calling_seats_tenant()
    {
        var database = await ForcedAsync(EverySeat);

        // Every seat of Harbor, and Oli's own in Orchard as before, and not Odette's there.
        (await SeatsReadByAsync(database, Oli, Harbor)).Should().BeEquivalentTo(
            [Ada.Seat.Value, Hiro.Seat.Value, Seth.Seat.Value, Oli.Seat.Value, Sue.Seat.Value, Eve.Seat.Value, OliInOrchard.Value]);
        (await SeatsReadByAsync(database, Odette, Orchard)).Should().BeEquivalentTo([Odette.Seat.Value, OliInOrchard.Value]);
    }

    [Fact]
    public async Task A_rule_that_widens_who_reads_the_invitations_adds_readers_and_the_seats_managers_keep_theirs()
    {
        // By default the seats managers at its unit read it: Seth, and not Hiro, who gives roles there.
        var byDefault = await WithAnInvitationAsync(null);
        (await InvitationsReadByAsync(byDefault, Seth)).Should().Be(1);
        (await InvitationsReadByAsync(byDefault, Hiro)).Should().Be(0);

        // The application lets whoever gives roles at its unit read it too: Hiro now, Seth as before, Oli still not.
        var widened = await WithAnInvitationAsync(InvitationsWhereTheyGiveRoles);
        (await InvitationsReadByAsync(widened, Hiro)).Should().Be(1);
        (await InvitationsReadByAsync(widened, Seth)).Should().Be(1, "listing and cancelling an invitation load it, and no rule takes that away");
        (await InvitationsReadByAsync(widened, Oli)).Should().Be(0);

        // What a seat writes stays Tenancy's: Hiro reads it, and cancels nothing.
        await using var hiro = await AsCaller.PersonAsync(widened, Hiro.Identity, Harbor, Cancellation);
        (await hiro.ExecuteAsync("UPDATE tenancy.\"Invitations\" SET \"State\" = 'Cancelled'", Cancellation)).Should().Be(0);
    }

    /// <summary>
    /// A copy of the secured template with an invitation to North Pier, which Ada issues through the use case, then
    /// forced, with the widgets' rules and <paramref name="rule"/> of the application's where one is given.
    /// </summary>
    private async Task<TestDatabase> WithAnInvitationAsync(RowAccessRule? rule)
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using (var services = new TenancyServices(database))
        {
            await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped =>
                scoped.Invitations().IssueAsync("lark@example.test", NorthPier, HarborRoles.Watcher, grantUntil: null, lifetime: null, Cancellation));
        }

        await TenancyPostgres.ForceAsync(database, Cancellation, rule is null ? null : [.. WidgetRules.All, rule]);
        return database;
    }

    /// <summary>A copy of the secured template, forced, with the widgets' rules and <paramref name="rule"/> of the application's.</summary>
    private async Task<TestDatabase> ForcedAsync(RowAccessRule rule)
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await TenancyPostgres.ForceAsync(database, Cancellation, [.. WidgetRules.All, rule]);
        return database;
    }

    /// <summary>The ids of the seats <paramref name="person"/> reads, with the connection naming <paramref name="tenant"/>.</summary>
    private static async Task<List<Guid>> SeatsReadByAsync(TestDatabase database, Person person, TenantId tenant)
    {
        await using var caller = await AsCaller.PersonAsync(database, person.Identity, tenant, Cancellation);
        return await caller.ListAsync<Guid>("SELECT \"Id\" FROM tenancy.\"Seats\"", Cancellation);
    }

    /// <summary>How many invitations <paramref name="person"/> reads in Harbor.</summary>
    private static async Task<long> InvitationsReadByAsync(TestDatabase database, Person person)
    {
        await using var caller = await AsCaller.PersonAsync(database, person.Identity, Harbor, Cancellation);
        return await caller.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Invitations\"", Cancellation);
    }

    /// <summary>What each of <paramref name="questions"/> answers <paramref name="person"/> in <paramref name="tenant"/>.</summary>
    private static async Task<List<string>> AnswersAsync(TestDatabase database, Person person, TenantId tenant, IEnumerable<string> questions)
    {
        await using var caller = await AsCaller.PersonAsync(database, person.Identity, tenant, Cancellation);
        var answers = new List<string>();
        foreach (var question in questions)
        {
            answers.Add(await caller.ScalarAsync<string>(question, Cancellation));
        }

        return answers;
    }
}

/// <summary>The narrowest read of the seats an application can write: a member reads itself, and no other seat.</summary>
[RowAccess<HostSeat>(RowOperations.Read)]
public static partial class MembersReadOnlyThemselves
{
    /// <summary>Whether <paramref name="seat"/> is the calling seat.</summary>
    public static bool Allows(HostSeat seat, Caller caller) => seat.Id == TenancyRowAccess.CallerSeat<SeatId>();
}

/// <summary>A member reads the people of its own part of the tree: the seats placed at a unit it is placed at, or below one.</summary>
[RowAccess<HostSeat>(RowOperations.Read)]
public static partial class MembersReadThePeopleOfTheirUnits
{
    /// <summary>Whether <paramref name="seat"/> is placed at a unit the calling seat belongs to.</summary>
    public static bool Allows(HostSeat seat, Caller caller) => TenancyRowAccess.SeatsInMyUnits<SeatId>().Contains(seat.Id);
}

/// <summary>A rule that lets every seat through, which Tenancy holds to the calling seat's tenant.</summary>
[RowAccess<HostSeat>(RowOperations.Read)]
public static partial class MembersReadEverySeat
{
    /// <summary>Always.</summary>
    public static bool Allows(HostSeat seat, Caller caller) => true;
}

/// <summary>A seat that gives roles at an invitation's unit reads it, as the seats managers there do.</summary>
[RowAccess<HostInvitation>(RowOperations.Read)]
public static partial class GrantsManagersReadTheInvitationsAtTheirUnits
{
    /// <summary>Whether the calling seat manages grants at <paramref name="invitation"/>'s unit.</summary>
    public static bool Allows(HostInvitation invitation, Caller caller)
        => TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(TenancyKeys.GrantsManage).Contains(invitation.UnitId);
}
