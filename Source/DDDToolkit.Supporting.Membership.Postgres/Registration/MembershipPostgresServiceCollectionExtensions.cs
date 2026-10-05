using DDDToolkit.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.Postgres;

/// <summary>Registers what Membership needs on Postgres with row level security.</summary>
public static class MembershipPostgresServiceCollectionExtensions
{
    /// <summary>
    /// Registers the package's start-up check, <see cref="MembershipPostgresChecks.FunctionsInPlaceCheck"/>, which a
    /// host runs with <c>services.RunStartupChecks()</c>: for every kind of resource with members the application
    /// registered, the database has the functions and the lock its rules write, as
    /// <see cref="MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync"/> says. Call it where the resources are
    /// registered, next to the registration generated for each:
    /// <code>
    /// services.AddDocumentMembership&lt;FilingContext&gt;(DocumentMembership.Rules);
    /// services.AddMembershipPostgres();
    /// </code>
    /// What the database holds comes with the access files the export writes, from a class of the application's
    /// derived from <see cref="MembershipRowAccessContribution{TMember}"/>, and nothing at run time depends on this
    /// call but the check. That is also why nothing else brings the check: a host that leaves this call out runs
    /// every other start-up check and not this one, without a word. Calling it more than once is harmless.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddMembershipPostgres(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // It reads the catalogs of the database and logs what it remarks on itself, so what it returns is not kept.
        return services.AddStartupCheck(new StartupCheck(
            MembershipPostgresChecks.FunctionsInPlaceCheck,
            StartupCheckStage.Database,
            static async (provider, cancellationToken) => await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(provider, cancellationToken).ConfigureAwait(false)));
    }
}
