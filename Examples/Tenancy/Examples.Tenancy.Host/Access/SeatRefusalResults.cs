using DDDToolkit.Supporting.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Examples.Tenancy.Host.Access;

/// <summary>
/// Answers a request that failed <see cref="SeatRequirement"/> with the refusal its caller resolved to, and one
/// that failed <see cref="OperatorRequirement"/> with <c>tenancy.operators-only</c>, as problem+json with its
/// code, instead of the bare 403 authorization answers by itself.
/// </summary>
/// <remarks>
/// It throws the refusal, so <see cref="RefusalProblems"/> writes it: one place turns every refusal into an
/// answer, this one included, in the same shape and the same language. A request without a valid token is not
/// forbidden but challenged, and that, like every other outcome, is left to the framework's own handler.
/// </remarks>
public sealed class SeatRefusalResults : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _otherwise = new();

    /// <inheritdoc />
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailureReasons.Any(reason => reason.Handler is OperatorRequirementHandler) == true)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.OperatorsOnly);
        }

        var forWantOfASeat = authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailureReasons.Any(reason => reason.Handler is SeatRequirementHandler) == true;

        // The caller the handler asked is still the request's: both run inside tenant selection.
        return forWantOfASeat && SeatRequirement.RefusalFor(TenancyUseCases.CurrentCaller()) is { } refusal
            ? throw refusal
            : _otherwise.HandleAsync(next, context, policy, authorizeResult);
    }
}
