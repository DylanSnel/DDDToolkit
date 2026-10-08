using Microsoft.AspNetCore.Authorization;

namespace Examples.Tenancy.Host.Access;

/// <summary>
/// Decides <see cref="SeatRequirement"/> from the request's Tenancy caller, which tenant selection began before
/// authorization runs, asked through the class the Tenants module's use cases are named through, closed over its ids.
/// It fails with the code of the refusal the caller resolved to as its reason.
/// </summary>
public sealed class SeatRequirementHandler : AuthorizationHandler<SeatRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SeatRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (SeatRequirement.RefusalFor(TenancyUseCases.CurrentCaller()) is { } refusal)
        {
            context.Fail(new AuthorizationFailureReason(this, refusal.Code));
        }
        else
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
