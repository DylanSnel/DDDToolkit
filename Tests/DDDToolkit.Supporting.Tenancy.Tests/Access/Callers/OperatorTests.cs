using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// An operator of the application looks across tenants and holds no seat in any: its token carries a role the
/// application lists, tenant selection answers nobody for it, and the tenants' directory answers it and nobody
/// else, a page at a time.
/// </summary>
public class OperatorTests
{
    private const string OperatorRole = "operator";

    private static readonly Guid Odette = Guid.NewGuid();

    /// <summary>Harbor and two more tenants, with <see cref="OperatorRole"/> listed as an operator's.</summary>
    private static Harness ThreeTenants()
    {
        var harness = Harness.OfHarbor();
        harness.Seed(2, "quarry");
        harness.Seed(3, "anvil");
        harness.Options.OperatorTokenRoles.Add(OperatorRole);
        return harness;
    }

    /// <summary>Asks for a page as <paramref name="caller"/>, the way a request does: nobody to Tenancy, whoever it is to the toolkit.</summary>
    private static async Task<HostTenancy.TenantDirectoryPage> Page(Harness harness, Caller? caller, string? after = null, int size = 50)
    {
        using (caller is null ? null : Callers.Begin(caller))
        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.NotSeated)))
        {
            harness.Store.BeginUnitOfWork();
            return await harness.TenantDirectory.ListAsync(after, size, default);
        }
    }

    [Fact]
    public async Task Only_an_operator_role_lists_tenants()
    {
        var harness = ThreeTenants();

        (await Page(harness, Caller.User(Odette, OperatorRole))).Items.Should().HaveCount(3);

        // Everybody else is refused, and before anything is read: a seat of a tenant, a user with another token
        // role, an operator's role without a verified identity, an anonymous caller, the application itself and
        // its scoped work.
        Caller?[] others =
        [
            Caller.User(Odette),
            Caller.User(Odette, "analyst"),
            Caller.User(Odette, role: null),
            Caller.User(null, OperatorRole),
            Caller.Anonymous,
            Caller.System,
            Caller.SystemIn(TenancyWork.SystemScope),
            null,
        ];
        foreach (var other in others)
        {
            var refusal = await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => Page(harness, other), $"{other?.ToString() ?? "no caller"} is no operator");
            refusal.Kind.Should().Be(DDDToolkit.Exceptions.RefusalKind.NotPermitted);
            refusal.Message.Should().Be("Only an operator can do this.");
            harness.Store.Calls.Should().BeEmpty("a caller who is no operator learns nothing, and reads nothing");
        }

        // Nor is it a matter of what a caller sends: a page size or a marker that would be refused says nothing
        // to someone who is no operator.
        await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => Page(harness, Caller.User(Odette), size: 0));
        await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => Page(harness, Caller.User(Odette), after: "not a marker"));
    }

    [Fact]
    public async Task An_application_without_operators_has_no_directory()
    {
        var harness = Harness.OfHarbor();

        harness.Options.OperatorTokenRoles.Should().BeEmpty("an application has no operators until it lists a token role");
        await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => Page(harness, Caller.User(Odette, OperatorRole)));
        await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => Page(harness, Caller.User(Odette)));
    }

    [Fact]
    public async Task The_directory_pages_by_slug_and_counts_active_seats()
    {
        var harness = ThreeTenants();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.WatcherPack);
        var cy = await harness.SeatAt("Cy", harness.Harbor.South, HostCatalogue.WatcherPack);
        await harness.BySystemWork(h => h.Seats.SuspendAsync(cy, default));
        var odette = Caller.User(Odette, OperatorRole);

        // By slug, whatever order the tenants were made in, and a page holds what it was asked to.
        var first = await Page(harness, odette, size: 2);
        first.Items.Select(tenant => tenant.Slug).Should().Equal("anvil", "harbor");
        first.Next.Should().NotBeNull("a tenant comes after these two");

        var second = await Page(harness, odette, first.Next, size: 2);
        second.Items.Select(tenant => tenant.Slug).Should().Equal("quarry");
        second.Next.Should().BeNull("the last page has nothing after it");

        // A page that ends on the last tenant says so as well: no empty page follows it.
        var whole = await Page(harness, odette, size: 3);
        whole.Items.Select(tenant => tenant.Slug).Should().Equal("anvil", "harbor", "quarry");
        whole.Next.Should().BeNull();

        // Each with its organization's name, its status, and how many of its seats are active: Ada and Bert, not Cy.
        var harbor = whole.Items.Single(tenant => tenant.Slug == "harbor");
        harbor.Should().Be(new HostTenancy.TenantListing(harness.Tenant, "harbor", "Harbor Works", TenantStatus.Active, ActiveSeats: 2));
        whole.Items.Single(tenant => tenant.Slug == "quarry").Should().Be(new HostTenancy.TenantListing(new TenantId(2), "quarry", "Harbor Works", TenantStatus.Active, ActiveSeats: 1));
        harness.Store.Seat(bert).Status.Should().Be(SeatStatus.Active);

        // The marker is the directory's own: it names no tenant to whoever holds it, and the same one gives the same page.
        first.Next.Should().NotContain("harbor");
        (await Page(harness, odette, first.Next, size: 2)).Should().BeEquivalentTo(second);
    }

    [Fact]
    public async Task A_page_holds_one_to_two_hundred_tenants()
    {
        var harness = ThreeTenants();
        var odette = Caller.User(Odette, OperatorRole);

        HostTenancy.TenantDirectory.MostPerPage.Should().Be(200);
        foreach (var size in new[] { 0, -1, 201, int.MaxValue })
        {
            var refusal = await Refused.WithCodeAsync(TenancyRefusals.PageSizeInvalid, () => Page(harness, odette, size: size));
            refusal.Kind.Should().Be(DDDToolkit.Exceptions.RefusalKind.Invalid);
            refusal.Arguments["Max"].Should().Be(200);
            refusal.Message.Should().Be("A page holds 1 to 200 tenants.");
        }

        (await Page(harness, odette, size: 1)).Items.Should().ContainSingle();
        (await Page(harness, odette, size: 200)).Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_marker_the_directory_did_not_make_is_refused()
    {
        var harness = ThreeTenants();
        var odette = Caller.User(Odette, OperatorRole);

        // Not the directory's alphabet, not a slug once read, empty, or longer than any slug's marker.
        string[] none = ["harbor!", "not a marker", "", "SGFyYm9y", new string('a', 400), "aGFyYm9y="];
        foreach (var marker in none)
        {
            var refusal = await Refused.WithCodeAsync(TenancyRefusals.CursorInvalid, () => Page(harness, odette, marker), $"'{marker}' is no marker");
            refusal.Kind.Should().Be(DDDToolkit.Exceptions.RefusalKind.Invalid);
            refusal.Message.Should().Be("That page marker does not belong to this list. Start again from the first page.");
            harness.Store.Calls.Should().BeEmpty();
        }

        // A marker for a slug no tenant has is still a marker: the page starts after where that tenant would be.
        var first = await Page(harness, odette, size: 1);
        (await Page(harness, odette, first.Next)).Items.Select(tenant => tenant.Slug).Should().Equal("harbor", "quarry");
    }

    [Fact]
    public async Task An_operator_has_no_seat_whatever_the_header()
    {
        // The same person has a seat in Harbor, and signs in once as a member and once as an operator.
        var harbor = new TenantId(1);
        var seat = SeatId.CreateSequential();
        var seats = new ListedSeats().With(Odette, harbor, "harbor", seat);
        var services = new ServiceCollection();
        services.AddTenancyCore<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(options =>
        {
            options.Catalogue = HostCatalogue.Application;
            options.NewTenantId = () => harbor;
            options.NewSeatId = SeatId.CreateSequential;
            options.NewUnitId = OrganizationUnitId.CreateSequential;
            options.NewRoleId = RoleId.CreateSequential;
            options.OperatorTokenRoles.Add(OperatorRole);
        });
        services.AddSingleton<ISeatDirectory<TenantId, SeatId>>(seats);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var selection = scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>();

        (await selection.ResolveAsync(Caller.User(Odette), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostCaller.InSeat(harbor, seat));

        foreach (var slug in new[] { "harbor", "quarry", null, "" })
        {
            var asOperator = await selection.ResolveAsync(Caller.User(Odette, OperatorRole), slug, TestContext.Current.CancellationToken);

            asOperator.Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated), "an operator is nobody in every tenant, and is told what a person without a seat is told");
            asOperator.Actor.Should().BeNull("nobody changes nothing, so there is nobody to record");
        }

        seats.Lookups.Should().ContainSingle("only the member's request was looked up: an operator's token role is asked about before any seat is");
        provider.GetRequiredService<TenancyOperatorTokenRoles>().TokenRoles.Should().Equal(OperatorRole);
    }

    [Fact]
    public void A_token_role_is_not_both_an_operators_and_seated()
    {
        static void Register(Action<TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>> configure)
            => new ServiceCollection().AddTenancyCore<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(options =>
            {
                options.Catalogue = HostCatalogue.Application;
                options.NewTenantId = () => new TenantId(1);
                options.NewSeatId = SeatId.CreateSequential;
                options.NewUnitId = OrganizationUnitId.CreateSequential;
                options.NewRoleId = RoleId.CreateSequential;
                configure(options);
            });

        // The role every signed-in user has is seated unless the application says otherwise.
        FluentActions.Invoking(() => Register(options => options.OperatorTokenRoles.Add(TenantSelectionOptions.AuthenticatedTokenRole)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("These token roles are operators' and hold seats as well: authenticated. An operator holds no seat:*");

        FluentActions.Invoking(() => Register(options =>
            {
                options.TenantSelection.SeatedTokenRoles.Add("member");
                options.TenantSelection.SeatedTokenRoles.Add("staff");
                options.OperatorTokenRoles.Add("staff");
                options.OperatorTokenRoles.Add("member");
                options.OperatorTokenRoles.Add(OperatorRole);
            }))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*seats as well: member, staff. *", "every such role is named, and the one that is only an operator's is not");

        FluentActions.Invoking(() => Register(options => options.OperatorTokenRoles.Add(OperatorRole))).Should().NotThrow();
    }
}
