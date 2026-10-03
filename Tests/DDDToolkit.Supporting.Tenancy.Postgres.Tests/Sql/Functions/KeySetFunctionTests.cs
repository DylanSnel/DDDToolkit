using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Microsoft.EntityFrameworkCore;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The questions for several keys at once, and for a part of the tree, asked by a module through Tenancy's read
/// functions and under the policies: each is one statement of the module's own context, composes into the module's
/// query, and answers a seat from its own rights, as the question for one key does.
/// <para>
/// Harbor as seeded: Seth supervises North, which reaches North Pier; Oli operates at North Pier; Ada administers.
/// </para>
/// </summary>
public abstract class KeySetFunctionTests(TenancyPostgres postgres, TenancyNaming names)
{
    private static readonly string[] Keys = [HostCatalogue.WidgetChange, TenancyKeys.UnitsManage, TenancyKeys.RolesManage];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_module_asks_for_several_keys_in_one_statement_of_its_own()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder));

        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, async scoped =>
        {
            var widgets = scoped.Widgets();
            var tenancy = scoped.Answers().Over(widgets);

            // Every pair of a unit and a key, from the seat's own rights: one statement.
            recorder.Clear();
            var held = await tenancy.WhereIHold(Keys).ToListAsync(Cancellation);
            recorder.Sent.Should().ContainSingle();
            held.Should().BeEquivalentTo(
                new UnitKey<OrganizationUnitId>[]
                {
                    new(North, HostCatalogue.WidgetChange), new(NorthPier, HostCatalogue.WidgetChange),
                    new(North, TenancyKeys.UnitsManage), new(NorthPier, TenancyKeys.UnitsManage),
                },
                "a supervisor at North holds two of the three keys, there and below");

            // In the module's own query: each widget with the keys held where it hangs.
            recorder.Clear();
            var abilities = await (
                from widget in widgets.Widgets
                join pair in tenancy.WhereIHold(Keys) on widget.UnitId equals pair.Unit
                orderby widget.Name, pair.Key
                select new { widget.Name, pair.Key }).ToListAsync(Cancellation);
            recorder.Sent.Should().ContainSingle("the question is a part of the module's statement");
            recorder.Sent[0].Text.Should().Contain(TenancyFunctionNames.CallerRights).And.NotContain(names.Of("SeatRights"), "a module reads Tenancy through its functions, and names no table of it");
            abilities.Select(row => (row.Name, row.Key)).Should().Equal(
                ("Pump", TenancyKeys.UnitsManage), ("Pump", HostCatalogue.WidgetChange),
                ("Valve", TenancyKeys.UnitsManage), ("Valve", HostCatalogue.WidgetChange));

            // The roles that grant the keys, and the part of the tree under a unit, one statement each.
            recorder.Clear();
            var roles = await tenancy.RoleKeys([HostCatalogue.WidgetCreate, TenancyKeys.RolesManage]).ToListAsync(Cancellation);
            recorder.Sent.Should().ContainSingle();
            roles.Should().BeEquivalentTo(
                new RoleWithKey<RoleId>[]
                {
                    new(HarborRoles.Administrator, HostCatalogue.WidgetCreate), new(HarborRoles.Administrator, TenancyKeys.RolesManage),
                    new(HarborRoles.Supervisor, HostCatalogue.WidgetCreate), new(HarborRoles.Operator, HostCatalogue.WidgetCreate),
                },
                "Harbor's roles, and no other tenant's");

            recorder.Clear();
            (await tenancy.UnitsUnder(North).ToListAsync(Cancellation)).Should().BeEquivalentTo([North, NorthPier]);
            (await tenancy.UnitsUnder(OrchardRoot).ToListAsync(Cancellation)).Should().BeEmpty("a unit of another tenant answers nothing");
            recorder.Sent.Should().HaveCount(2);
        });

        // A seat is answered from its own rights, whatever the others hold: Oli at North Pier, and Ada everywhere.
        (await AnsweredToAsync(services, Oli)).Should().BeEquivalentTo([(NorthPier, HostCatalogue.WidgetChange)]);
        (await AnsweredToAsync(services, Ada)).Should().BeEquivalentTo(new[] { HarborRoot, North, South, NorthPier }.SelectMany(unit => Keys.Select(key => (unit, key))));

        // System work of the module in the tenant holds every key at every unit there.
        var bySystem = await services.BySystemIn(
            Harbor,
            async scoped => (await scoped.Answers().Over(scoped.Widgets()).WhereIHold(Keys).ToListAsync(Cancellation)).Select(pair => (pair.Unit, pair.Key)).ToList(),
            scope: "widgets");
        bySystem.Should().BeEquivalentTo(new[] { HarborRoot, North, South, NorthPier }.SelectMany(unit => Keys.Select(key => (unit, key))));
    }

    private static Task<List<(OrganizationUnitId Unit, string Key)>> AnsweredToAsync(TenancyServices services, Person person)
        => services.BySeat(person.Identity, Harbor, person.Seat, async scoped =>
            (await scoped.Answers().Over(scoped.Widgets()).WhereIHold(Keys).ToListAsync(Cancellation)).Select(pair => (pair.Unit, pair.Key)).ToList());
}

/// <summary>The questions for several keys, through the read functions, under the names Entity Framework gives the tables and columns.</summary>
public sealed class KeySetFunctionTestsOnDefaultNames(TenancyPostgres postgres) : KeySetFunctionTests(postgres, TenancyNaming.Default);

/// <summary>The questions for several keys, through the read functions, under snake_case names with enums stored as snake_case text.</summary>
public sealed class KeySetFunctionTestsOnSnakeCase(TenancyPostgres postgres) : KeySetFunctionTests(postgres, TenancyNaming.SnakeCase);
