using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Tenancy's questions as a row access rule asks them are the database's to answer: asked in C#, each throws,
/// naming the SQL function that answers it, relative to Tenancy's owner, whatever schema the application gives
/// Tenancy's tables.
/// </summary>
public class TenancyRowAccessTests
{
    [Fact]
    public void Every_question_is_the_databases_and_throws_in_csharp_naming_its_function()
    {
        TenancyRowAccess.Owner.Should().Be("tenancy");

        Asked(() => TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(HostCatalogue.WidgetRead).Contains(OrganizationUnitId.CreateSequential()))
            .Should().Be("tenancy/units_where_i_hold");
        Asked(() => TenancyRowAccess.ReadableUnits<OrganizationUnitId>().Contains(OrganizationUnitId.CreateSequential())).Should().Be("tenancy/readable_units");
        Asked(() => TenancyRowAccess.SeatsInMyUnits<SeatId>().Contains(SeatId.CreateSequential())).Should().Be("tenancy/seats_in_my_units");
        Asked(() => TenancyRowAccess.RolesWithKey<RoleId>(HostCatalogue.WidgetRead).Contains(RoleId.CreateSequential())).Should().Be("tenancy/roles_with_key");
        Asked(() => TenancyRowAccess.CallerSeat<SeatId>()).Should().Be("tenancy/caller_seat");
        Asked(() => TenancyRowAccess.CallerTenant<TenantId>()).Should().Be("tenancy/caller_tenant");
        Asked(() => TenancyRowAccess.HoldsKey(HostCatalogue.WidgetRead)).Should().Be("tenancy/holds_key");
        Asked(() => TenancyRowAccess.HoldsTenantWide(TenancyKeys.SeatsManage)).Should().Be("tenancy/holds_tenant_wide");
    }

    [Fact]
    public void The_questions_that_take_the_tenant_are_the_databases_too()
    {
        var tenant = new TenantId(1);

        Asked(() => TenancyRowAccess.SeatInTenant<SeatId, TenantId>(tenant)).Should().Be("tenancy/seat_in_tenant");
        Asked(() => TenancyRowAccess.SeatedInTenant(tenant)).Should().Be("tenancy/seated_in_tenant");
        Asked(() => TenancyRowAccess.HoldsKeyInTenant(tenant, HostCatalogue.WidgetRead)).Should().Be("tenancy/holds_key_in_tenant");
        Asked(() => TenancyRowAccess.UnitsWhereIHoldInTenant<OrganizationUnitId, TenantId>(tenant, HostCatalogue.WidgetRead).Contains(OrganizationUnitId.CreateSequential()))
            .Should().Be("tenancy/units_where_i_hold_in_tenant");
        Asked(() => TenancyRowAccess.RolesWithKeyInTenant<RoleId, TenantId>(tenant, HostCatalogue.WidgetRead).Contains(RoleId.CreateSequential()))
            .Should().Be("tenancy/roles_with_key_in_tenant");
    }

    /// <summary>The function <paramref name="question"/> says only the database answers, as the <see cref="DatabaseOnlyException"/> it throws names it.</summary>
    private static string Asked(Func<object> question)
        => FluentActions.Invoking(question).Should().Throw<DatabaseOnlyException>().Which.SqlText;
}
