using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Requests;

/// <summary>
/// Runs code the way the pipeline runs the handler of a command marked <c>[Operator]</c>: the operator signs in with the
/// operators' token role, Tenancy's check of the operator case lets the command through, and the code runs inside the
/// operator work the check then begins. Operator work has no other way in, so the tests take this one.
/// </summary>
/// <remarks>
/// It asks the module's set of checks as the generated behavior does: in one <see langword="async"/> method, the checks
/// first and the code inside <see cref="AccessChecks{TRequests}.BeginWork"/>. So whatever the work began ends with the
/// code, and the caller around it is current again.
/// </remarks>
public static class OperatorWork
{
    /// <summary>The token role the operators of these runs sign in with.</summary>
    public const string TokenRole = "operator";

    /// <summary>Runs <paramref name="handler"/> as an operator's command with no tenant: operator work in no tenant, which provisions.</summary>
    /// <param name="identity">The operator's identity.</param>
    /// <param name="handler">What the command's handler does.</param>
    /// <param name="scope">The scope of the work.</param>
    public static Task RunAsync(Guid identity, Func<Task> handler, string scope = TenancyWork.SystemScope)
        => RunAsync(identity, TenancyAccess.Operator(scope: scope), handler);

    /// <summary>Runs <paramref name="handler"/> as an operator's command for <paramref name="tenant"/>: operator work in that tenant.</summary>
    /// <param name="identity">The operator's identity.</param>
    /// <param name="tenant">The tenant the command names with its <c>[ForTenant]</c> member.</param>
    /// <param name="handler">What the command's handler does.</param>
    /// <param name="scope">The scope of the work.</param>
    public static Task RunAsync(Guid identity, TenantId tenant, Func<Task> handler, string scope = TenancyWork.SystemScope)
        => RunAsync(identity, TenancyAccess.Operator(tenant, scope: scope), handler);

    /// <summary>
    /// Runs <paramref name="handler"/> as an operator's command for <paramref name="tenant"/>, and answers what it answers.
    /// </summary>
    /// <param name="identity">The operator's identity.</param>
    /// <param name="tenant">The tenant the command names with its <c>[ForTenant]</c> member.</param>
    /// <param name="handler">What the command's handler does.</param>
    /// <param name="scope">The scope of the work.</param>
    public static async Task<T> RunAsync<T>(Guid identity, TenantId tenant, Func<Task<T>> handler, string scope = TenancyWork.SystemScope)
    {
        T answer = default!;
        await RunAsync(identity, TenancyAccess.Operator(tenant, scope: scope), async () => answer = await handler());
        return answer;
    }

    /// <summary>
    /// Runs <paramref name="handler"/> as an operator's command with no tenant, and answers what it answers.
    /// </summary>
    /// <param name="identity">The operator's identity.</param>
    /// <param name="handler">What the command's handler does.</param>
    /// <param name="scope">The scope of the work.</param>
    public static async Task<T> RunAsync<T>(Guid identity, Func<Task<T>> handler, string scope = TenancyWork.SystemScope)
    {
        T answer = default!;
        await RunAsync(identity, TenancyAccess.Operator(scope: scope), async () => answer = await handler());
        return answer;
    }

    /// <summary>The checks of a module with operators, asked as the behavior asks them, and the work they begin around <paramref name="handler"/>.</summary>
    private static async Task RunAsync(Guid identity, AccessRequirement requirement, Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var services = new ServiceCollection();
        services.AddSingleton(new TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId> { OperatorTokenRoles = { TokenRole } });
        services.AddSingleton<ICallerAccessor>(new AmbientCallerAccessor());
        services.AddTenancyOperatorAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IHostRequest>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IHostRequest>>();
        var command = new OperatorCommand(requirement);

        using (Callers.Begin(Caller.User(identity, TokenRole)))
        {
            await checks.RequireAsync(command);
            using (checks.BeginWork(command, isQuery: false))
            {
                await handler();
            }
        }
    }

    /// <summary>An operator's command of the module, which declares <paramref name="Requires"/>.</summary>
    private sealed record OperatorCommand(AccessRequirement Requires) : IHostRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }
}
