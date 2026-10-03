using Mediator;

namespace Examples.Tenancy.Tenants.Application.Roles.Commands;

/// <summary>
/// Sets the keys a role grants, for every seat that holds it. The keys become exactly the list: an empty one
/// takes every key off the role.
/// </summary>
/// <remarks>
/// The package decides who may: its use case asks for <c>tenancy.roles.manage</c> for the whole tenant, and that only
/// an administrator adds or takes out a key that manages access.
/// </remarks>
/// <param name="Role">The role.</param>
/// <param name="Keys">The keys it grants from now on.</param>
public sealed record SetRoleKeys(RoleId Role, IReadOnlyList<string> Keys) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Handles <see cref="SetRoleKeys"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="roles">The package's use cases that change a role.</param>
public sealed class SetRoleKeysHandler(SampleTenancy.RoleCommands roles) : ICommandHandler<SetRoleKeys>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(SetRoleKeys command, CancellationToken cancellationToken)
    {
        await roles.SetKeysAsync(command.Role, command.Keys, cancellationToken);
        return Unit.Value;
    }
}
