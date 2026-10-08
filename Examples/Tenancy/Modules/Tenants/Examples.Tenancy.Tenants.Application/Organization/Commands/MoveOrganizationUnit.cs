using Mediator;

namespace Examples.Tenancy.Tenants.Application.Organization.Commands;

/// <summary>
/// Hangs a unit, with everything below it, under another parent.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant. The rest is for the package's use case to ask, since only it reads
/// where the unit hangs now: <c>tenancy.units.manage</c> at the old parent and at the new one, and that the move
/// gives or takes away nothing the caller could not.
/// </remarks>
/// <param name="Unit">The unit to move.</param>
/// <param name="Parent">The unit it hangs under from now on.</param>
public sealed record MoveOrganizationUnit(OrganizationUnitId Unit, OrganizationUnitId Parent) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
}

/// <summary>Handles <see cref="MoveOrganizationUnit"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="organization">The package's use cases that change the organization.</param>
public sealed class MoveOrganizationUnitHandler(TenancyUseCases.OrganizationCommands organization) : ICommandHandler<MoveOrganizationUnit>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(MoveOrganizationUnit command, CancellationToken cancellationToken)
    {
        await organization.MoveUnitAsync(command.Unit, command.Parent, cancellationToken);
        return Unit.Value;
    }
}
