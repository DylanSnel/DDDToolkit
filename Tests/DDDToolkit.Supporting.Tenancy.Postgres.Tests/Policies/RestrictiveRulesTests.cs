using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// A restrictive rule of the application's on Tenancy's organization: <c>Restrictive = true</c>, so its policy is
/// AND-ed with every policy that allows, Tenancy's defaults included, and only narrows what they allow. Here only an
/// employee's seat reads the organization, its units and the closure of its tree, which the application says by a
/// field of its own seat class, read through <c>TenancyRowAccess.CurrentSeat&lt;HostSeat&gt;()</c>. Tenancy's own
/// work, and what its functions answer as their owner, keep working under it: every use case runs, and a seat the rule
/// keeps out of the organization still learns what the unit it works at is called.
/// </summary>
public sealed class RestrictiveRulesTests(TenancyPostgres postgres)
{
    private static readonly RowAccessRule OnlyEmployees =
        RowAccessRule.For<HostOrganization>("Only employees read the organization", RowOperations.Read, OnlyEmployeesReadTheOrganization.RowAccessSql);

    /// <summary>Lark's identity: an external person with a seat in Harbor, placed nowhere.</summary>
    private static readonly Guid Lark = Guid.Parse("d0000000-0000-4000-8000-000000000096");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------------------------------------ the export

    [Fact]
    public void The_rule_narrows_the_organization_its_units_and_the_closure_of_its_tree_beside_tenancys_own_policies()
    {
        OnlyEmployees.Restrictive.Should().BeTrue("the generator marks the rule's SQL");

        var tenancy = TenancyPostgres.AccessScripts(rules: [.. WidgetRules.All, OnlyEmployees])[0];

        // The kind is stored as its number, as the TestHost's model maps an enum: an employee's is 0.
        const string Condition = "(SELECT q1.\"Kind\" FROM \"tenancy\".current_seat_row() q1) = 0";
        foreach (var table in new[] { "Organizations", "OrganizationUnits", "OrganizationUnitPaths" })
        {
            tenancy.Should().Contain(
                $"CREATE POLICY \"Only employees read the organization (select) for authenticated\" ON \"tenancy\".\"{table}\" AS RESTRICTIVE FOR SELECT TO authenticated\n    USING ({Condition});",
                "{0} is part of the organization, or follows it", table);
        }

        tenancy.Should().Contain("CREATE POLICY \"Seats read their organizations (select) for authenticated\" ON \"tenancy\".\"Organizations\" FOR SELECT", "Tenancy's own policy stays, and the rule narrows it");
        tenancy.Should().NotContain("\"Only employees read the organization (select) for anon\"", "nothing lets an anonymous caller read the organization, so there is nothing to narrow for one");
        tenancy.Should().NotContain("\"Only employees read the organization (select) for authenticated\" ON \"tenancy\".\"Seats\"", "the rule narrows the organization alone");
    }

    [Fact]
    public void A_rule_that_allows_on_the_organization_is_refused_since_it_could_only_add_to_what_tenancy_allows()
    {
        var allowing = () => TenancyPostgres.AccessScripts(rules: [RowAccessRule.For<HostOrganization>("Everyone reads the organization", RowOperations.Read, "TRUE")]);

        allowing.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Everyone reads the organization' would add a policy to tenancy.Organizations, which the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution keeps to itself*");
    }

    // ------------------------------------------------------------------------------------------------ the database

    [Fact]
    public async Task An_employee_reads_the_units_an_external_seat_reads_none_and_a_seat_of_another_tenant_reads_none_of_this_tenants()
    {
        var database = await ForcedAsync();
        await MarkExternalAsync(database, Oli.Seat);

        // Ada is an employee of Harbor: the rule lets through what Tenancy's policy allows her.
        (await UnitsReadByAsync(database, Ada, Harbor)).Should().BeEquivalentTo([HarborRoot.Value, North.Value, South.Value, NorthPier.Value]);
        (await PathsReadByAsync(database, Ada, Harbor)).Should().Be(8, "the closure of Harbor's tree: four units to themselves, three to the root, and North Pier to North");
        (await OrganizationsReadByAsync(database, Ada, Harbor)).Should().Be(1);

        // Oli's seat in Harbor is an external person's: Tenancy's policy allows him the units, and the rule does not.
        (await UnitsReadByAsync(database, Oli, Harbor)).Should().BeEmpty();
        (await PathsReadByAsync(database, Oli, Harbor)).Should().Be(0);
        (await OrganizationsReadByAsync(database, Oli, Harbor)).Should().Be(0);

        // Odette is an employee of Orchard: she reads Orchard's units, and none of Harbor's, which Tenancy's policy
        // never allowed her. A restrictive rule grants nothing.
        (await UnitsReadByAsync(database, Odette, Orchard)).Should().BeEquivalentTo([OrchardRoot.Value]);
        await using (var odette = await AsUser.PersonAsync(database, Odette.Identity, Orchard, Cancellation))
        {
            (await odette.ListAsync<Guid>("SELECT \"Id\" FROM tenancy.\"OrganizationUnits\" WHERE \"TenantId\" = 1", Cancellation)).Should().BeEmpty();
        }

        // And the same person in another tenant is whatever his seat there is: Oli's seat in Orchard is an employee's.
        (await UnitsReadByAsync(database, Oli, Orchard)).Should().BeEquivalentTo([OrchardRoot.Value]);
    }

    [Fact]
    public async Task Every_use_case_still_runs_under_the_rule()
    {
        var database = await ForcedAsync();
        await using var services = new TenancyServices(database);

        await EveryUseCase.RunAsync(services, Cancellation);
    }

    [Fact]
    public async Task An_external_seat_placed_nowhere_reads_what_a_unit_is_called_by_its_id_and_nothing_else_of_the_organization()
    {
        var database = await ForcedAsync();
        await using var services = new TenancyServices(database);
        var lark = (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Seats().AddAsync(Lark, Cancellation, configure: seat =>
        {
            seat.Rename("Lark");
            seat.MarkExternal();
        }))).Id;

        // Asked by id, the unit her work is at is named, through Tenancy's function that runs as its owner, and for
        // the ids asked alone; another tenant's is left out as before.
        var named = await services.BySeat(Lark, Harbor, lark, scoped => scoped.Organization().UnitsByIdAsync([NorthPier, OrchardRoot], Cancellation));
        named.Select(unit => (unit.Id, unit.Name, unit.Path, unit.Depth)).Should().Equal((NorthPier, "North Pier", "Harbor / North / North Pier", 3));

        // While what she reads of the tables themselves is nothing.
        await using (var caller = await AsUser.PersonAsync(database, Lark, Harbor, Cancellation))
        {
            (await caller.ListAsync<Guid>("SELECT \"Id\" FROM tenancy.\"OrganizationUnits\"", Cancellation)).Should().BeEmpty();
            (await caller.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"OrganizationUnitPaths\"", Cancellation)).Should().Be(0);
        }

        // And the list of the units she reads is empty: the rule keeps her out of the organization.
        (await services.BySeat(Lark, Harbor, lark, scoped => scoped.Organization().ListUnitsAsync(Cancellation))).Should().BeEmpty();
    }

    [Fact]
    public async Task An_external_seats_own_overview_names_the_units_it_is_placed_at_and_nothing_below_them()
    {
        var database = await ForcedAsync();
        await using var services = new TenancyServices(database);
        await MarkExternalAsync(database, Oli.Seat);

        // Oli is placed at North Pier, an Operator there: his overview names the unit through Tenancy's function, as
        // the directory names a unit by its id, and the tenant by its name through the function the picker reads,
        // while the organization itself is what the rule keeps from him.
        var overview = await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => scoped.Seats().WhoAmIAsync(Cancellation));

        overview.TenantName.Should().Be("Harbor Works", "as the tenant picker names it");
        await using (var oli = await AsUser.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Organizations\"", Cancellation)).Should().Be(0, "the organizations stay closed to him");
        }

        (await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => scoped.Seats().OverviewAsync(Oli.Seat, Cancellation))).TenantName
            .Should().Be("Harbor Works", "his overview asked by id is the same");
        overview.Units.Select(unit => unit.Path).Should().Equal("Harbor / North / North Pier");
        overview.Permissions.Should().NotBeEmpty().And.OnlyContain(reach => !reach.AtTenant && reach.Reaches.Select(unit => unit.Id).SequenceEqual(new[] { NorthPier }));
        (await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => scoped.Organization().ListUnitsAsync(Cancellation))).Should().BeEmpty();

        // Ada is an employee, and her overview reads the organization as before.
        var ada = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Seats().WhoAmIAsync(Cancellation));
        ada.TenantName.Should().Be("Harbor Works");
        ada.Permissions.Should().Contain(reach => reach.AtTenant && reach.Reaches.Count == 4);
    }

    [Fact]
    public async Task Every_seat_still_finds_the_seat_it_acts_as_and_the_tenants_it_has_one_in()
    {
        var database = await ForcedAsync();
        await MarkExternalAsync(database, Oli.Seat);
        await using var services = new TenancyServices(database);

        // The seat a request acts as is found before a tenant is chosen, past the organization, which only names it.
        foreach (var (person, slug, seat) in new[] { (Oli, "harbor", Oli.Seat), (Ada, "harbor", Ada.Seat), (Oli, "orchard", OliInOrchard) })
        {
            using (TestUser.BeginAs(person.Identity))
            {
                (await services.InScopeAsync(scoped => scoped.GetRequiredService<TenantSelection<TenantId, SeatId>>().ResolveAsync(CurrentUserSnapshot.User(person.Identity), slug, Cancellation)))
                    .Seat.Should().Be(seat, "{0} has a seat in {1}", person.Name, slug);
            }
        }

        // The tenant picker lists every seat of the person's, each tenant by its name, which Tenancy's function that
        // runs as its owner answers: before a tenant is chosen the rule has no seat to ask about, and lets no
        // organization through, to the employee as to the external seat.
        foreach (var (person, expected) in new[] { (Oli, new[] { ("harbor", "Harbor Works"), ("orchard", "Orchard Works") }), (Ada, new[] { ("harbor", "Harbor Works") }) })
        {
            using (TestUser.BeginAs(person.Identity))
            {
                var mine = await services.InScopeAsync(scoped => scoped.GetRequiredService<TenantSelection<TenantId, SeatId>>().SeatsOfAsync<HostSeat>(CurrentUserSnapshot.User(person.Identity), Cancellation));
                mine.Select(found => (found.Slug, found.OrganizationName)).Should().BeEquivalentTo(expected, "{0} sees the tenants by name", person.Name);
            }
        }

        // While the organizations themselves stay closed to the external seat, before a tenant is chosen and in it.
        foreach (var tenant in new TenantId?[] { null, Harbor })
        {
            await using var oli = await AsUser.PersonAsync(database, Oli.Identity, tenant, Cancellation);
            (await oli.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Organizations\" WHERE \"Id\" = 1", Cancellation)).Should().Be(0);
        }
    }

    private async Task<TestDatabase> ForcedAsync()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await TenancyPostgres.ForceAsync(database, Cancellation, [.. WidgetRules.All, OnlyEmployees]);
        return database;
    }

    /// <summary>Marks a seat as an external person's, past the policies, as an import would.</summary>
    private static Task MarkExternalAsync(TestDatabase database, SeatId seat)
        => TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, $"UPDATE tenancy.\"Seats\" SET \"Kind\" = 1 WHERE \"Id\" = '{seat.Value}'", Cancellation);

    /// <summary>The ids of the units <paramref name="person"/> reads, with the connection naming <paramref name="tenant"/>.</summary>
    private static async Task<List<Guid>> UnitsReadByAsync(TestDatabase database, Person person, TenantId tenant)
    {
        await using var caller = await AsUser.PersonAsync(database, person.Identity, tenant, Cancellation);
        return await caller.ListAsync<Guid>("SELECT \"Id\" FROM tenancy.\"OrganizationUnits\"", Cancellation);
    }

    /// <summary>How many rows of the closure of the tree <paramref name="person"/> reads.</summary>
    private static async Task<long> PathsReadByAsync(TestDatabase database, Person person, TenantId tenant)
    {
        await using var caller = await AsUser.PersonAsync(database, person.Identity, tenant, Cancellation);
        return await caller.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"OrganizationUnitPaths\"", Cancellation);
    }

    /// <summary>How many organizations <paramref name="person"/> reads.</summary>
    private static async Task<long> OrganizationsReadByAsync(TestDatabase database, Person person, TenantId tenant)
    {
        await using var caller = await AsUser.PersonAsync(database, person.Identity, tenant, Cancellation);
        return await caller.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Organizations\"", Cancellation);
    }
}

/// <summary>
/// Only an employee's seat reads the organization: a restrictive rule, which narrows what Tenancy's own policy on the
/// organization allows, and reaches its units and the closure of its tree with it.
/// </summary>
[RowAccess<HostOrganization>(RowOperations.Read, Restrictive = true)]
public static partial class OnlyEmployeesReadTheOrganization
{
    public static bool Allows(HostOrganization organization, ICurrentUser user)
        => TenancyRowAccess.CurrentSeat<HostSeat>().Kind == HostSeatKind.Employee;
}
