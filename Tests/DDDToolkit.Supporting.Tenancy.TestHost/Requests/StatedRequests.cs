using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Requests;

/// <summary>
/// One request of the host's tenancy module for each requirement a request can state with an attribute, which the
/// toolkit's generator turns into the call it stands for: the cases the tests of Tenancy's check hold a caller to,
/// said the short way a module says them.
/// </summary>
public static class StatedRequests
{
    /// <summary>Anyone may send it.</summary>
    [AllowAnonymousCallers]
    public sealed record ForAnyone : IHostRequest;

    /// <summary>A signed-in user, who need not have a seat.</summary>
    [SignedIn]
    public sealed record ForTheSignedIn : IHostRequest;

    /// <summary>A caller who works in the tenant: a seat there, or system work there.</summary>
    [InTenant]
    public sealed record InTheTenant : IHostRequest;

    /// <summary>The key that manages seats, for the whole tenant: a key without a marker.</summary>
    [RequiresKey(TenancyKeys.SeatsManage)]
    public sealed record ManageEverySeat : IHostRequest;

    /// <summary>The key that manages seats, at the unit it names or above it.</summary>
    /// <param name="Unit">The unit.</param>
    [RequiresKey(TenancyKeys.SeatsManage)]
    public sealed record ManageSeatsAt([AtUnit] OrganizationUnitId Unit) : IHostRequest;

    /// <summary>One of the application's operators.</summary>
    [RequiresOperator]
    public sealed record ForOperators : IHostRequest;
}
