using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Tests.Access;

/// <summary>
/// A request says what it requires of its caller, and the checks of its module hold it to that before its
/// handler runs: one set of checks per request interface, asked in the order they were added, and closed to
/// every request that declares nothing or something no check decides.
/// </summary>
public class AccessChecksTests
{
    // ------------------------------------------------------------------ a module's vocabulary

    /// <summary>What every request of the billing module implements.</summary>
    public interface IBillingRequest : IRequireAccess;

    /// <summary>What every request of the shipping module implements.</summary>
    public interface IShippingRequest : IRequireAccess;

    /// <summary>The cases the billing module declares.</summary>
    public abstract record BillingAccess : AccessRequirement
    {
        private BillingAccess()
        {
        }

        public sealed record OnInvoice(string Key, int Invoice) : BillingAccess;

        public sealed record InTenant : BillingAccess;
    }

    /// <summary>A case of a package, closed over the application's id.</summary>
    public abstract record LedgerAccess<TId> : AccessRequirement
    {
        private LedgerAccess()
        {
        }

        public sealed record On(TId Ledger) : LedgerAccess<TId>;
    }

    /// <summary>The cases a package ships, which say the registration that adds the package's check for them.</summary>
    [AccessCheckRegistration("services.AddParcelAccess<{TRequests}>()")]
    public abstract record ParcelAccess : AccessRequirement
    {
        private ParcelAccess()
        {
        }

        public sealed record Weighed(int Parcel) : ParcelAccess;
    }

    /// <summary>A case that is itself closed over the application's id, in a requirement that is not.</summary>
    public abstract record ShippingAccess : AccessRequirement
    {
        private ShippingAccess()
        {
        }

        public sealed record From<TDepot>(TDepot Depot) : ShippingAccess;
    }

    public sealed record CloseInvoice(int Invoice, AccessRequirement Requires) : IBillingRequest, IShippingRequest
    {
        public int Asked { get; private set; }

        AccessRequirement IRequireAccess.RequiredAccess
        {
            get
            {
                Asked++;
                return Requires;
            }
        }
    }

    /// <summary>What the checks of a test were asked, in order.</summary>
    public sealed class Asked
    {
        public List<string> Entries { get; } = [];
    }

    /// <summary>Decides the billing cases: lets a caller through for the keys it holds, and keeps the invoice for the handler.</summary>
    public sealed class BillingAccessCheck(Asked asked, Checked<int> checkedInvoice) : IAccessCheck
    {
        public bool Decides(AccessRequirement requirement) => requirement is BillingAccess;

        public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
        {
            asked.Entries.Add("billing");
            switch (requirement)
            {
                case BillingAccess.OnInvoice { Key: "billing.close" } required:
                    checkedInvoice.KeepFor(request, required.Invoice);
                    return ValueTask.CompletedTask;

                case BillingAccess.OnInvoice required:
                    throw new RefusalException("billing.not-permitted", RefusalKind.NotPermitted, $"The key '{required.Key}' is not held.");

                default:
                    return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>Decides everything, and says it was asked: where it stands in the order shows who was asked first.</summary>
    public sealed class EverythingCheck(Asked asked) : IAccessCheck
    {
        public bool Decides(AccessRequirement requirement) => true;

        public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
        {
            asked.Entries.Add("everything");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A check written with <c>Decides</c> and <c>RequireAsync</c> out of step: it decides nothing, so it is never asked to require.</summary>
    public sealed class NothingCheck(Asked asked) : IAccessCheck
    {
        public bool Decides(AccessRequirement requirement) => false;

        public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
        {
            asked.Entries.Add("nothing");
            return ValueTask.CompletedTask;
        }
    }

    private static readonly BillingAccess.OnInvoice MayClose = new("billing.close", 7);

    private static ServiceProvider Provider(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection().AddScoped<Asked>();
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    // ------------------------------------------------------------------ the requirement

    [Fact]
    public void Two_requirements_that_say_the_same_are_equal()
    {
        new BillingAccess.OnInvoice("billing.close", 7).Should().Be(MayClose).And.NotBe(new BillingAccess.OnInvoice("billing.close", 8));
        AccessRequirement.AllowAnonymous().Should().Be(AccessRequirement.AllowAnonymous());
        AccessRequirement.SignedIn().Should().Be(AccessRequirement.SignedIn());
        AccessRequirement.RequiresSystemWork().Should().Be(AccessRequirement.RequiresSystemWork());
        ((AccessRequirement)AccessRequirement.AllowAnonymous()).Should().NotBe(AccessRequirement.SignedIn(), "two cases are never each other")
            .And.NotBe(AccessRequirement.RequiresSystemWork());
        ((AccessRequirement)AccessRequirement.SignedIn()).Should().NotBe(AccessRequirement.RequiresSystemWork());
    }

    // ------------------------------------------------------------------ holding a request to it

    [Fact]
    public async Task A_request_anyone_may_send_passes_without_a_check_being_asked()
    {
        var asked = new Asked();
        var anyone = new CloseInvoice(7, AccessRequirement.AllowAnonymous());

        await new AccessChecks<IBillingRequest>([]).RequireAsync(anyone, TestContext.Current.CancellationToken);
        await new AccessChecks<IBillingRequest>([new EverythingCheck(asked)]).RequireAsync(anyone, TestContext.Current.CancellationToken);

        asked.Entries.Should().BeEmpty("nobody has to decide that anyone may send it");
    }

    [Fact]
    public async Task The_check_that_decides_a_requirement_holds_the_caller_to_it()
    {
        var asked = new Asked();
        var kept = new Checked<int>();
        var checks = new AccessChecks<IBillingRequest>([new BillingAccessCheck(asked, kept)]);
        var allowed = new CloseInvoice(7, MayClose);
        var refused = new CloseInvoice(7, new BillingAccess.OnInvoice("billing.reopen", 7));

        await checks.RequireAsync(allowed, TestContext.Current.CancellationToken);
        var refusal = await FluentActions.Awaiting(() => checks.RequireAsync(refused, TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<RefusalException>();

        refusal.Which.Code.Should().Be("billing.not-permitted", "a check refuses with the code of whoever the request belongs to");
        asked.Entries.Should().Equal("billing", "billing");
        kept.TakeFor(allowed).Should().Be(7, "the check was handed the very request, and kept what it read under it");
        FluentActions.Invoking(() => kept.TakeFor(refused)).Should().Throw<InvalidOperationException>("nothing is kept for a caller that was refused");
    }

    [Fact]
    public async Task The_requirement_is_read_from_the_request_once()
    {
        var request = new CloseInvoice(7, MayClose);

        await new AccessChecks<IBillingRequest>([new BillingAccessCheck(new Asked(), new Checked<int>())]).RequireAsync(request, TestContext.Current.CancellationToken);

        request.Asked.Should().Be(1, "what is decided and what is required are the same requirement, even when a request makes it anew each time");
    }

    [Fact]
    public async Task The_first_check_that_decides_a_requirement_is_the_only_one_asked()
    {
        var asked = new Asked();
        var billing = new BillingAccessCheck(asked, new Checked<int>());

        await new AccessChecks<IBillingRequest>([new NothingCheck(asked), billing, new EverythingCheck(asked)])
            .RequireAsync(new CloseInvoice(7, MayClose), TestContext.Current.CancellationToken);
        asked.Entries.Should().Equal("billing");

        asked.Entries.Clear();
        await new AccessChecks<IBillingRequest>([new EverythingCheck(asked), billing])
            .RequireAsync(new CloseInvoice(7, MayClose), TestContext.Current.CancellationToken);
        asked.Entries.Should().Equal(["everything"], "the checks are asked in the order they were given");
    }

    [Fact]
    public async Task A_requirement_no_check_decides_lets_nobody_through()
    {
        var asked = new Asked();
        var checks = new AccessChecks<IBillingRequest>([new NothingCheck(asked), new BillingAccessCheck(asked, new Checked<int>())]);
        var request = new CloseInvoice(7, new LedgerAccess<Guid>.On(Guid.Empty));

        var failure = await FluentActions.Awaiting(() => checks.RequireAsync(request, TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Message.Should()
            .StartWith("AccessChecksTests.CloseInvoice declares 'AccessChecksTests.LedgerAccess<Guid>.On'", "the message names the request and the case as their author wrote them")
            .And.Contain("registered for AccessChecksTests.IBillingRequest")
            .And.Contain("AddAccessCheck<AccessChecksTests.IBillingRequest, TCheck>()");
        asked.Entries.Should().BeEmpty("a check that does not decide a requirement is not asked to require it");

        await FluentActions.Awaiting(() => new AccessChecks<IBillingRequest>([]).RequireAsync(new CloseInvoice(7, MayClose), TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<InvalidOperationException>("a set without checks lets through only a request anyone may send");
    }

    [Fact]
    public async Task A_case_of_a_package_that_says_its_registration_is_stopped_with_that_registration_named()
    {
        var checks = new AccessChecks<IBillingRequest>([new BillingAccessCheck(new Asked(), new Checked<int>())]);

        var failure = await FluentActions.Awaiting(() => checks.RequireAsync(new CloseInvoice(7, new ParcelAccess.Weighed(3)), TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Message.Should().Be(
            "AccessChecksTests.CloseInvoice declares 'AccessChecksTests.ParcelAccess.Weighed', which none of the access checks registered for AccessChecksTests.IBillingRequest decides. "
            + "A requirement nothing checks lets nobody through: register the check that decides it with services.AddParcelAccess<AccessChecksTests.IBillingRequest>().",
            "the case says the package's registration, inherited from the requirement it derives from, for this module's interface");
    }

    [Fact]
    public async Task A_case_is_named_with_each_type_argument_beside_the_type_that_declares_it()
    {
        var checks = new AccessChecks<IBillingRequest>([]);

        (await MessageFor(new LedgerAccess<Guid>.On(Guid.Empty))).Should().Contain("declares 'AccessChecksTests.LedgerAccess<Guid>.On',");
        (await MessageFor(new ShippingAccess.From<int>(4))).Should().Contain("declares 'AccessChecksTests.ShippingAccess.From<Int32>',");
        (await MessageFor(new ShippingAccess.From<LedgerAccess<Guid>.On>(new(Guid.Empty)))).Should()
            .Contain("declares 'AccessChecksTests.ShippingAccess.From<AccessChecksTests.LedgerAccess<Guid>.On>',");
        (await MessageFor(new BillingAccess.InTenant())).Should().Contain("declares 'AccessChecksTests.BillingAccess.InTenant',");

        async Task<string> MessageFor(AccessRequirement requirement)
            => (await FluentActions.Awaiting(() => checks.RequireAsync(new CloseInvoice(7, requirement), TestContext.Current.CancellationToken).AsTask())
                .Should().ThrowAsync<InvalidOperationException>()).Which.Message;
    }

    [Fact]
    public async Task A_request_that_declares_nothing_lets_nobody_through()
    {
        var asked = new Asked();
        var checks = new AccessChecks<IBillingRequest>([new EverythingCheck(asked)]);

        var failure = await FluentActions.Awaiting(() => checks.RequireAsync(new CloseInvoice(7, null!), TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Message.Should().Contain("CloseInvoice declares no access requirement").And.Contain("AccessRequirement.AllowAnonymous()");
        asked.Entries.Should().BeEmpty("not even a check that decides everything is asked about nothing");
    }

    [Fact]
    public void Whether_a_requirement_can_pass_is_answered_without_asking_a_check_to_require_it()
    {
        var asked = new Asked();
        var checks = new AccessChecks<IBillingRequest>([new NothingCheck(asked), new BillingAccessCheck(asked, new Checked<int>())]);

        checks.Decides(MayClose).Should().BeTrue();
        checks.Decides(new BillingAccess.InTenant()).Should().BeTrue();
        checks.Decides(AccessRequirement.AllowAnonymous()).Should().BeTrue("that anyone may send a request is decided by the set itself");
        checks.Decides(AccessRequirement.SignedIn()).Should().BeFalse("a set made by hand without the core's check has nobody to ask who is calling");
        checks.Decides(new LedgerAccess<Guid>.On(Guid.Empty)).Should().BeFalse();
        new AccessChecks<IBillingRequest>([]).Decides(MayClose).Should().BeFalse();

        asked.Entries.Should().BeEmpty("it is what a test asks for every requirement a module declares, so it reads nothing and refuses nobody");
    }

    [Fact]
    public async Task Nulls_are_refused_where_they_are_handed_in()
    {
        var checks = new AccessChecks<IBillingRequest>([]);

        FluentActions.Invoking(() => new AccessChecks<IBillingRequest>(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new AccessChecks<IBillingRequest>([null!])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => checks.Decides(null!)).Should().Throw<ArgumentNullException>();
        await FluentActions.Awaiting(() => checks.RequireAsync(null!, TestContext.Current.CancellationToken).AsTask()).Should().ThrowAsync<ArgumentNullException>();
        FluentActions.Invoking(() => AccessCheckServiceCollectionExtensions.AddAccessCheck<IBillingRequest, EverythingCheck>(null!)).Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------ one set per request interface

    [Fact]
    public async Task Each_request_interface_has_a_set_of_checks_of_its_own()
    {
        using var provider = Provider(services => services
            .AddAccessCheck<IBillingRequest, BillingAccessCheck>()
            .AddAccessCheck<IShippingRequest, EverythingCheck>());
        using var scope = provider.CreateScope();
        var asked = scope.ServiceProvider.GetRequiredService<Asked>();
        var request = new CloseInvoice(7, MayClose);

        await scope.ServiceProvider.GetRequiredService<AccessChecks<IBillingRequest>>().RequireAsync(request, TestContext.Current.CancellationToken);
        asked.Entries.Should().Equal(["billing"], "the check of the shipping module is not asked about a billing request");

        await scope.ServiceProvider.GetRequiredService<AccessChecks<IShippingRequest>>().RequireAsync(request, TestContext.Current.CancellationToken);
        asked.Entries.Should().Equal("billing", "everything");

        scope.ServiceProvider.GetRequiredService<AccessChecks<IShippingRequest>>().Decides(new LedgerAccess<Guid>.On(Guid.Empty)).Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<AccessChecks<IBillingRequest>>().Decides(new LedgerAccess<Guid>.On(Guid.Empty)).Should().BeFalse();
    }

    [Fact]
    public async Task A_check_two_modules_use_is_added_for_each_and_asked_for_each()
    {
        using var provider = Provider(services => services
            .AddAccessCheck<IBillingRequest, BillingAccessCheck>()
            .AddAccessCheck<IShippingRequest, BillingAccessCheck>());
        using var scope = provider.CreateScope();
        var request = new CloseInvoice(7, MayClose);

        await scope.ServiceProvider.GetRequiredService<AccessChecks<IBillingRequest>>().RequireAsync(request, TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<AccessChecks<IShippingRequest>>().RequireAsync(request, TestContext.Current.CancellationToken);

        scope.ServiceProvider.GetRequiredService<Asked>().Entries.Should().Equal("billing", "billing");
    }

    [Fact]
    public async Task Checks_are_asked_in_the_order_they_were_added()
    {
        using var first = Provider(services => services
            .AddAccessCheck<IBillingRequest, NothingCheck>()
            .AddAccessCheck<IBillingRequest, BillingAccessCheck>()
            .AddAccessCheck<IBillingRequest, EverythingCheck>());
        using var second = Provider(services => services
            .AddAccessCheck<IBillingRequest, EverythingCheck>()
            .AddAccessCheck<IBillingRequest, BillingAccessCheck>());

        (await AskedBy(first)).Should().Equal("billing");
        (await AskedBy(second)).Should().Equal("everything");

        static async Task<List<string>> AskedBy(ServiceProvider provider)
        {
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AccessChecks<IBillingRequest>>().RequireAsync(new CloseInvoice(7, MayClose), TestContext.Current.CancellationToken);
            return scope.ServiceProvider.GetRequiredService<Asked>().Entries;
        }
    }

    [Fact]
    public void Adding_a_check_for_an_interface_twice_adds_it_once()
    {
        var services = new ServiceCollection()
            .AddAccessCheck<IBillingRequest, BillingAccessCheck>()
            .AddAccessCheck<IBillingRequest, BillingAccessCheck>();

        services.Should().HaveCount(4, "the check, its place in the set, the set, and what a check keeps for a handler");
        services.Should().OnlyContain(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task The_set_of_a_module_that_added_no_check_decides_who_is_calling_and_nothing_else()
    {
        // What whoever asks the set in front of the handlers registers: the set alone. A module whose requests
        // require only what the core decides has no check to add, and its set is there all the same.
        using var provider = Provider(services => services.AddAccessChecks<IBillingRequest>());
        using var scope = provider.CreateScope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IBillingRequest>>();

        await checks.RequireAsync(new CloseInvoice(7, AccessRequirement.AllowAnonymous()), TestContext.Current.CancellationToken);
        checks.Decides(AccessRequirement.SignedIn()).Should().BeTrue("the core's check is in every set dependency injection makes");
        checks.Decides(AccessRequirement.RequiresSystemWork()).Should().BeTrue();
        await FluentActions.Awaiting(() => checks.RequireAsync(new CloseInvoice(7, MayClose), TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<InvalidOperationException>("a set that holds no check of the module's is closed to everything else");
        scope.ServiceProvider.GetRequiredService<Checked<int>>().Should().NotBeNull("what a check keeps for a handler is registered with the set");
    }

    [Fact]
    public async Task A_check_added_after_the_set_was_registered_is_in_it()
    {
        using var provider = Provider(services => services
            .AddAccessChecks<IBillingRequest>()
            .AddAccessCheck<IBillingRequest, BillingAccessCheck>()
            .AddAccessChecks<IBillingRequest>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<AccessChecks<IBillingRequest>>().RequireAsync(new CloseInvoice(7, MayClose), TestContext.Current.CancellationToken);

        scope.ServiceProvider.GetRequiredService<Asked>().Entries.Should().Equal(["billing"], "the set is made from the checks registered for the interface, whenever they were added");
    }

    [Fact]
    public void Registering_the_set_more_than_once_registers_it_once()
    {
        var services = new ServiceCollection()
            .AddAccessChecks<IBillingRequest>()
            .AddAccessChecks<IBillingRequest>();

        services.Should().HaveCount(2, "the set, and what a check keeps for a handler");
        services.Should().OnlyContain(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped);

        services.AddAccessChecks<IShippingRequest>();
        services.Should().HaveCount(3, "each request interface has a set of its own");
        FluentActions.Invoking(() => AccessCheckServiceCollectionExtensions.AddAccessChecks<IBillingRequest>(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task What_a_check_keeps_reaches_the_handler_through_the_scope()
    {
        using var provider = Provider(services => services.AddAccessCheck<IBillingRequest, BillingAccessCheck>());
        using var scope = provider.CreateScope();
        using var other = provider.CreateScope();
        var request = new CloseInvoice(7, MayClose);

        await scope.ServiceProvider.GetRequiredService<AccessChecks<IBillingRequest>>().RequireAsync(request, TestContext.Current.CancellationToken);

        scope.ServiceProvider.GetRequiredService<AccessChecks<IBillingRequest>>()
            .Should().BeSameAs(scope.ServiceProvider.GetRequiredService<AccessChecks<IBillingRequest>>(), "a scope has one set");
        FluentActions.Invoking(() => other.ServiceProvider.GetRequiredService<Checked<int>>().TakeFor(request))
            .Should().Throw<InvalidOperationException>("what was checked in one scope is not handed out in another");
        scope.ServiceProvider.GetRequiredService<Checked<int>>().TakeFor(request).Should().Be(7);
    }

    [Fact]
    public async Task A_check_the_host_registered_itself_is_the_one_asked()
    {
        // The host knows something the container does not, and makes the check itself.
        var asked = new Asked();
        using var provider = Provider(services => services
            .AddSingleton(new EverythingCheck(asked))
            .AddAccessCheck<IBillingRequest, EverythingCheck>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<AccessChecks<IBillingRequest>>().RequireAsync(new CloseInvoice(7, MayClose), TestContext.Current.CancellationToken);

        asked.Entries.Should().Equal("everything");
        scope.ServiceProvider.GetRequiredService<Asked>().Entries.Should().BeEmpty("the scope's own was handed to nobody");
    }
}
