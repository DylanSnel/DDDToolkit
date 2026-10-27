using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy;
using Examples.Tenancy.Tenants.Contracts.TokenRoles;
using Microsoft.AspNetCore.Authorization;

namespace Examples.Tenancy.Host.Access;

/// <summary>
/// Decides <see cref="OperatorRequirement"/> from the request's own user, which tenant selection began from the
/// validated token before authorization runs. It fails with <c>tenancy.operators-only</c> as its reason.
/// </summary>
/// <param name="user">Who is calling.</param>
public sealed class OperatorRequirementHandler(ICurrentUser user) : AuthorizationHandler<OperatorRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperatorRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The question the modules' access checks ask, of the same user.
        if (SampleTokenRoles.IsOperator(user))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, TenancyRefusals.OperatorsOnly));
        }

        return Task.CompletedTask;
    }
}
