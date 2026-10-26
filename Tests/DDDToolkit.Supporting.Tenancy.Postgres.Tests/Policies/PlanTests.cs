using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// A module's rule that asks Tenancy's set-shaped question is worked out once per statement, not once per row: Postgres
/// plans the question as an init plan, whose result the scan compares with. A small copy of the check the sample's
/// scale test makes on a large tenant.
/// </summary>
public sealed class PlanTests(TenancyPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_consumer_rule_over_units_where_i_hold_is_an_init_plan()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation);

        // The policy alone: every widget Seth may read, with nothing in the query that narrows it.
        var plan = await seth.PlanAsync("SELECT \"Id\" FROM widgets.\"Widgets\"", Cancellation);

        QueryPlans.InitPlansCalling(plan, "tenancy.units_where_i_hold(").Should().NotBeEmpty("the question is asked once, before the scan");
        QueryPlans.InitPlansCalling(plan, "tenancy.caller_tenant(").Should().NotBeEmpty("and so is the tenant the restrictive policy compares with");

        var scans = QueryPlans.Scans(plan, "Widgets");
        scans.Should().NotBeEmpty();
        scans.Should().OnlyContain(
            scan => (scan.Filter ?? string.Empty).Contains("units_where_i_hold(", StringComparison.Ordinal) == false
                    && (scan.IndexCondition ?? string.Empty).Contains("units_where_i_hold(", StringComparison.Ordinal) == false,
            "no row calls the function itself");

        (await seth.ListAsync<string>("SELECT \"Name\" FROM widgets.\"Widgets\" ORDER BY 1", Cancellation)).Should().Equal("Pump", "Valve");
    }
}
