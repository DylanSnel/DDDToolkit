using Microsoft.AspNetCore.Authorization;

namespace Examples.Tenancy.Host.Access;

/// <summary>The host's authorization policies, by name, and their registration.</summary>
public static class SamplePolicies
{
    /// <summary>
    /// The policy of every route that works inside a tenant: the request resolved to an active seat in the tenant
    /// it named (<see cref="SeatRequirement"/>).
    /// </summary>
    public const string SeatRequired = "seat-required";

    /// <summary>
    /// The policy of every route of the application's own staff: the request's token carries the operators' role
    /// (<see cref="OperatorRequirement"/>).
    /// </summary>
    public const string OperatorRequired = "operator-required";

    /// <summary>
    /// Registers authorization with <see cref="SeatRequired"/>, the handler that decides it and the answer a
    /// request without a seat gets. Any route can ask for the policy: a group with
    /// <c>RequireAuthorization(SamplePolicies.SeatRequired)</c>, a controller with
    /// <c>[Authorize(Policy = SamplePolicies.SeatRequired)]</c>.
    /// </summary>
    /// <param name="services">The host's services.</param>
    public static IServiceCollection AddSampleSeatPolicy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorizationBuilder()
            .AddPolicy(SeatRequired, policy => policy.RequireAuthenticatedUser().AddRequirements(new SeatRequirement()));
        services.AddSingleton<IAuthorizationHandler, SeatRequirementHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, SeatRefusalResults>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="OperatorRequired"/> and the handler that decides it. A request that fails it is
    /// answered by what answers one that fails <see cref="SeatRequired"/>, with the refusal's code.
    /// </summary>
    /// <param name="services">The host's services.</param>
    public static IServiceCollection AddSampleOperatorPolicy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorizationBuilder()
            .AddPolicy(OperatorRequired, policy => policy.RequireAuthenticatedUser().AddRequirements(new OperatorRequirement()));
        services.AddSingleton<IAuthorizationHandler, OperatorRequirementHandler>();
        return services;
    }
}
