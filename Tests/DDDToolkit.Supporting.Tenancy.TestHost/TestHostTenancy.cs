using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using DDDToolkit.Supporting.Tenancy.TestHost.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.TestHost;

/// <summary>
/// The application's Tenancy registration, stored in <see cref="TestTenancyContext"/>. It lives here, next to
/// the classes it is closed over, as an application's does: <c>AddTenancy&lt;TestTenancyContext&gt;</c> is the
/// one the generator writes into this project, internal to it, so tests call this rather than naming the classes.
/// </summary>
public static class TestHostTenancy
{
    /// <summary>
    /// Registers Tenancy over <see cref="TestTenancyContext"/> with <see cref="HostCatalogue.Application"/>, and its
    /// invitations, with the lifetimes the package starts with; and Tenancy's access check for the requests of this
    /// module (<see cref="IHostRequest"/>), asked over the same context. New ids are the ids' own: a tenant's is the
    /// next number of the process (<see cref="TenantId.Create"/>), the others sequential Guids.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Changes the options after those defaults, for a test that needs another catalogue.</param>
    /// <param name="invitations">Changes the invitations' options after their defaults, for a test that needs other lifetimes.</param>
    public static IServiceCollection Add(
        IServiceCollection services,
        Action<TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>>? configure = null,
        Action<TenancyInvitationOptions<InvitationId>>? invitations = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTenancy<TestTenancyContext>(options =>
        {
            options.Catalogue = HostCatalogue.Application;
            configure?.Invoke(options);
        });

        // Generated like AddTenancy, closed over this project's four ids: the module names its request interface
        // and its context.
        services.AddTenancyAccess<IHostRequest, TestTenancyContext>();

        // Generated like AddTenancy, closed over this project's classes: the invitation class and its id are named.
        return services.AddTenancyInvitations<HostInvitation, InvitationId, TestTenancyContext>(invitations);
    }
}
