using Campus.Courses;
using Campus.Labs;
using Campus.Persistence;
using Campus.Tenants;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Campus;

/// <summary>
/// The application's registration: Tenancy over <see cref="TenancyContext"/>, and the courses and the labs
/// over <see cref="CampusContext"/>, each with its rules. What those rules ask of the organization is
/// answered by a class written into this project for each of the two, since a member of either is a seat:
/// the application writes one registration for a resource, and no class.
/// </summary>
public static class CampusHost
{
    /// <summary>Registers Tenancy and the two kinds of resource beside it.</summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection Add(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Generated for this project's classes: Tenant, TenantId, Organization and the rest. A new id is the id's own.
        services.AddTenancy<TenancyContext>(options => options.Catalogue = CampusCatalogue.Application);

        // The keys of the courses and the labs that a role of the organization can hold.
        services.AddTenancyPermissions(CampusCatalogue.Permissions);

        // A course: its members are seats, its roles are kept for it, and the organization reaches it from above.
        services.AddCourseMembershipWithTenancy<CampusContext>(CourseMembership.Rules);

        // A lab: the same, with the college's own roles, which the member class says by holding them by Tenancy's role id.
        services.AddLabMembershipWithTenancy<CampusContext>(LabMembership.Rules);

        return services;
    }
}
