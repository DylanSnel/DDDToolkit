using DDDToolkit.Exceptions;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Tenant.Commands;

/// <summary>
/// Marks the tenant the caller works in as one that exists only to demonstrate the application.
/// </summary>
/// <remarks>
/// The one command of this module that is the application's own:
/// <see cref="Domain.Aggregates.Tenants.Tenant.IsDemo"/> is state the application adds to the package's tenant, so
/// no use case of the package sets it. Nobody in a tenant decides that their tenant is a demonstration; whoever
/// seeds one does, so it requires system work, every user is refused, and no route sends it. The seeding sends it as
/// system work in the tenant it marks.
/// </remarks>
public sealed record MarkTenantAsDemo : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.RequiresSystemWork();
}

/// <summary>
/// Handles <see cref="MarkTenantAsDemo"/>: loads the tenant through the package's store, which the package's own
/// use cases load and save through, marks it and saves.
/// </summary>
/// <remarks>
/// Every other command of this module hands its request to a use case of the package, which asks again for what
/// its request requires, and for the rest, whoever calls it. This one changes an aggregate of the package's with
/// nothing of the package in between, so it asks again itself, and narrower: system work in the very tenant it
/// marks goes on, and nothing else. The handler then decides the same wherever it runs, in a program that left the
/// module's access behavior out of the pipeline as much as in the host.
/// </remarks>
/// <param name="store">Where Tenancy's aggregates are loaded and saved: one unit of work per request.</param>
/// <param name="answers">Tenancy's answers about the current caller, for the tenant it works in.</param>
public sealed class MarkTenantAsDemoHandler(SampleTenancy.IStore store, SampleAnswers answers) : ICommandHandler<MarkTenantAsDemo>
{
    /// <inheritdoc />
    /// <exception cref="RefusalException"><c>access.system-only</c> for a seat, whatever it holds.</exception>
    /// <exception cref="InvalidOperationException">The tenant the caller works in is not there: it was never provisioned.</exception>
    public async ValueTask<Unit> Handle(MarkTenantAsDemo command, CancellationToken cancellationToken)
    {
        var scope = answers.RequireTenant();
        if (!scope.BySystem)
        {
            throw ToolkitRefusals.Of(ToolkitRefusals.SystemOnly);
        }

        var tenant = scope.Tenant;
        var marked = await store.FindTenantAsync(tenant, cancellationToken)
            ?? throw new InvalidOperationException($"The tenant {tenant} is not there to mark: provision it first.");

        marked.MarkAsDemo();
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
