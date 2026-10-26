using DDDToolkit.Auth.Supabase.AspNetCore;
using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using DDDToolkit.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// <c>AddSupabaseAuthAdmin</c>: what a host gets from the container, what fails at start-up instead of on
/// the first invitation, how a host on another identity provider keeps its own port, and how one
/// description of the project serves it and the other Supabase registrations alike.
/// </summary>
public sealed class SupabaseAuthAdminRegistrationTests
{
    private const string Address = "ada.lindqvist@example.test";

    private static readonly Guid Ada = Guid.Parse("8051a7e8-8599-4ad8-b169-68dc96beb0c5");

    private readonly StubAuthServer _auth = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_admin_client_and_the_port_are_registered_over_one_client()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(Ada, Address, invitedAt: "2026-03-02T09:15:54.013489931Z"));
        await using var provider = new ServiceCollection()
            .AddSupabaseAuthAdmin(StubAuthServer.ProjectUrl, StubAuthServer.SecretKey, _auth)
            .BuildServiceProvider(validateScopes: true);

        var admin = provider.GetRequiredService<SupabaseAuthAdmin>();
        var accounts = provider.GetRequiredService<IIdentityAccounts>();

        accounts.Should().BeOfType<SupabaseIdentityAccounts>();
        provider.GetRequiredService<SupabaseAuthAdmin>().Should().BeSameAs(admin, "one client, and so one pool of connections, for the whole host");
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IIdentityAccounts>().Should().BeSameAs(accounts, "a request's handler takes it like any other service");
        }

        var outcome = await accounts.InviteByEmailAsync(Address, signInRedirect: null, Cancellation);

        outcome.Should().Be(new IdentityAccountOutcome.Created(Ada));
        _auth.Requests.Select(request => request.Uri).Should().Equal(new Uri(StubAuthServer.Url + "/admin/users"), new Uri(StubAuthServer.Url + "/invite"));
        _auth.Requests.Should().AllSatisfy(request =>
        {
            request.Headers["apikey"].Should().Be(StubAuthServer.SecretKey);
            request.Headers["Authorization"].Should().Be("Bearer " + StubAuthServer.SecretKey);
        });
    }

    [Fact]
    public async Task One_description_of_the_project_serves_the_bearer_the_validator_and_the_admin_client()
    {
        // A project whose Auth is reached without its gateway, on a network of the host's own: said once.
        var project = new SupabaseAuthOptions { ProjectUrl = "http://127.0.0.1:54321", AuthUrl = "http://auth.example.test:9999", AllowPlainHttp = true };
        var services = new ServiceCollection();
        services.AddAuthentication().AddSupabaseJwtBearer(project);
        services.AddSupabaseAuth(project);
        services.AddSupabaseAuthAdmin(project, StubAuthServer.SecretKey, _auth);

        // Each read it when it was registered: what the host does to it afterwards reaches none of them.
        project.ProjectUrl = "https://another-project.example.test";
        project.AuthUrl = "https://somewhere-else.example.test";

        await using var provider = services.BuildServiceProvider();
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        await provider.GetRequiredService<SupabaseAuthAdmin>().FindUserAsync(Ada, Cancellation);

        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        bearer.TokenHandlers.OfType<SupabaseTokenHandler>().Should().ContainSingle()
            .Which.KeysAddress.Should().Be("http://auth.example.test:9999/.well-known/jwks.json");
        _auth.Request.Uri.Should().Be(new Uri("http://auth.example.test:9999/admin/users/" + Ada), "the admin client reaches Auth where the bearer fetches its keys");
        provider.GetRequiredService<SupabaseTokenValidator>().Issuer.Should().Be("http://127.0.0.1:54321/auth/v1", "the issuer stays the project's");
        provider.GetRequiredService<SupabaseAuthOptions>().Should().NotBeSameAs(project, "the validator's registration keeps a copy")
            .And.BeEquivalentTo(new { ProjectUrl = "http://127.0.0.1:54321", AuthUrl = "http://auth.example.test:9999", AllowPlainHttp = true });
    }

    [Theory]
    [InlineData("", StubAuthServer.SecretKey, "projectUrl")]
    [InlineData("project.example.test", StubAuthServer.SecretKey, "projectUrl")]
    [InlineData(StubAuthServer.ProjectUrl, "", "secretKey")]
    [InlineData(StubAuthServer.ProjectUrl, "sb_publishable_kq7zzv", "secretKey")]
    [InlineData(StubAuthServer.ProjectUrl, "kq7zzv with spaces", "secretKey")]
    public void A_url_or_a_key_that_cannot_work_fails_when_the_host_starts(string projectUrl, string secretKey, string parameter)
    {
        var services = new ServiceCollection();

        var registering = () => services.AddSupabaseAuthAdmin(projectUrl, secretKey);

        registering.Should().Throw<ArgumentException>("the first invitation is too late to find out").WithParameterName(parameter)
            .Which.ToString().Should().NotContain("kq7zzv");
        services.Should().BeEmpty("a registration that failed leaves nothing half registered");
    }

    [Fact]
    public async Task The_container_disposes_the_client_it_made()
    {
        var provider = new ServiceCollection()
            .AddSupabaseAuthAdmin(StubAuthServer.ProjectUrl, StubAuthServer.SecretKey, _auth)
            .BuildServiceProvider();
        var admin = provider.GetRequiredService<SupabaseAuthAdmin>();

        await provider.DisposeAsync();

        await admin.Invoking(a => a.FindUserAsync(Ada, Cancellation)).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task A_host_on_another_identity_provider_keeps_its_own_port()
    {
        // Registered after: the host's own wins, and the Supabase client is still there for what only
        // Supabase has, such as seeding demo users.
        await using var provider = new ServiceCollection()
            .AddSupabaseAuthAdmin(StubAuthServer.ProjectUrl, StubAuthServer.SecretKey, _auth)
            .AddSingleton<IIdentityAccounts, AccountsOfAnotherProvider>()
            .BuildServiceProvider();

        provider.GetRequiredService<IIdentityAccounts>().Should().BeOfType<AccountsOfAnotherProvider>();
        provider.GetRequiredService<SupabaseAuthAdmin>().Should().NotBeNull();
    }

    [Fact]
    public async Task Registering_again_replaces_what_was_registered()
    {
        var services = new ServiceCollection()
            .AddSingleton<IIdentityAccounts, AccountsOfAnotherProvider>()
            .AddSupabaseAuthAdmin("http://127.0.0.1:54321", "a-first-key", _auth)
            .AddSupabaseAuthAdmin(StubAuthServer.ProjectUrl, StubAuthServer.SecretKey, _auth);
        await using var provider = services.BuildServiceProvider();
        _auth.Answers(200, "{}");

        services.Count(service => service.ServiceType == typeof(SupabaseAuthAdmin)).Should().Be(1);
        services.Count(service => service.ServiceType == typeof(IIdentityAccounts)).Should().Be(1);
        provider.GetServices<IIdentityAccounts>().Should().ContainSingle().Which.Should().BeOfType<SupabaseIdentityAccounts>();

        await provider.GetRequiredService<IIdentityAccounts>().DeleteAsync(Ada, Cancellation);

        _auth.Request.Uri.Host.Should().Be("project.example.test", "the last registration is the one in force");
        _auth.Request.Headers["apikey"].Should().Be(StubAuthServer.SecretKey);
    }

    /// <summary>
    /// The port as a host on another provider would write it, here remembering accounts in memory the way
    /// a development host does.
    /// </summary>
    private sealed class AccountsOfAnotherProvider : IIdentityAccounts
    {
        private readonly Dictionary<string, Guid> _byAddress = new(StringComparer.OrdinalIgnoreCase);

        public Task<IdentityAccountOutcome> CreateAsync(Guid identity, string address, CancellationToken cancellationToken)
            => Task.FromResult<IdentityAccountOutcome>(_byAddress.TryAdd(address, identity)
                ? new IdentityAccountOutcome.Created(identity)
                : new IdentityAccountOutcome.AddressTaken());

        public Task<IdentityAccountOutcome> InviteByEmailAsync(string address, Uri? signInRedirect, CancellationToken cancellationToken)
            => CreateAsync(Guid.NewGuid(), address, cancellationToken);

        public Task<IdentityInvitation> InviteAccountAsync(Guid identity, Uri? signInRedirect, CancellationToken cancellationToken)
            => Task.FromResult(_byAddress.ContainsValue(identity) ? IdentityInvitation.Sent : IdentityInvitation.NoSuchAccount);

        public Task<IdentityAccount?> FindAsync(Guid identity, CancellationToken cancellationToken)
            => Task.FromResult(_byAddress.ContainsValue(identity) ? new IdentityAccount(identity, HasSignedIn: false) : null);

        public Task<bool> DeleteAsync(Guid identity, CancellationToken cancellationToken)
            => Task.FromResult(_byAddress.Where(pair => pair.Value == identity).Select(pair => pair.Key).ToList() is { Count: > 0 } addresses
                && addresses.TrueForAll(_byAddress.Remove));
    }
}
