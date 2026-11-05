using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.HotChocolate.Tests.Domain;
using DDDToolkit.HotChocolate.Tests.GraphQl;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using FluentAssertions;
using Gallery.Api;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// The defaults for the classes of the toolkit's packages in a type that binds by convention. The methods of the
/// packages' entities are no fields. Members marked <c>[PersonalData]</c> or <c>[Bookkeeping]</c> are left out
/// unless the application lists them or resolves them. A class with <c>[DisplayName]</c> names its type, unless the
/// application names it.
/// </summary>
public sealed partial class PackageClassDefaultsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_methods_of_the_packages_entities_are_not_fields()
    {
        var sdl = await PrintAsync(graphql => graphql.AddQueryType<PackageQuery>());

        var memberRole = TypeBlock(sdl, "type MemberRoleOfTicketIdAndTicketId");
        memberRole.Should().Contain("startsAt: DateTime!");
        memberRole.Should().NotContain("appliesAt");

        var grant = TypeBlock(sdl, "type RoleGrant");
        grant.Should().Contain("startsAt: DateTime!");
        grant.Should().NotContain("appliesAt");
    }

    [Fact]
    public async Task Without_the_toolkit_conventions_the_methods_and_the_long_names_would_be_in_the_schema()
    {
        // The same query in a schema that does not call AddDDDToolkitTypes: proof that the tests above see its doing.
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddGraphQlTestsGraphQlRuntimeBindings()
            .AddQueryType<PackageQuery>()
            .BuildRequestExecutorAsync(cancellationToken: Cancellation);

        var sdl = executor.Schema.ToString();
        TypeBlock(sdl, "type RoleGrantOfTicketIdAndTicketId").Should().Contain("appliesAt(moment: DateTime!): Boolean!");
        TypeBlock(sdl, "type MemberRoleOfTicketIdAndTicketId").Should().Contain("appliesAt(moment: DateTime!): Boolean!");
        sdl.Should().Contain("type PlacementOfTicketIdAndTicketIdAndTicketId");
    }

    [Fact]
    public async Task The_methods_of_an_applications_own_class_stay_fields()
    {
        var sdl = await PrintAsync(graphql => graphql.AddQueryType<PackageQuery>());

        TypeBlock(sdl, "type Stay").Should().Contain("nights: Int!");
    }

    [Fact]
    public async Task A_class_with_a_display_name_names_its_type()
    {
        var sdl = await PrintAsync(graphql => graphql.AddQueryType<PackageQuery>());

        sdl.Should().Contain("type Placement {");
        sdl.Should().Contain("type RoleGrant {");
        sdl.Should().Contain("type NamedUnit {");
        sdl.Should().NotContain("PlacementOf");
        sdl.Should().NotContain("RoleGrantOf");
        sdl.Should().NotContain("NamedUnitOf");
    }

    [Fact]
    public async Task A_name_the_application_gives_takes_precedence_over_the_display_name()
    {
        var sdl = await PrintAsync(graphql => graphql
            .AddQueryType<PackageQuery>()
            .AddObjectType<NamedUnit<TicketId>>(unit => unit.Name("OrganizationUnitName")));

        sdl.Should().Contain("type OrganizationUnitName {");
        sdl.Should().NotContain("type NamedUnit ");
    }

    [Fact]
    public async Task A_display_name_that_is_no_GraphQL_name_fails_the_schema_build()
    {
        var build = () => PrintAsync(graphql => graphql.AddQueryType<SpacedQuery>());

        var failure = await build.Should().ThrowAsync<Exception>();
        failure.Which.ToString().Should().Contain("The display name 'Two words'").And.Contain("is not a GraphQL name");
    }

    [Fact]
    public async Task Personal_data_and_bookkeeping_are_left_out_of_a_type_that_binds_by_convention()
    {
        var sdl = await PrintAsync(graphql => graphql.AddQueryType<AccountQuery>());

        var account = TypeBlock(sdl, "type Account");
        account.Should().Contain("name: String!");
        account.Should().NotContain("identity");
        account.Should().NotContain("ledger");

        var input = TypeBlock(sdl, "input AccountDraftInput");
        input.Should().Contain("name: String!");
        input.Should().NotContain("identity");
        input.Should().NotContain("ledger");
    }

    [Fact]
    public async Task A_type_that_lists_a_marked_member_keeps_it()
    {
        var sdl = await PrintAsync(graphql => graphql
            .AddQueryType<AccountQuery>()
            .AddObjectType<Account>(account =>
            {
                account.BindFieldsExplicitly();
                account.Field(listed => listed.Name);
                account.Field(listed => listed.Identity);
            }));

        var account = TypeBlock(sdl, "type Account");
        account.Should().Contain("identity: UUID!");
        account.Should().NotContain("ledger");
    }

    [Fact]
    public async Task A_marked_member_with_a_resolver_of_its_own_stays()
    {
        var sdl = await PrintAsync(graphql => graphql
            .AddQueryType<AccountQuery>()
            .AddObjectType<Account>(account => account
                .Field(resolved => resolved.Ledger)
                .Resolve(context => context.Parent<Account>().Ledger)));

        var account = TypeBlock(sdl, "type Account");
        account.Should().Contain("ledger: String");
        account.Should().NotContain("identity");
    }

    [Fact]
    public async Task A_generated_type_class_shows_personal_data_with_a_resolver_named_like_the_property()
    {
        var sdl = await PrintAsync(graphql => graphql.AddGalleryTypes().AddDDDToolkitPermissionAuthorization(), services => services.AddGalleryServices());

        TypeBlock(sdl, "type CuratorProfile").Should().Contain("email: String!");
    }

    [Fact]
    public async Task A_resolver_bound_with_BindMember_to_a_member_of_its_own_name_is_removed_by_HotChocolates_generator()
    {
        // HotChocolate's generator ignores the bound member's field by its name, and the resolver has that name too.
        // This pins the behaviour that the GraphQL docs warn about. Without [BindMember], GetRoom would be the field.
        var sdl = await PrintAsync(graphql => graphql.AddGalleryTypes().AddDDDToolkitPermissionAuthorization(), services => services.AddGalleryServices());

        var curator = TypeBlock(sdl, "type CuratorProfile");
        curator.Should().Contain("name: String!");
        curator.Should().NotContain("room");
    }

    [Fact]
    public void Tenancy_marks_the_seats_identity_as_personal_data_and_leaves_the_invitations_address_unmarked()
    {
        Marked<PersonalDataAttribute>(typeof(SeatAggregate<,,,>), "Identity").Should().BeTrue();

        // Whoever may read invitations needs the address.
        Marked<PersonalDataAttribute>(typeof(InvitationAggregate<,,,,>), "Address").Should().BeFalse();
    }

    [Fact]
    public void Both_packages_mark_the_starter_role_fields_of_a_role_as_bookkeeping()
    {
        foreach (var role in new[] { typeof(RoleAggregate<,>), typeof(CustomRoleAggregate<>) })
        {
            Marked<BookkeepingAttribute>(role, "FromStarterRole").Should().BeTrue(role.Name);
            Marked<BookkeepingAttribute>(role, "PermissionsFromStarterRole").Should().BeTrue(role.Name);
        }
    }

    [Fact]
    public void A_roles_facts_are_internal()
        => Marked<InternalAttribute>(typeof(RoleAggregate<,>), "Facts").Should().BeTrue();

    [Fact]
    public void The_packages_entities_without_an_id_of_their_own_are_entities()
    {
        typeof(Placement<,,>).Should().Implement<IEntity>();
        typeof(RoleGrant<,>).Should().Implement<IEntity>();
        typeof(MemberRole<,>).Should().Implement<IEntity>();
    }

    [Fact]
    public void Tenancys_display_names_are_GraphQL_names_and_Membership_has_none()
    {
        var named = typeof(Placement<,,>).Assembly.GetTypes()
            .Select(type => (Type: type, Display: type.GetCustomAttribute<DisplayNameAttribute>(inherit: false)))
            .Where(pair => pair.Display is not null)
            .ToDictionary(pair => pair.Display!.Name, pair => pair.Type);

        named.Keys.Should().BeEquivalentTo(
        [
            "AcceptedInvitation", "IssuedInvitation", "NamedUnit", "PermissionReach", "Placement", "RoleGrant",
            "SeatInTenant", "SeatOverview", "TenantListing", "TenantPage", "UnitInTree", "UnitRef",
        ]);
        named.Keys.Should().AllSatisfy(name => GraphQLName().IsMatch(name).Should().BeTrue(name));

        typeof(MemberRole<,>).Assembly.GetTypes()
            .Where(type => type.IsDefined(typeof(DisplayNameAttribute), inherit: false))
            .Should().BeEmpty("an application can close Membership's classes more than once");
    }

    // The query roots below are this file's own, so the full test schema stays as it is.

    /// <summary>A query that returns the packages' entities and an application's own class.</summary>
    public sealed class PackageQuery
    {
        /// <summary>Membership's role on a member, with its method AppliesAt.</summary>
        public IReadOnlyList<MemberRole<TicketId, TicketId>> MemberRoles() => [];

        /// <summary>Tenancy's placement, whose grants carry the method AppliesAt.</summary>
        public IReadOnlyList<Placement<TicketId, TicketId, TicketId>> Placements() => [];

        /// <summary>Tenancy's name of a unit.</summary>
        public IReadOnlyList<NamedUnit<TicketId>> Units() => [];

        /// <summary>An application's own class with a method.</summary>
        public Stay Stay() => new(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 4));
    }

    /// <summary>An application's class, outside any package. Its method is a field.</summary>
    public sealed record Stay(DateOnly From, DateOnly Until)
    {
        /// <summary>How many nights the stay lasts.</summary>
        public int Nights() => Until.DayNumber - From.DayNumber;
    }

    /// <summary>A query that reads an account and takes one as an argument.</summary>
    public sealed class AccountQuery
    {
        /// <summary>An account.</summary>
        public Account Account() => new(Guid.Empty, "Ada", "kept since 2026");

        /// <summary>The name of the draft it is given.</summary>
        public string Echo(AccountDraft draft) => draft.Name;
    }

    /// <summary>A class with one member of each kind.</summary>
    public sealed class Account(Guid identity, string name, string? ledger)
    {
        /// <summary>The account's id at an identity provider.</summary>
        [PersonalData]
        public Guid Identity { get; set; } = identity;

        /// <summary>A name the application shows.</summary>
        public string Name { get; set; } = name;

        /// <summary>A record a package keeps for its own work.</summary>
        [Bookkeeping]
        public string? Ledger { get; set; } = ledger;
    }

    /// <summary>An input with one member of each kind. A left-out member keeps its default when HotChocolate makes it.</summary>
    public sealed class AccountDraft
    {
        /// <summary>The account's id at an identity provider.</summary>
        [PersonalData]
        public Guid Identity { get; set; }

        /// <summary>A name the application shows.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>A record a package keeps for its own work.</summary>
        [Bookkeeping]
        public string? Ledger { get; set; }
    }

    /// <summary>A query that returns a class whose display name has a space.</summary>
    public sealed class SpacedQuery
    {
        /// <summary>The class.</summary>
        public Spaced Spaced() => new();
    }

    /// <summary>A class whose display name is no GraphQL name.</summary>
    [DisplayName("Two words")]
    public sealed class Spaced
    {
        /// <summary>A value.</summary>
        public int Value { get; } = 1;
    }

    private static async Task<string> PrintAsync(
        Func<IRequestExecutorBuilder, IRequestExecutorBuilder> configure, Func<IServiceCollection, IServiceCollection>? services = null)
    {
        var graphql = (services ?? (none => none))(new ServiceCollection())
            .AddGraphQL()
            .AddDDDToolkitTypes()
            .AddGraphQlTestsGraphQlRuntimeBindings();

        var executor = await configure(graphql).BuildRequestExecutorAsync(cancellationToken: Cancellation);
        return executor.Schema.ToString();
    }

    private static bool Marked<TAttribute>(Type type, string property)
        where TAttribute : Attribute
        => type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance) is { } found
            && Attribute.IsDefined(found, typeof(TAttribute));

    /// <summary>The body of one SDL type declaration, so an assertion cannot match a different type.</summary>
    private static string TypeBlock(string sdl, string header)
    {
        var start = sdl.IndexOf(header + " ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the schema should declare '{0}'", header);

        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return sdl[open..close];
    }

    [GeneratedRegex("^[_A-Za-z][_0-9A-Za-z]*$")]
    private static partial Regex GraphQLName();
}
