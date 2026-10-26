using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

/// <summary>
/// Tenancy's use cases, closed over the application's classes and ids. They are the one door to the rules
/// that span aggregates: who may do what, where (the caller's keys at a unit); that a role that manages access
/// is given or taken away, and its seat suspended, deactivated or reactivated, only by someone who holds its
/// keys that manage access, there and for at least as long, and never given by a seat to itself, while other
/// roles are given by whoever holds <c>tenancy.grants.manage</c> where the seat is placed; that a move gains the
/// mover nothing, and gives or takes away from anyone else no key that manages access the mover could not; and
/// that a tenant always keeps an administrator.
/// <para>
/// An application does not close the class itself. The toolkit's generator closes it over the classes a module
/// declares with Tenancy's templates, in the project that declares them, as a class of this one's name without its
/// type parameters:
/// <code>
/// public abstract class TenancyUseCases : global::DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases&lt;
///     global::Shop.Domain.ShopTenant, global::Shop.Contracts.TenantId, ..., global::Shop.Contracts.RoleId&gt;
/// {
///     private TenancyUseCases() { }
/// }
/// </code>
/// whatever the module is called; C# tells the two apart by their type parameters. A type nested in a class is found
/// through every class derived from it, so every project that sees that one, the module's application and API projects
/// and the host among them, takes a <c>TenancyUseCases.SeatCommands</c> and answers a <c>TenancyUseCases.SeatOverview</c>:
/// the types nested here, closed over the module's classes, which the container registered and whose documentation
/// shows. Each type parameter's <c>[TemplateType]</c> says which class fills it, as <c>AddTenancy</c>'s do, and the
/// package's <c>[assembly: TemplateFacade]</c> asks for the class. That is why this class is abstract rather than
/// static. An application whose two modules both declare Tenancy's classes names one of the two with
/// <c>[assembly: TemplateFacadeName("TenancyUseCases", "CustomersTenancy")]</c> in that module's project.
/// </para>
/// <para>
/// A static member is found through a derived class as a nested type is, so the same class closes over the ids what
/// is called rather than named: system work, <c>TenancyUseCases.BeginSystem()</c> and <c>TenancyUseCases.BeginSystemIn(tenant)</c>,
/// and the current caller, <c>TenancyUseCases.CurrentCaller()</c>. <see cref="Access.TenancyWork"/> and
/// <see cref="Access.TenancyCallers"/> keep them generic over the ids, for code that sees only those.
/// </para>
/// <para>
/// They are plain services, not handlers: each method checks the Tenancy caller, loads what it needs
/// through <see cref="IStore"/>, calls the aggregates, and saves once. Every command starts by asking who is
/// calling. Nobody is refused, a seat is asked for its keys, and system work in a tenant holds every key
/// there. System work outside any tenant only provisions.
/// </para>
/// <para>
/// A new tenant, unit, seat, role or invitation gets its id from the id itself, <c>TSeatId.Create()</c>, in code and
/// before the save, so the change and every event of it know the id from the start; a command given an id, for an
/// import or fixed seed data, uses that one. That is why each id is an <see cref="ICreatableEntityId{TSelf}"/>: the
/// generator writes <c>Create()</c> for an id over a <see cref="Guid"/>, and an id over anything else declares it.
/// </para>
/// <para>
/// A command that changes rights, or what they reach, first takes the tenant's access revision, before it
/// reads anything, so that two such commands committed at the same time cannot both have decided on what
/// the other changed. Placing a seat and archiving a unit take it as well: a placement widens what a seat may
/// read, and an archived unit takes no new placement or grant, so a placement or a grant that found the unit
/// active cannot commit next to the archive that ended it. Every command decides everything before it
/// changes an aggregate, so a refused command leaves nothing behind that a later save in the same unit of
/// work would write.
/// </para>
/// <para>
/// The rules that span aggregates hold for what goes through these use cases, and only for that. The
/// aggregates' public methods check the aggregate's own state; code of the application that calls
/// <c>Seat.Revoke</c> or <c>Role.Archive</c> itself skips the key checks, containment and self-appointment,
/// and the last-administrator rule, and answers for them. A closed tenant is closed to its seats, because no seat
/// is selected in it; system work begun in it is not stopped.
/// </para>
/// </summary>
public abstract partial class TenancyUseCases<
    [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
    [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
    [TemplateType(typeof(OrganizationAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TOrganization,
    [TemplateType(typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)] TUnit,
    [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
    [TemplateType(typeof(SeatAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TSeat,
    [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
    [TemplateType(typeof(RoleAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TRole,
    [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId>
    where TTenant : TenantAggregate<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
{
    /// <summary>
    /// For the class the generator writes for an application, which derives from this one closed over its classes
    /// so that every nested type and static member is named through it. Nothing makes an instance: everything here
    /// is a nested type or a static member.
    /// </summary>
    protected TenancyUseCases()
    {
    }
}
