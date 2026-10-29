using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The two unique indexes of a role class whose rows belong to a tenant of the Tenancy package: a name once in a tenant,
/// and each starter role once in a tenant. The second also keeps two set-ups of one tenant at the same moment apart.
/// <para>
/// A role class says whose its rows are with Tenancy's marker, <c>[OfTenant] public TenantId TenantId</c>. This is the
/// one file of Membership's packages that names it, and it names it as text: no Membership package references Tenancy,
/// and an application without Tenancy has no such marker, so its role classes get nothing here.
/// </para>
/// </summary>
internal static class OrganizationRoleIndexes
{
    /// <summary>Tenancy's marker of the property that says which tenant a row belongs to, by its full name.</summary>
    private const string OfTenantMarker = "DDDToolkit.Supporting.Tenancy.OfTenantAttribute";

    /// <summary>
    /// Adds the two indexes to <paramref name="role"/> where its class marks its tenant. A class that marks none, or two,
    /// which Tenancy refuses itself, gets none.
    /// </summary>
    /// <param name="role">The builder of the role class's entity type.</param>
    /// <typeparam name="TRole">The application's role class.</typeparam>
    /// <typeparam name="TRoleId">The role class's id.</typeparam>
    public static void Add<TRole, TRoleId>(EntityTypeBuilder<TRole> role)
        where TRole : CustomRoleAggregate<TRoleId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        var marked = typeof(TRole).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static property => property.GetCustomAttributes(inherit: false).Any(static attribute => attribute.GetType().FullName == OfTenantMarker))
            .ToList();
        if (marked.Count != 1)
        {
            return;
        }

        // Named by Entity Framework, as indexes written by hand in a context are: IX_{table}_{tenant}_Name and IX_{table}_{tenant}_MadeFrom.
        role.HasIndex(marked[0].Name, nameof(CustomRoleAggregate<TRoleId>.Name)).IsUnique();
        role.HasIndex(marked[0].Name, nameof(CustomRoleAggregate<TRoleId>.MadeFrom)).IsUnique();
    }
}
