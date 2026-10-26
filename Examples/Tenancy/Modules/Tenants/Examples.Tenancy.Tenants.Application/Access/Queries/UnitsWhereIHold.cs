using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Access.Queries;

/// <summary>
/// Where the caller holds a permission, for the "who may do what" screens: the units, by their ids, and whether
/// it holds the permission across the whole tenant.
/// </summary>
/// <remarks>
/// It asks about the caller only, so it needs no permission of its own: whoever works in a tenant may ask where they
/// themselves hold a permission. Asked with no seat behind it, the question would answer "nowhere", so such a caller is
/// refused first, as every use case refuses it; then the permission is checked, which comes from outside.
/// <para>
/// The answer is ids, and no paths: it is built on the access questions, and the rows those read carry no name.
/// Whoever shows the units asks what they are called with <see cref="OrganizationUnitsById"/>.
/// </para>
/// </remarks>
/// <param name="Permission">A permission, as the request names it.</param>
[InTenant]
public sealed record UnitsWhereIHold(string Permission) : IQuery<HeldUnits>, ITenantsRequest;

/// <summary>Where a caller holds a permission: the units, and whether it holds the permission tenant-wide.</summary>
/// <param name="Units">Each unit where the permission applies, by its id, ordered by the id's value so the answer is the same every time.</param>
/// <param name="AtTenant">Whether the permission applies across the whole tenant: whether it is held at the root.</param>
public sealed record HeldUnits(IReadOnlyList<OrganizationUnitId> Units, bool AtTenant);

/// <summary>
/// Answers <see cref="UnitsWhereIHold"/> with the Tenancy package's access questions, asked over a reading of
/// this query's own.
/// </summary>
/// <remarks>
/// One statement. The question "where do I hold this permission" is a set, not a list: it is put inside the query that
/// reads the tenant's units, where it becomes a subquery, so each unit comes back with whether the caller holds
/// the permission there. A tenant has few units, so all of them are read, the root among them, held or not: whether the
/// permission is held at the root is whether it holds for the whole tenant. That is so for system work in the tenant as
/// well, which holds every permission at every unit: a tenant is provisioned with its root, so there is always a root
/// for it to hold the permission at.
/// <para>
/// The questions bind the caller, and the moment, when they are asked for, so they are asked for here, per query,
/// and never kept.
/// </para>
/// </remarks>
/// <param name="reads">Where Tenancy is read: a context of this query's own.</param>
/// <param name="catalogue">The permissions the application knows, for a permission that comes from outside.</param>
/// <param name="answers">Tenancy's answers about the current caller.</param>
public sealed class UnitsWhereIHoldHandler(ITenancyReads reads, TenancyCatalogue catalogue, SampleAnswers answers) : IQueryHandler<UnitsWhereIHold, HeldUnits>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.unknown-permission</c> for a permission the catalogue does not know.</exception>
    public async ValueTask<HeldUnits> Handle(UnitsWhereIHold query, CancellationToken cancellationToken)
    {
        // The questions treat an unknown permission as a bug in the code that asks. Here it is only a wrong request.
        if (!catalogue.Knows(query.Permission))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.UnknownPermission, ("Permissions", query.Permission));
        }

        await using var reading = reads.Open();
        var questions = answers.Over(reading.Rows, reading.Queries);

        // A local, so the storage sees the set itself and translates it into the statement below.
        var held = questions.UnitsWhereIHold(query.Permission);
        var units = await reading.Queries.ListAsync(
            questions.Units().Select(unit => new UnitRead(unit.Id, unit.ParentId, held.Contains(unit.Id))),
            cancellationToken);

        return new HeldUnits(
            [.. units.Where(unit => unit.Held).Select(unit => unit.Id).OrderBy(unit => unit.Value)],
            AtTenant: units.Any(unit => unit.ParentId is null && unit.Held));
    }

    /// <summary>What the statement reads of a unit: whether it is the root, and whether the caller holds the permission there.</summary>
    private sealed record UnitRead(OrganizationUnitId Id, OrganizationUnitId? ParentId, bool Held);
}
