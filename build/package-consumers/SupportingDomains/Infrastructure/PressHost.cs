using Acme.Press.Manuscripts;
using Acme.Press.Persistence;
using Acme.Press.Tenants;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.Postgres;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Postgres;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.Press;

/// <summary>
/// The module's registration, which the host calls. Each call below but <c>AddTenancyPostgres</c>, which the
/// package declares, is written into this project by a generator, so this does not compile when one of them did
/// not arrive. The module's keys are not registered here: the host registers them, with the call Tenancy's
/// generator writes into it from the list they are marked on.
/// </summary>
public static class PressHost
{
    /// <summary>Registers Tenancy, and the manuscripts beside it.</summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection Add(IServiceCollection services)
    {
        // The toolkit's generator: Tenancy's registration closed over this project's classes.
        services.AddTenancy<TenancyContext>(options =>
        {
            options.Catalogue = PressCatalogue.Application;
            options.NewTenantId = TenantId.CreateSequential;
            options.NewSeatId = SeatId.CreateSequential;
            options.NewUnitId = OrganizationUnitId.CreateSequential;
            options.NewRoleId = RoleId.CreateSequential;
        });

        // Membership's Entity Framework generator, which notices Tenancy here: a manuscript's members are seats.
        services.AddManuscriptMembershipWithTenancy<PressContext>(ManuscriptMembership.Rules);

        // Tenancy on Postgres.
        services.AddTenancyPostgres();

        return services;
    }
}

/// <summary>Tenancy's functions, policies and triggers, for the export to write.</summary>
public sealed class PressTenancyPolicies() : TenancyRowAccessContribution(PressCatalogue.Built);

/// <summary>A manuscript's four set functions, for the export to write.</summary>
public sealed class ManuscriptMembershipFunctions() : MembershipRowAccessContribution<ManuscriptEditor>(ManuscriptMembership.Rules);
