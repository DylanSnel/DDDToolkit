using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// A request that lets one caller or another through, <c>AccessRequirement.Any(TenancyAccess.Operator(),
/// TenancyAccess.AtTenant(TenancyPermissions.SeatsManage))</c>, over a table of the application's own, the tallies:
/// the door decides per request and reads no row, and the policies decide per row, as the caller. Postgres lets a row
/// through that one permissive policy allows, so each way the door lets a caller in needs a row rule of its own: without
/// it the caller passes the door and reads nothing.
/// </summary>
/// <remarks>
/// Roles are the server's, and the other tests of the run share the server: the operators' role is the one the
/// tallies' tests use, under the default names alone.
/// </remarks>
public sealed class OneUserOrAnotherTests(TenancyPostgres postgres)
{
    /// <summary>The verified identity an operator signs in with: a person with no seat anywhere.</summary>
    private static readonly Guid Operator = Guid.Parse("d0000000-0000-4000-8000-000000000078");

    /// <summary>The operators' branch of the request, as a row rule.</summary>
    private static readonly RowAccessRule ReadByOperators = RowAccessRule.For<Tally>(
        "Operators read every tally", RowOperations.Read, OperatorsReadEveryTally.RowAccessSql, TallyRules.Operators);

    /// <summary>The seat managers' branch of the request, as a row rule.</summary>
    private static readonly RowAccessRule ReadBySeatManagers = RowAccessRule.For<Tally>(
        "Seat managers read the tallies", RowOperations.Read, SeatManagersReadTheTallies.RowAccessSql, RowAccessRoles.User);

    /// <summary>What the request requires: an operator, or a seat that manages seats for the whole tenant.</summary>
    private static readonly AccessRequirement OperatorOrSeatManager = AccessRequirement.Any(
        TenancyAccess.Operator(),
        TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Claims => $$"""{"sub":"{{Operator}}","role":"{{TallyRules.OperatorTokenRole}}"}""";

    /// <summary>A request of the tenancy module that declares <paramref name="Requires"/>.</summary>
    private sealed record Declaring(AccessRequirement Requires) : IHostRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    [Fact]
    public async Task Each_way_through_the_door_reads_the_rows_a_rule_of_its_own_lets_it()
    {
        var database = await TallyDatabase.CreateAsync(postgres, Cancellation, operators: true, tallyRules: [ReadByOperators, ReadBySeatManagers]);
        await using var services = TallyDatabase.Services(database, operators: true);
        await SeedAsync(services);
        var log = new Declaring(OperatorOrSeatManager);

        // Ada administers Harbor: let through by the permission, and the seat managers' rule lets her read Harbor's tallies.
        var read = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, async scoped =>
        {
            await RequireAsync(scoped, log);
            return await scoped.Tallies().Tallies.IgnoreQueryFilters([TenancyQueryFilter.Name]).Select(tally => tally.Name).OrderBy(name => name).ToListAsync(Cancellation);
        });
        read.Should().Equal(["Barrels", "Crates"], "Harbor's, and no other tenant's: Tenancy keeps a seat to its tenant, whatever the module's rule says");

        // An operator: let through by the first case, and the operators' rule lets it read every tenant's.
        using (TestUser.BeginAs(CurrentUserSnapshot.FromClaims(Claims)))
        using (TenancyUsers.Begin(HostUser.Nobody(TenancyRefusals.NotSeated)))
        {
            var across = await services.InScopeAsync(async scoped =>
            {
                await RequireAsync(scoped, log);
                return await scoped.Tallies().Tallies.IgnoreQueryFilters([TenancyQueryFilter.Name]).Select(tally => tally.Name).OrderBy(name => name).ToListAsync(Cancellation);
            });
            across.Should().Equal(["Apples", "Barrels", "Crates"]);
        }

        // Hiro manages seats at North only, and is no operator: neither way is met, and one refusal says both.
        var refused = (await FluentActions.Awaiting(() => services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => RequireAsync(scoped, log)))
            .Should().ThrowAsync<RefusalException>()).Which;
        refused.Code.Should().Be(ToolkitRefusals.Refused);
        ((IReadOnlyList<RefusalReason>)refused.Arguments[ToolkitRefusals.ReasonsArgument]!).Select(reason => reason.Code)
            .Should().Equal(TenancyRefusals.OperatorsOnly, TenancyRefusals.NotPermitted);

        // Past the door, a statement of his own reads nothing: no rule lets his row through.
        await using var asHiro = await AsUser.PersonAsync(database, Hiro.Identity, Harbor, Cancellation);
        (await asHiro.ScalarAsync<long>($"SELECT count(*) FROM {TallyDatabase.NamesOf(database).Table}", Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task A_way_through_the_door_without_a_row_rule_of_its_own_reads_nothing()
    {
        // The operators' rule alone: the door still lets a seat manager through, and the policies let no row of hers through.
        var database = await TallyDatabase.CreateAsync(postgres, Cancellation, operators: true, tallyRules: [ReadByOperators]);
        await using var services = TallyDatabase.Services(database, operators: true);
        await SeedAsync(services);

        var read = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, async scoped =>
        {
            await RequireAsync(scoped, new Declaring(OperatorOrSeatManager));
            return await scoped.Tallies().Tallies.CountAsync(Cancellation);
        });

        read.Should().Be(0, "the door decides per request, the policies per row, and Postgres lets through what one policy for her role allows: none here");
    }

    [Fact]
    public void Each_rule_is_written_as_a_policy_for_its_own_role()
    {
        var script = string.Concat(TallyDatabase.Scripts(operators: true, tallyRules: [ReadByOperators, ReadBySeatManagers]));

        script.Should().Contain(
            $"CREATE POLICY \"Operators read every tally (select) for {TallyRules.OperatorRole}\" ON \"tallies\".\"Tallies\" FOR SELECT TO {TallyRules.OperatorRole}\n"
            + "    USING (TRUE);\n");
        script.Should().Contain(
            "CREATE POLICY \"Seat managers read the tallies (select) for authenticated\" ON \"tallies\".\"Tallies\" FOR SELECT TO authenticated\n"
            + "    USING ((SELECT \"tenancy\".holds_at_tenant('tenancy.seats.manage')));\n");
    }

    /// <summary>Two tallies in Harbor and one in Orchard, as the tallies' own system work in each.</summary>
    private static async Task SeedAsync(TenancyServices services)
    {
        await services.BySystemIn(
            Harbor,
            async scoped =>
            {
                scoped.Tallies().Tallies.AddRange(new Tally(TallyId.CreateSequential(), Harbor, North, "Crates"), new Tally(TallyId.CreateSequential(), Harbor, North, "Barrels"));
                await scoped.Tallies().SaveChangesAsync(Cancellation);
            },
            scope: TallyContext.Schema);
        await services.BySystemIn(
            Orchard,
            async scoped =>
            {
                scoped.Tallies().Tallies.Add(new Tally(TallyId.CreateSequential(), Orchard, OrchardRoot, "Apples"));
                await scoped.Tallies().SaveChangesAsync(Cancellation);
            },
            scope: TallyContext.Schema);
    }

    private static Task RequireAsync(IServiceProvider scoped, Declaring request)
        => scoped.GetRequiredService<AccessChecks<IHostRequest>>().RequireAsync(request, Cancellation).AsTask();
}

/// <summary>The application's operators read every tally, in every tenant: the operators' way through the door, as a row rule.</summary>
[RowAccess<Tally>(RowOperations.Read, To = [TallyRules.Operators])]
public static partial class OperatorsReadEveryTally
{
    /// <summary>Every tally.</summary>
    public static bool Allows(Tally tally, ICurrentUser user) => true;
}

/// <summary>
/// A seat that manages seats for the whole tenant reads its tenant's tallies: the seat managers' way through the door, as
/// a row rule. Tenancy keeps every row of a table kept to a tenant to the caller's tenant itself.
/// </summary>
[RowAccess<Tally>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class SeatManagersReadTheTallies
{
    /// <summary>Whether the calling seat manages seats for the whole tenant.</summary>
    public static bool Allows(Tally tally, ICurrentUser user) => TenancyRowAccess.HoldsAtTenant(TenancyPermissions.SeatsManage);
}
