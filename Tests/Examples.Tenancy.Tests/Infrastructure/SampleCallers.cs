using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The callers a request has, begun by hand: for a test that sends a command or a query the way anything but a
/// route sends it, from a scope of the host's services.
/// </summary>
/// <remarks>
/// A request has two callers, and tenant selection begins both: the toolkit's, which is who the token says is
/// calling, and Tenancy's, which is the seat that person has in the tenant the request names. The host runs
/// nothing that names no caller, and the database runs every statement as the toolkit's caller, so a test that
/// began the seat alone would be refused before its first statement.
/// <para>
/// System work begins both through <c>TenancyWork</c>, as the application's own code does.
/// </para>
/// </remarks>
public static class SampleCallers
{
    /// <summary>
    /// Begins the callers of a request of <paramref name="person"/> in <paramref name="tenant"/>: the signed-in
    /// user, and the seat that person has there. Disposing what it returns ends both, the seat first.
    /// </summary>
    public static IDisposable BeginSeatOf(DemoPerson person, DemoTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(tenant);

        var user = Callers.Begin(Caller.User(person.Id));
        try
        {
            return new Both(user, TenancyCallers.Begin(TenancyCaller<TenantId, SeatId>.InSeat(tenant.Id, tenant.SeatOf(person))));
        }
        catch
        {
            user.Dispose();
            throw;
        }
    }

    /// <summary>Ends the seat and then the user, once.</summary>
    private sealed class Both(IDisposable user, IDisposable seat) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            seat.Dispose();
            user.Dispose();
        }
    }
}
