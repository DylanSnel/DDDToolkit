using DDDToolkit.Auth.Supabase;
using DDDToolkit.Identity;
using Examples.Tenancy.Tenants.Api;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Examples.Tenancy.Host.Auth;

/// <summary>
/// Which accounts an invited person signs in with: who answers <see cref="IIdentityAccounts"/>, the port the
/// Tenants module asks for an account at an invited address, and where the mail for a new account leads.
/// </summary>
public static class SampleIdentityAccounts
{
    /// <summary>
    /// The address of the page where a person accepts an invitation: the UI's <c>/invitations/accept</c>, as a
    /// browser reaches it, such as <c>http://localhost:5091/invitations/accept</c>. With it, the mail Auth sends a
    /// new account leads there with the invitation's token after the <c>#</c>; without it, the mail leads to
    /// Auth's own site URL and whoever invited hands the token over. Auth follows it only when its settings
    /// allow the address: the host of its site URL, or an entry of its list of redirect addresses.
    /// </summary>
    public const string AcceptPageSetting = "Sample:Invitations:AcceptPage";

    /// <summary>
    /// Registers the adapter the host's settings call for, or none, and tells the Tenants module the page of
    /// <see cref="AcceptPageSetting"/> when it is set.
    /// <list type="bullet">
    /// <item>With <c>Supabase:SecretKey</c>: Supabase Auth, through its admin client. An address without an
    /// account gets one, and Auth mails the person a link that lets them in; that is how anyone gets in where
    /// nobody signs up.</item>
    /// <item>Without it, with the dev login on: <see cref="DevIdentityAccounts"/>, which knows the demonstration
    /// people and mails nobody.</item>
    /// <item>With neither, nothing: the module then issues the invitation alone, for a project where a person
    /// makes their own account.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="environment">The host's environment, which the dev login guard asks.</param>
    /// <exception cref="InvalidOperationException"><see cref="AcceptPageSetting"/> is no absolute address.</exception>
    /// <exception cref="ArgumentException"><see cref="AcceptPageSetting"/> is not http or https, or has a fragment.</exception>
    public static IServiceCollection AddSampleIdentityAccounts(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (configuration[DemoAuthUsers.SecretKeySetting] is { Length: > 0 } secretKey)
        {
            // The key stays with the admin client: it is in no log and no exception.
            services.AddSupabaseAuthAdmin(SampleAuthentication.AuthUrlOf(configuration), secretKey);
        }
        else if (DevLoginGuard.Check(configuration, environment))
        {
            services.TryAddSingleton<IIdentityAccounts, DevIdentityAccounts>();
        }

        if (configuration[AcceptPageSetting] is { Length: > 0 } configured)
        {
            // Checked here, where the host starts, and not when the first person is invited.
            services.AddTenantsInvitationPage(Uri.TryCreate(configured, UriKind.Absolute, out var page)
                ? page
                : throw new InvalidOperationException(AcceptPageSetting + " has to be the absolute address of the page that accepts an invitation."));
        }

        return services;
    }
}
