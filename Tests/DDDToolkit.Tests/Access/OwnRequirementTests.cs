using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Tests.Access;

/// <summary>
/// A request about the caller's own: <see cref="AccessRequirement.Own{TValue}"/> names a value, and the core's check
/// compares it at the door with the caller's own value of that type, which whoever knows says with an
/// <see cref="ICallersOwn{TValue}"/>: the core for a <see cref="Guid"/>, the caller's verified identity, and the
/// application, with a small handle it registers, for a kind of its own.
/// </summary>
public class OwnRequirementTests
{
    /// <summary>What every request of the test's module implements.</summary>
    public interface IProfileRequest : IRequireAccess;

    /// <summary>A request that requires what it is given.</summary>
    public sealed record EditProfile(AccessRequirement Requires) : IProfileRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    /// <summary>An application's own user id, over the id its users' tokens name.</summary>
    public readonly record struct UserId(Guid Value);

    /// <summary>The application's handle: the caller's own user id is the one over the id its token names.</summary>
    public sealed class CallersUser : ICallersOwn<UserId>
    {
        public UserId? Find(Caller caller) => caller.UserId is { } id ? new UserId(id) : null;
    }

    /// <summary>A handle that knows no caller: what a host registers to say that nobody's own a Guid is.</summary>
    public sealed class NobodysGuid : ICallersOwn<Guid>
    {
        public Guid? Find(Caller caller) => null;
    }

    /// <summary>A case of the module's own: a key, held by the callers a test says.</summary>
    public sealed record HoldsKey(string Key) : AccessRequirement;

    /// <summary>Decides the module's key, held by nobody, and says it was asked.</summary>
    public sealed class KeyCheck(List<string> asked) : IAccessCheck
    {
        public bool Decides(AccessRequirement requirement) => requirement is HoldsKey;

        public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
        {
            asked.Add(((HoldsKey)requirement).Key);
            return ValueTask.FromException(new RefusalException("profiles.not-permitted", RefusalKind.NotPermitted, "That takes the key."));
        }
    }

    /// <summary>An accessor that answers the caller a test gives it.</summary>
    private sealed class Answering(Caller caller) : ICallerAccessor
    {
        public Caller Current => caller;
    }

    private static readonly Guid Ann = Guid.Parse("3f9a1e2c-6b7d-4c8e-9a10-5d2e7f4b8c61");

    private static readonly Guid Bob = Guid.Parse("7c2d9e4f-1a3b-4d5e-8f60-2b1a9c8d7e53");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_guid_is_the_caller_s_own_where_it_is_the_id_its_token_names()
    {
        using var provider = Provider(Caller.User(Ann));
        using var scope = provider.CreateScope();

        await Checks(scope).RequireAsync(new EditProfile(AccessRequirement.Own(Ann)), Cancellation);

        var refusal = await RefusedAsync(scope, AccessRequirement.Own(Bob));
        refusal.Code.Should().Be(ToolkitRefusals.NotOwn, "Bob's id is not the one Ann's token names");
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        refusal.Message.Should().Be("Only whoever this is about can do this.");
        refusal.Arguments.Should().BeEmpty("the value is the request's, and the caller sent it");
    }

    [Theory]
    [InlineData("a caller who did not sign in")]
    [InlineData("the application itself")]
    [InlineData("the application in a scope")]
    [InlineData("a token whose subject is no id")]
    public async Task Nobody_but_a_signed_in_user_has_an_identity_of_its_own(string kind)
    {
        var caller = kind switch
        {
            "a caller who did not sign in" => Caller.Anonymous,
            "the application itself" => Caller.System,
            "the application in a scope" => Caller.SystemIn("profiles"),
            _ => Caller.User(null),
        };
        using var provider = Provider(caller);
        using var scope = provider.CreateScope();

        (await RefusedAsync(scope, AccessRequirement.Own(Ann))).Code.Should().Be(ToolkitRefusals.NotOwn, "{0} has no identity a request could name", kind);
    }

    [Fact]
    public async Task A_kind_of_the_application_s_own_is_the_caller_s_own_where_its_handle_says_so()
    {
        using var provider = Provider(Caller.User(Ann), services => services.AddCallersOwn<UserId, CallersUser>());
        using var scope = provider.CreateScope();

        Checks(scope).Decides(AccessRequirement.Own(new UserId(Ann))).Should().BeTrue();
        await Checks(scope).RequireAsync(new EditProfile(AccessRequirement.Own(new UserId(Ann))), Cancellation);
        (await RefusedAsync(scope, AccessRequirement.Own(new UserId(Bob)))).Code.Should().Be(ToolkitRefusals.NotOwn);
    }

    [Fact]
    public async Task A_kind_nothing_says_the_caller_s_own_of_is_stopped_and_the_registration_named()
    {
        using var provider = Provider(Caller.User(Ann));
        using var scope = provider.CreateScope();

        Checks(scope).Decides(AccessRequirement.Own(new UserId(Ann))).Should().BeFalse("a requirement nothing checks lets nobody through, the caller's own included");
        (await FluentActions.Awaiting(() => Checks(scope).RequireAsync(new EditProfile(AccessRequirement.Own(new UserId(Ann))), Cancellation).AsTask())
                .Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*declares 'AccessRequirement.Owned<OwnRequirementTests.UserId>'*services.AddCallersOwn<TValue, TCallersOwn>()*");
    }

    [Fact]
    public async Task A_host_s_handle_takes_the_place_of_the_core_s_and_of_one_registered_before()
    {
        using var provider = Provider(Caller.User(Ann), services => services.AddCallersOwn<Guid, NobodysGuid>());
        using var scope = provider.CreateScope();

        (await RefusedAsync(scope, AccessRequirement.Own(Ann))).Code.Should().Be(ToolkitRefusals.NotOwn, "the host said nobody's own a Guid is");

        var services = new ServiceCollection();
        services.AddCallersOwn<UserId, CallersUser>().AddCallersOwn<UserId, CallersUser>();
        services.Where(descriptor => descriptor.ServiceType == typeof(ICallersOwn<UserId>)).Should().ContainSingle("one handle per type, the later in the place of the earlier")
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task Yours_or_the_key_asks_the_key_only_where_the_value_is_not_the_caller_s()
    {
        var asked = new List<string>();
        using var provider = Provider(Caller.User(Ann), services => services.AddSingleton(asked).AddAccessCheck<IProfileRequest, KeyCheck>());
        using var scope = provider.CreateScope();
        AccessRequirement YoursOrTheKey(Guid profile) => AccessRequirement.Any(AccessRequirement.Own(profile), new HoldsKey("profiles.edit"));

        await Checks(scope).RequireAsync(new EditProfile(YoursOrTheKey(Ann)), Cancellation);
        asked.Should().BeEmpty("her own profile is hers, and the key is not asked");

        var refusal = await RefusedAsync(scope, YoursOrTheKey(Bob));
        asked.Should().Equal("profiles.edit");
        refusal.Code.Should().Be(ToolkitRefusals.Refused);
        ((IReadOnlyList<RefusalReason>)refusal.Arguments[ToolkitRefusals.ReasonsArgument]!).Select(reason => reason.Code)
            .Should().Equal(ToolkitRefusals.NotOwn, "profiles.not-permitted");
    }

    [Fact]
    public void Two_requirements_about_the_same_value_are_equal()
    {
        AccessRequirement.Own(Ann).Should().Be(AccessRequirement.Own(Ann));
        AccessRequirement.Own(Ann).Should().NotBe(AccessRequirement.Own(Bob));
        AccessRequirement.Own(new UserId(Ann)).Should().NotBe(AccessRequirement.Own(Ann), "a user id of the application's is not the identity itself, though it is over it");
        AccessRequirement.Own(Ann).Value.Should().Be(Ann);
    }

    private static async Task<RefusalException> RefusedAsync(IServiceScope scope, AccessRequirement requirement)
        => (await FluentActions.Awaiting(() => Checks(scope).RequireAsync(new EditProfile(requirement), Cancellation).AsTask()).Should().ThrowAsync<RefusalException>()).Which;

    private static AccessChecks<IProfileRequest> Checks(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AccessChecks<IProfileRequest>>();

    private static ServiceProvider Provider(Caller caller, Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICallerAccessor>(new Answering(caller));
        register?.Invoke(services);
        services.AddAccessChecks<IProfileRequest>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
