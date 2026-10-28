using System.Reflection;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Tests.Access;

/// <summary>
/// The requirements that are about who is calling and nothing else: anyone, and a signed-in user. The core decides
/// them, so every module has them with no check of its own, and the same caller is let through or refused the same way
/// in each. The application's own work is none of them: it is a scope trusted code begins around the work, a request
/// it sends says who may send it like any other, and what only system work may do is refused past the door, with
/// <see cref="ICurrentUser.RequireSystemWork"/>.
/// </summary>
public class CurrentUserRequirementTests
{
    /// <summary>What every request of the test's module implements.</summary>
    public interface IShopRequest : IRequireAccess;

    /// <summary>A request that requires what it is given.</summary>
    public sealed record Checkout(AccessRequirement Requires) : IShopRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    /// <summary>A case of the module's own, which the core's check does not decide.</summary>
    public sealed record OnBasket(int Basket) : AccessRequirement;

    /// <summary>Decides everything, and says so: where it stands shows who was asked first.</summary>
    public sealed class EverythingCheck(List<string> asked) : IAccessCheck
    {
        public bool Decides(AccessRequirement requirement) => true;

        public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
        {
            asked.Add("everything");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>An accessor that answers the caller a test gives it.</summary>
    private sealed class Answering(CurrentUserSnapshot caller) : ICurrentUserSource
    {
        public CurrentUserSnapshot? Current => caller;
    }

    private static readonly CurrentUserSnapshot Signed = CurrentUserSnapshot.User(Guid.Parse("8d1c43a2-7d0e-4b8e-9a61-2a4f1f0c5b11"));

    /// <summary>Every kind of caller, with whether it is a signed-in user and whether it is the application itself.</summary>
    public static TheoryData<string, bool, bool> Kinds => new()
    {
        { "a signed-in user", true, false },
        { "a user with a role of its own", true, false },
        { "a caller who did not sign in", false, false },
        { "a token whose subject is no id", false, false },
        { "the application itself", false, true },
        { "the application in a scope", false, true },
    };

    private static CurrentUserSnapshot CallerOf(string kind) => kind switch
    {
        "a signed-in user" => Signed,
        "a user with a role of its own" => CurrentUserSnapshot.User(Guid.Parse("2b0e6c1d-9f3a-4c55-8e2d-7a1b9c0d4e21"), "operator"),
        "a caller who did not sign in" => CurrentUserSnapshot.Anonymous,
        "a token whose subject is no id" => CurrentUserSnapshot.User(null),
        "the application itself" => CurrentUserSnapshot.System,
        "the application in a scope" => CurrentUserSnapshot.SystemIn("billing"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // ------------------------------------------------------------------ the cases

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Anyone_passes_a_request_that_allows_anonymous_callers(string kind, bool signedIn, bool system)
    {
        _ = (signedIn, system);
        using var provider = Provider(services => services.AddSingleton<ICurrentUser>(new CurrentUser(source: new Answering(CallerOf(kind)))));
        using var scope = provider.CreateScope();

        await Checks(scope).RequireAsync(new Checkout(AccessRequirement.AllowAnonymous()), TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Only_a_signed_in_user_passes_a_request_that_requires_one(string kind, bool signedIn, bool system)
    {
        _ = system;
        using var provider = Provider(services => services.AddSingleton<ICurrentUser>(new CurrentUser(source: new Answering(CallerOf(kind)))));
        using var scope = provider.CreateScope();
        var require = () => Checks(scope).RequireAsync(new Checkout(AccessRequirement.SignedIn()), TestContext.Current.CancellationToken).AsTask();

        if (signedIn)
        {
            await require();
            return;
        }

        var refusal = (await require.Should().ThrowAsync<RefusalException>(kind)).Which;
        refusal.Code.Should().Be(ToolkitRefusals.NotSignedIn, "{0} is no signed-in user, and the application's own work is nobody's sign-in", kind);
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        refusal.Message.Should().Be("Only a signed-in user can do this.");
    }

    [Fact]
    public async Task System_work_is_a_scope_begun_around_the_send_and_the_caller_before_it_is_current_again_after()
    {
        // No request requires the application's own work. Trusted code begins it around the work, a request it sends
        // says who may send it, as any other does, and once the scope ends the caller before it is current again.
        using var provider = Provider(_ => { });
        using var scope = provider.CreateScope();
        var anyone = new Checkout(AccessRequirement.AllowAnonymous());
        var signedIn = new Checkout(AccessRequirement.SignedIn());

        using (TestUser.BeginAs(Signed))
        {
            using (TestUser.BeginSystem())
            {
                await Checks(scope).RequireAsync(anyone, TestContext.Current.CancellationToken);
                (await FluentActions.Awaiting(() => Checks(scope).RequireAsync(signedIn, TestContext.Current.CancellationToken).AsTask())
                    .Should().ThrowAsync<RefusalException>("the application's own work is nobody's sign-in")).Which.Code.Should().Be(ToolkitRefusals.NotSignedIn);
                BegunUser.Should().Be(CurrentUserSnapshot.System);
            }

            BegunUser.Should().Be(Signed, "the scope ended, and the caller before it is current again");
            await Checks(scope).RequireAsync(signedIn, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void There_is_one_way_to_spell_each_case()
    {
        // A request writes [SignedIn] or AccessRequirement.SignedIn(), never a constructor: nothing else makes one.
        foreach (var each in new[] { typeof(AccessRequirement.Anyone), typeof(AccessRequirement.SignedInUser), typeof(AccessRequirement.Owned<Guid>), typeof(AccessRequirement.OwnedAggregate<OwnedBoardId>), typeof(AccessRequirement.AllOf), typeof(AccessRequirement.AnyOf) })
        {
            each.IsSealed.Should().BeTrue();
            each.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Should().BeEmpty("{0} is made by its method alone", each.Name);
        }

        typeof(AccessRequirement.Combined).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => constructor.GetParameters().FirstOrDefault()?.ParameterType != typeof(AccessRequirement.Combined))
            .Should().OnlyContain(constructor => constructor.IsFamilyAndAssembly, "no case of a module's own is a combination the set of checks would take for one of its own; the record's copy constructor aside");
        AccessRequirement.AllowAnonymous().Should().BeOfType<AccessRequirement.Anyone>();
        AccessRequirement.SignedIn().Should().BeOfType<AccessRequirement.SignedInUser>();
        AccessRequirement.Own(Guid.NewGuid()).Should().BeOfType<AccessRequirement.Owned<Guid>>();
        AccessRequirement.OwnerOf(OwnedBoardId.Create()).Should().BeOfType<AccessRequirement.OwnedAggregate<OwnedBoardId>>();
        AccessRequirement.All(AccessRequirement.SignedIn()).Should().BeOfType<AccessRequirement.AllOf>();
        AccessRequirement.Any(AccessRequirement.SignedIn()).Should().BeOfType<AccessRequirement.AnyOf>();
        typeof(AccessRequirement).GetNestedTypes().Select(each => each.Name).Should().BeEquivalentTo(
            ["Anyone", "SignedInUser", "Owned`1", "OwnedAggregate`1", "Combined", "AllOf", "AnyOf"],
            "the core's cases are about who may send a request, the owner of what it names among them, or combine other cases; there is no case that says nothing, and system work is no case but a scope");
        typeof(AccessRequirement).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => !method.IsSpecialName).Select(method => method.Name)
            .Should().BeEquivalentTo(["AllowAnonymous", "SignedIn", "Own", "OwnerOf", "All", "Any"], "the record's operators aside, a spelling for each case and none for system work");
    }

    [Fact]
    public void Each_case_has_its_attribute_marked_with_the_call_the_generator_writes()
    {
        // What the generator reads: the attribute's class says which call it stands for, so nothing knows it by name.
        StatedBy(typeof(AllowAnonymousAttribute)).Should().Be((typeof(AccessRequirement), nameof(AccessRequirement.AllowAnonymous)));
        StatedBy(typeof(SignedInAttribute)).Should().Be((typeof(AccessRequirement), nameof(AccessRequirement.SignedIn)));
        typeof(AccessRequirement).GetMethod(nameof(AccessRequirement.AllowAnonymous))!.Invoke(null, null).Should().Be(AccessRequirement.AllowAnonymous());

        // A request carries each; the module's interface carries those that may be its default, and [AllowAnonymous]
        // and [OwnOnly] never are: a forgotten requirement never opens a door, and a default reads no member.
        foreach (var (attribute, validOn) in new[]
                 {
                     (typeof(AllowAnonymousAttribute), AttributeTargets.Class | AttributeTargets.Struct),
                     (typeof(OwnOnlyAttribute), AttributeTargets.Class | AttributeTargets.Struct),
                     (typeof(SignedInAttribute), AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface),
                     (typeof(RequiresPermissionAttribute), AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface),
                 })
        {
            var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>()!;
            usage.ValidOn.Should().Be(validOn, "{0} goes on a request, and on a module's interface only where it may be its default", attribute.Name);
            usage.Inherited.Should().BeFalse();
        }

        static (Type Factory, string Method) StatedBy(Type attribute)
            => attribute.GetCustomAttribute<StatesRequirementAttribute>() is { } states ? (states.Factory, states.Method) : default;
    }

    // ------------------------------------------------------------------ in front of the module's own checks

    [Fact]
    public async Task The_core_decides_its_cases_before_any_check_a_module_added()
    {
        // A module's check that says yes to everything is never asked about who is calling: the core's check is
        // first in every set, so a check of a module cannot take its cases over and let somebody through.
        var asked = new List<string>();
        using var provider = Provider(services => services
            .AddSingleton(asked)
            .AddSingleton<ICurrentUser>(new CurrentUser(source: new Answering(CurrentUserSnapshot.Anonymous)))
            .AddAccessCheck<IShopRequest, EverythingCheck>());
        using var scope = provider.CreateScope();

        await FluentActions.Awaiting(() => Checks(scope).RequireAsync(new Checkout(AccessRequirement.SignedIn()), TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<RefusalException>();
        asked.Should().BeEmpty();

        await Checks(scope).RequireAsync(new Checkout(new OnBasket(4)), TestContext.Current.CancellationToken);
        asked.Should().Equal(["everything"], "the module's own cases go to the module's checks, as before");
    }

    [Fact]
    public async Task Without_an_accessor_the_caller_is_the_one_begun_for_the_flow_of_work()
    {
        using var provider = Provider(_ => { });
        using var scope = provider.CreateScope();
        var signedIn = new Checkout(AccessRequirement.SignedIn());

        using (TestUser.BeginAs(Signed))
        {
            await Checks(scope).RequireAsync(signedIn, TestContext.Current.CancellationToken);
        }

        using (TestUser.BeginAs(CurrentUserSnapshot.Anonymous))
        {
            (await FluentActions.Awaiting(() => Checks(scope).RequireAsync(signedIn, TestContext.Current.CancellationToken).AsTask())
                .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(ToolkitRefusals.NotSignedIn);
        }

        // Outside any caller, a host that requires nothing answers the application itself, as in every 3.x host,
        // and that is nobody's sign-in.
        (await FluentActions.Awaiting(() => Checks(scope).RequireAsync(signedIn, TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(ToolkitRefusals.NotSignedIn);
    }

    [Fact]
    public async Task A_host_that_requires_explicit_callers_fails_work_nobody_began_a_caller_for()
    {
        using var provider = Provider(services => services.RequireExplicitCurrentUser());
        using var scope = provider.CreateScope();

        await FluentActions.Awaiting(() => Checks(scope).RequireAsync(new Checkout(AccessRequirement.SignedIn()), TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<NoCurrentUserException>("such work is a mistake to fix, not a caller to refuse or to let through");

        using (TestUser.BeginAs(Signed))
        {
            await Checks(scope).RequireAsync(new Checkout(AccessRequirement.SignedIn()), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_set_made_by_hand_without_the_core_s_check_names_the_registration_that_adds_it()
    {
        var checks = new AccessChecks<IShopRequest>([]);

        var failure = await FluentActions.Awaiting(() => checks.RequireAsync(new Checkout(AccessRequirement.SignedIn()), TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Message.Should().Be(
            "CurrentUserRequirementTests.Checkout declares 'AccessRequirement.SignedInUser', which none of the access checks registered for CurrentUserRequirementTests.IShopRequest decides. "
            + "A requirement nothing checks lets nobody through: register the check that decides it with "
            + "services.AddAccessChecks<CurrentUserRequirementTests.IShopRequest>(), which puts the core's CurrentUserAccessCheck first in the module's set.");

        // Given the core's check, the same set decides it.
        await new AccessChecks<IShopRequest>([new CurrentUserAccessCheck(new CurrentUser(source: new Answering(Signed)))])
            .RequireAsync(new Checkout(AccessRequirement.SignedIn()), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_core_s_check_decides_its_own_cases_and_no_other()
    {
        var check = new CurrentUserAccessCheck(new CurrentUser(source: new Answering(Signed)));

        check.Decides(AccessRequirement.SignedIn()).Should().BeTrue();
        check.Decides(AccessRequirement.Own(Guid.NewGuid())).Should().BeTrue("the caller's verified identity is the core's to compare");
        check.Decides(AccessRequirement.Own(4)).Should().BeFalse("nothing says what the caller's own number is");
        check.Decides(AccessRequirement.AllowAnonymous()).Should().BeFalse("nobody has to decide that anyone may send a request");
        check.Decides(AccessRequirement.All(AccessRequirement.SignedIn())).Should().BeFalse("the set of checks decides what combines cases, each case by its own check");
        check.Decides(new OnBasket(4)).Should().BeFalse();

        await FluentActions.Awaiting(() => check.RequireAsync(new OnBasket(4), new Checkout(new OnBasket(4)), TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<InvalidOperationException>("a case it was never asked to decide lets nobody through");
        await FluentActions.Awaiting(() => check.RequireAsync(null!, new Checkout(new OnBasket(4)), TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => check.RequireAsync(AccessRequirement.SignedIn(), null!, TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<ArgumentNullException>();
    }

    // ------------------------------------------------------------------ past the door: what only system work may do

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Only_system_work_trusted_code_began_passes_what_only_the_application_itself_may_do(string kind, bool signedIn, bool system)
    {
        _ = signedIn;
        var user = new CurrentUser();

        // The user is begun for the flow of work, as trusted code begins system work, and the current user answers it.
        using var begun = TestUser.BeginAs(CallerOf(kind));
        var require = () => user.RequireSystemWork();

        if (system)
        {
            require().Should().BeSameAs(BegunUser, "it answers who the work runs as");
            return;
        }

        var refusal = require.Should().Throw<RefusalException>(kind).Which;
        refusal.Code.Should().Be(ToolkitRefusals.SystemOnly, "{0} is not the application, whatever it holds", kind);
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        refusal.Message.Should().Be("Only the application itself can do this.");
    }

    [Fact]
    public void Work_nobody_began_is_no_system_work_even_where_the_host_answers_the_application_itself()
    {
        // A host that requires nothing answers system work for work that began no user: a web request no source knows
        // gets that answer too, so asking the current user alone would let it through.
        var user = new CurrentUser();
        user.Snapshot.Should().Be(CurrentUserSnapshot.System, "that is the 3.x default, which this test is about");
        RefusedAsSystemOnly(() => user.RequireSystemWork(), "nothing began a user for the flow");

        using (user.BeginSystem())
        {
            user.RequireSystemWork().Should().BeSameAs(CurrentUserSnapshot.System);

            using (user.BeginNone())
            {
                RefusedAsSystemOnly(() => user.RequireSystemWork(), "the toolkit hid the system work from the handler it runs");
            }
        }

        using (user.BeginSystem("billing"))
        {
            user.RequireSystemWork().SystemScope.Should().Be("billing", "it answers the scope the work runs in");
        }

        // A host's own source that answers system work where nothing was begun is not believed either.
        RefusedAsSystemOnly(() => new CurrentUser(source: new Answering(CurrentUserSnapshot.System)).RequireSystemWork(), "the host's answer is a default, not work anybody began");

        // System work begun beside a host that answers somebody else is system work: a begun user wins over the host's answer.
        using (user.BeginSystem())
        {
            new CurrentUser(source: new Answering(Signed)).RequireSystemWork().Should().BeSameAs(CurrentUserSnapshot.System, "a begun user wins over the host's answer");
        }

        // A host that requires an explicit current user fails such work instead of refusing it.
        FluentActions.Invoking(() => new CurrentUser(new CurrentUserOptions { RequireExplicitCurrentUser = true }).RequireSystemWork())
            .Should().Throw<NoCurrentUserException>("such work is a mistake to fix, not a caller to refuse");
    }

    private static void RefusedAsSystemOnly(Func<CurrentUserSnapshot> require, string because)
        => require.Should().Throw<RefusalException>(because).Which.Code.Should().Be(ToolkitRefusals.SystemOnly, because);

    private static AccessChecks<IShopRequest> Checks(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AccessChecks<IShopRequest>>();

    private static ServiceProvider Provider(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        services.AddAccessChecks<IShopRequest>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
