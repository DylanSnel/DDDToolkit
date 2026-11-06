using DTS.DDD.Abstractions.Access;
using DTS.DDD.Access;

namespace DTS.DDD.Analyzers.Tests.Generation;

/// <summary>
/// A request says what it requires of its caller with an attribute where the requirement reads no value of the
/// request but a key and an id it carries, and the generator turns the attribute into plain C# at compile time: one
/// line per request, in its module's interface, declared <c>partial</c>, which implements <c>RequiredAccess</c> with
/// it. Each test compiles a module of its own and asks the request it made what it requires, so a line that compiled
/// and called the wrong case would fail here. What the packages' checks then do with each case is their own tests'.
/// </summary>
public partial class RequirementAttributeGenerationTests
{
    private const string Hint = "BillingAccessBehavior.";

    /// <summary>A module's one line, declared partial so its requests' attributes are written into it.</summary>
    internal const string Billing =
        """
        using DTS.DDD.Abstractions.Attributes;
        using DTS.DDD.Access;

        namespace Shop.Billing;

        [AccessRequests]
        public partial interface IBillingRequest : IRequireAccess;
        """;

    /// <summary>The ids a request of the module carries.</summary>
    internal const string Ids =
        """
        using System;
        using DTS.DDD.Abstractions.Attributes;

        namespace Shop.Billing;

        [EntityId<Guid>] public readonly partial record struct TenantId;
        [EntityId<Guid>] public readonly partial record struct SeatId;
        [EntityId<Guid>] public readonly partial record struct OrganizationUnitId;
        [EntityId<Guid>] public readonly partial record struct RoleId;
        [EntityId<Guid>] public readonly partial record struct UserId;
        [EntityId<Guid>] public readonly partial record struct DocumentId;
        [EntityId<Guid>] public readonly partial record struct DocumentShareId;
        [EntityId<Guid>] public readonly partial record struct FolderId;
        [EntityId<Guid>] public readonly partial record struct FolderShareId;
        """;

    /// <summary>One of the module's ids, over <paramref name="value"/>.</summary>
    private static object IdOf(EmittedAssembly emitted, string name, Guid value) => emitted.New("Shop.Billing." + name, value);

    /// <summary>What the request of <paramref name="typeName"/> answers, made with <paramref name="arguments"/> and asked through the toolkit's interface.</summary>
    private static AccessRequirement Asked(GeneratorRunOutcome result, string typeName, params object?[] arguments)
        => ((IRequireAccess)result.Emit().New("Shop.Billing." + typeName, arguments)).RequiredAccess;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_module_with_a_namespace_of_its_own_called_DDDToolkit_gets_a_line_that_compiles(bool mediator)
    {
        // Contoso.DTS.DDD.Billing: written out, DTS.DDD.Access.IRequireAccess would be looked up in Contoso.DDDToolkit.
        var host = GeneratorTestHost.Create(
            """
            using DTS.DDD.Abstractions.Attributes;
            using DTS.DDD.Access;

            namespace Contoso.DTS.DDD.Billing;

            [AccessRequests]
            public partial interface IBillingRequest : IRequireAccess;

            [SignedIn] public sealed record MyInvoices : IBillingRequest;
            """,
            "Billing.cs");
        var result = (mediator ? host.WithMediator() : host.WithDependencyInjection()).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        var emitted = result.Emit();
        ((IRequireAccess)emitted.New("Contoso.DTS.DDD.Billing.MyInvoices")).RequiredAccess.Should().Be(AccessRequirement.SignedIn());
        emitted.Type("Contoso.DTS.DDD.Billing.IBillingRequest")
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Select(property => property.Name)
            .Should().Equal([typeof(IRequireAccess).FullName + "." + nameof(IRequireAccess.RequiredAccess)], "the member is named after the interface alone, not the alias it is written through");
    }

    [Fact]
    public void An_interface_that_is_not_partial_gets_no_requirements_and_its_requests_say_their_own()
    {
        var result = GeneratorTestHost.Create(Billing.Replace("public partial interface", "public interface", StringComparison.Ordinal), "Billing.cs")
            .WithSource(
                """
                using DTS.DDD.Access;

                namespace Shop.Billing;

                public sealed record ListPlans : IBillingRequest
                {
                    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.AllowAnonymous();
                }
                """,
                "Requests.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.GeneratedSources.Should().BeEmpty("the property alone needs nothing written");
    }

    /// <summary>A current user whose host answers the user a test gives it, where nothing was begun.</summary>
    public static DTS.DDD.Abstractions.Access.ICurrentUser Answering(DTS.DDD.Abstractions.Access.CurrentUserSnapshot user)
        => new DTS.DDD.Access.CurrentUser(source: new Fixed(user));

    /// <summary>A host that answers one user.</summary>
    private sealed class Fixed(DTS.DDD.Abstractions.Access.CurrentUserSnapshot user) : DTS.DDD.Access.ICurrentUserSource
    {
        /// <inheritdoc />
        public DTS.DDD.Abstractions.Access.CurrentUserSnapshot? Current => user;
    }
}
