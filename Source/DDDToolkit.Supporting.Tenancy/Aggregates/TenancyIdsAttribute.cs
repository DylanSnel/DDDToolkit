using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Says which of a project's ids are Tenancy's, for a module that sees Tenancy's ids and none of its classes. Write it
/// once, in the module's application project:
/// <code>
/// using DDDToolkit.Supporting.Tenancy;
///
/// [assembly: TenancyIds&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;]
/// </code>
/// <para>
/// The generator then writes Tenancy's names for those ids into the module, as it writes them into the module that
/// declares the classes. The project with the line gets <c>TenancyAccessManager</c>, in its root namespace, so the
/// module's code under that namespace names it without a using. The module's project that holds its context, below the
/// one with the line, gets <c>db.Tenancy()</c>, <c>services.AddTenancyAccess&lt;TRequests, TContext&gt;()</c>, which
/// registers that <c>TenancyAccessManager</c> too, and <c>modelBuilder.AddTenancyReadFunctions()</c>. None of them names an
/// id again.
/// </para>
/// <para>
/// The module that declares Tenancy's classes needs no line: the generator reads the ids from the classes. A class the
/// module declares with one of Tenancy's templates wins over the line.
/// </para>
/// </summary>
/// <typeparam name="TTenantId">The id of the application's tenant class.</typeparam>
/// <typeparam name="TSeatId">The id of the application's seat class.</typeparam>
/// <typeparam name="TUnitId">The id of the application's organization unit class.</typeparam>
/// <typeparam name="TRoleId">The id of the application's role class.</typeparam>
[TemplateIds]
[AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
public sealed class TenancyIdsAttribute<
    [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
    [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
    [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
    [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId> : Attribute
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
