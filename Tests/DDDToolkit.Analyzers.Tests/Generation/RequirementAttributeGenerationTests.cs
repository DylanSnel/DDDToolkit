using DDDToolkit.Access;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A request says what it requires of its caller with an attribute where the requirement reads no value of the
/// request but a key and an id it carries, and the generator turns the attribute into plain C# at compile time: one
/// line per request, in its module's interface, declared <c>partial</c>, which implements <c>RequiredAccess</c> with
/// it. Each test compiles a module of its own and asks the request it made what it requires, so a line that compiled
/// and called the wrong case would fail here. What the packages' checks then do with each case is their own tests'.
/// </summary>
public class RequirementAttributeGenerationTests
{
    private const string Hint = "BillingAccessBehavior.";

    /// <summary>A module's one line, declared partial so its requests' attributes are written into it.</summary>
    internal const string Billing =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;

        namespace Shop.Billing;

        [AccessRequests]
        public partial interface IBillingRequest : IRequireAccess;
        """;

    /// <summary>What the requests of a test see: the toolkit's and both packages' attributes, and the module's ids.</summary>
    internal const string Usings =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using DDDToolkit.Supporting.Membership;
        using DDDToolkit.Supporting.Membership.Access;
        using DDDToolkit.Supporting.Tenancy;
        using DDDToolkit.Supporting.Tenancy.Access;

        namespace Shop.Billing;

        """;

    /// <summary>The ids a request of the module carries.</summary>
    internal const string Ids =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Shop.Billing;

        [EntityId<Guid>] public readonly partial record struct TenantId;
        [EntityId<Guid>] public readonly partial record struct SeatId;
        [EntityId<Guid>] public readonly partial record struct OrganizationUnitId;
        [EntityId<Guid>] public readonly partial record struct RoleId;
        [EntityId<Guid>] public readonly partial record struct UserId;
        [EntityId<Guid>] public readonly partial record struct DocumentId;
        [EntityId<Guid>] public readonly partial record struct DocumentShareId;
        """;

    /// <summary>The module's domain, in a project of its own as an application has it: Tenancy's classes, and a document with members.</summary>
    internal const string Domain =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Membership;
        using DDDToolkit.Supporting.Tenancy;

        namespace Shop.Billing;

        [TenantAggregate<TenantId>] public sealed partial class Tenant;
        [OrganizationAggregate<TenantId>] public sealed partial class Organization;
        [OrganizationUnit<OrganizationUnitId>] public sealed partial class OrganizationUnit;
        [SeatAggregate<SeatId>] public sealed partial class Seat;
        [RoleAggregate<RoleId>] public sealed partial class Role;

        [Member<DocumentShareId, UserId, NamedRole, Document>]
        public sealed partial class DocumentShare;

        [AggregateRoot<DocumentId>]
        public sealed partial class Document
        {
            public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

            public Document(DocumentId id, UserId owner) : base(id) => OwnerId = owner;

            public UserId OwnerId { get; private set; }

            public partial IReadOnlyList<DocumentShare> Shares { get; }
        }
        """;

    /// <summary>
    /// A project of the module's requests and its ids, with both packages, and with the library or without it: one
    /// assembly, so a test can run what it compiled.
    /// </summary>
    internal static GeneratorTestHost Module(string requests, bool mediator = false)
    {
        var host = GeneratorTestHost.Create(Billing, "Billing.cs").WithTenancy().WithMembershipAlone().WithSource(Ids, "Ids.cs").WithSource(Usings + requests, "Requests.cs");
        return mediator ? host.WithMediator() : host.WithDependencyInjection();
    }

    /// <summary>
    /// A project of the module's requests above its contracts and its domain, each a project of its own as an
    /// application has them: Tenancy's classes and a document with members are seen through metadata, which is how
    /// a marker's id is told from another.
    /// </summary>
    internal static GeneratorTestHost OverTheDomain(string requests)
        => GeneratorTestHost.Create(Billing, "Billing.cs")
            .WithTenancy()
            .WithMembershipAlone()
            .WithReferencedAssembly(Ids, "Shop.Contracts")
            .WithReferencedProject("Shop.Domain", project => project.WithSource(Domain, "Domain.cs"), GeneratorTestHost.MemberListGenerators())
            .WithSource(Usings + requests, "Requests.cs")
            .WithDependencyInjection();

    /// <summary>One of the module's ids, over <paramref name="value"/>.</summary>
    private static object IdOf(EmittedAssembly emitted, string name, Guid value) => emitted.New("Shop.Billing." + name, value);

    /// <summary>What the request of <paramref name="typeName"/> answers, made with <paramref name="arguments"/> and asked through the toolkit's interface.</summary>
    private static AccessRequirement Asked(GeneratorRunOutcome result, string typeName, params object?[] arguments)
        => ((IRequireAccess)result.Emit().New("Shop.Billing." + typeName, arguments)).RequiredAccess;

    // ------------------------------------------------------------------ the core's

    [Fact]
    public void A_request_anyone_may_send_answers_AllowAnonymous()
    {
        var result = Module("[AllowAnonymous] public sealed record ListPlans : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.ListPlans => global::DDDToolkit.Access.AccessRequirement.AllowAnonymous(),");
        Asked(result, "ListPlans").Should().Be(AccessRequirement.AllowAnonymous());
    }

    [Fact]
    public void A_request_for_a_signed_in_user_answers_SignedIn()
    {
        var result = Module("[SignedIn] public sealed record MyInvoices : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "global::Shop.Billing.MyInvoices => global::DDDToolkit.Access.AccessRequirement.SignedIn(),");
        Asked(result, "MyInvoices").Should().Be(AccessRequirement.SignedIn());
    }

    // ------------------------------------------------------------------ Tenancy's

    [Fact]
    public void A_request_in_the_tenant_answers_InTenant()
    {
        var result = Module("[InTenant] public sealed record Invoices : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "global::Shop.Billing.Invoices => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.InTenant(),");
        Asked(result, "Invoices").Should().Be(TenancyAccess.InTenant());
    }

    [Fact]
    public void A_query_for_operators_answers_RequiresOperator()
    {
        var result = Module("[RequiresOperator] public sealed record AllLedgers : IBillingRequest;").RunCore();

        result.ShouldCompile();
        Asked(result, "AllLedgers").Should().Be(TenancyAccess.RequiresOperator());
    }

    [Fact]
    public void A_key_without_a_marker_is_held_for_the_whole_tenant_where_tenancy_says_so()
    {
        var result = Module("[RequiresKey(\"billing.close\")] public sealed record CloseTheBooks(int Year) : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.CloseTheBooks => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtTenant(\"billing.close\"),");
        Asked(result, "CloseTheBooks", 2026).Should().Be(TenancyAccess.AtTenant("billing.close"));
    }

    [Fact]
    public void A_key_is_read_from_a_constant_as_its_value()
    {
        var result = Module(
            """
            public static class BillingKeys
            {
                public const string Close = "billing.close";
            }

            [RequiresKey(RequiredKey)]
            public sealed record CloseTheBooks(int Year) : IBillingRequest
            {
                public const string RequiredKey = BillingKeys.Close;
            }
            """).RunCore();

        result.ShouldCompile();
        Asked(result, "CloseTheBooks", 2026).Should().Be(TenancyAccess.AtTenant("billing.close"), "a request's own constant is in scope of its attributes");
    }

    [Theory]
    [InlineData("public sealed record OpenLedger(string Name, [AtUnit] OrganizationUnitId Unit) : IBillingRequest;", "a positional parameter of a record")]
    [InlineData("public sealed record OpenLedger(string Name, [property: AtUnit] OrganizationUnitId Unit) : IBillingRequest;", "the property a positional parameter declares")]
    [InlineData("public sealed class OpenLedger(string name, OrganizationUnitId unit) : IBillingRequest { public string Name { get; } = name; [AtUnit] public OrganizationUnitId Unit { get; } = unit; }", "an ordinary property")]
    public void A_key_at_a_unit_reads_the_member_its_marker_is_on(string request, string marked)
    {
        var result = Module("[RequiresKey(\"billing.open-ledger\")]\n" + request).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.OpenLedger request => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtUnit(\"billing.open-ledger\", request.Unit),",
            marked);

        var unit = Guid.NewGuid();
        var emitted = result.Emit();
        var id = IdOf(emitted, "OrganizationUnitId", unit);
        var asked = ((IRequireAccess)emitted.New("Shop.Billing.OpenLedger", "Ledger", id)).RequiredAccess;
        asked.GetType().Name.Should().Be("AtUnit`1");
        asked.ToString().Should().Contain("billing.open-ledger").And.Contain(unit.ToString());
    }

    // ------------------------------------------------------------------ Membership's

    [Fact]
    public void A_key_on_a_resource_reads_the_resource_and_the_version_the_caller_read()
    {
        var result = Module(
            """
            [RequiresKey("documents.share")]
            public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IBillingRequest;

            [RequiresKey("documents.read")]
            public sealed record ReadDocument([OnResource] DocumentId Document) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.ShareDocument request => global::DDDToolkit.Supporting.Membership.Access.MemberAccess.On(\"documents.share\", request.Document, request.ExpectedVersion),");
        result.ShouldContain(Hint, "global::Shop.Billing.ReadDocument request => global::DDDToolkit.Supporting.Membership.Access.MemberAccess.On(\"documents.read\", request.Document),");

        var emitted = result.Emit();
        var document = Guid.NewGuid();
        var id = IdOf(emitted, "DocumentId", document);
        var user = IdOf(emitted, "UserId", Guid.NewGuid());
        var asked = ((IRequireAccess)emitted.New("Shop.Billing.ShareDocument", id, user, 7L)).RequiredAccess;
        asked.ToString().Should().Contain("documents.share").And.Contain(document.ToString()).And.Contain("7");
    }

    [Fact]
    public void A_query_seen_with_a_key_answers_SeenWith_over_its_resource()
    {
        var result = Module("[SeenWith<DocumentId>(\"documents.view\")] public sealed record ListDocuments : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.ListDocuments => global::DDDToolkit.Supporting.Membership.Access.MemberAccess.SeenWith<global::Shop.Billing.DocumentId>(\"documents.view\"),",
            "the attribute's type argument is the method's");
        Asked(result, "ListDocuments").GetType().Name.Should().Be("SeenWith");
    }

    // ------------------------------------------------------------------ the property, beside the attributes

    [Fact]
    public void A_request_that_computes_its_requirement_answers_with_its_own_and_gets_no_line()
    {
        var result = Module(
            """
            public sealed record ListSeats(OrganizationUnitId? Unit) : IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => Unit is { } unit
                    ? TenancyAccess.AtUnit("billing.read", unit)
                    : TenancyAccess.AtTenant("billing.read");
            }

            [InTenant] public sealed record Invoices : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldNotContain(Hint, "ListSeats", "a request that answers the property itself is answered by C#, not by a line");
        Asked(result, "ListSeats", new object?[] { null }).Should().Be(TenancyAccess.AtTenant("billing.read"));
        Asked(result, "Invoices").Should().Be(TenancyAccess.InTenant());
    }

    [Fact]
    public void A_derived_request_with_attributes_of_its_own_holds_to_its_base_s_as_well_the_base_s_first_on_a_line_before_the_base_s()
    {
        var result = Module(
            """
            [InTenant] public abstract record Report : IBillingRequest;
            public sealed record MonthlyReport(int Month) : Report;
            [SignedIn] public sealed record MyReport : Report;

            [RequiresKey("tenancy.seats.manage")] public abstract record SeatCommand([OrOwn] SeatId Seat) : IBillingRequest;
            [SignedIn] public sealed record RenameMySeat(SeatId Seat) : SeatCommand(Seat);
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        var source = result.Source(Hint);
        source.IndexOf("global::Shop.Billing.MyReport =>", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("global::Shop.Billing.Report =>", StringComparison.Ordinal), "a line for a base would otherwise take the derived request too");

        // As several [Authorize] on a controller and the one it derives from: what the derived request carries adds to what
        // its base requires, and never takes its place, so a signed-in caller who is no seat of the tenant is not let in.
        Asked(result, "MyReport").Should().Be(AccessRequirement.All(TenancyAccess.InTenant(), AccessRequirement.SignedIn()));
        Asked(result, "MonthlyReport", 5).Should().Be(TenancyAccess.InTenant(), "a request without attributes of its own takes its base's line");
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.RenameMySeat request => global::DDDToolkit.Access.AccessRequirement.All(global::DDDToolkit.Access.AccessRequirement.Any(global::DDDToolkit.Access.AccessRequirement.Own(request.Seat), "
            + "global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtTenant(\"tenancy.seats.manage\")), global::DDDToolkit.Access.AccessRequirement.SignedIn()),",
            "the base's key, with the caller's own it marks, then the derived request's own attribute");
    }

    [Fact]
    public void The_interface_gets_one_line_per_request_in_plain_csharp_and_a_last_one_that_lets_nobody_through()
    {
        var result = Module(
            """
            [SignedIn] public sealed record MyInvoices : IBillingRequest;
            [RequiresKey("billing.close")] public sealed record CloseTheBooks(int Year) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            Hint,
            """
            partial interface IBillingRequest
            {
                /// <inheritdoc cref="global::DDDToolkit.Access.IRequireAccess.RequiredAccess"/>
                global::DDDToolkit.Access.AccessRequirement DDDToolkit_Generated_IRequireAccess.RequiredAccess => this switch
                {
                    global::Shop.Billing.CloseTheBooks => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtTenant("billing.close"),
                    global::Shop.Billing.MyInvoices => global::DDDToolkit.Access.AccessRequirement.SignedIn(),
                    _ => throw new global::System.InvalidOperationException(
                        this.GetType().FullName + " declares no access requirement: it carries no requirement attribute, and does not answer RequiredAccess itself. "
                        + "A request that declares nothing lets nobody through."),
                };
            }
            """,
            "the call each attribute stands for, written out, and nothing that reads an attribute when the request is sent");
    }

    [Fact]
    public void A_type_the_interface_has_no_line_for_is_stopped_when_it_is_asked()
    {
        // A request that states nothing is DDD00077 when it builds; built all the same, it reaches the end of the switch
        // when it is asked, as a request of another project that implements the interface does, and nobody passes.
        var result = Module(
            """
            [InTenant] public sealed record Invoices : IBillingRequest;
            public sealed record Forgotten : IBillingRequest;
            """).RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00077");
        result.ShouldNotContain(Hint, "GetCustomAttribute", "nothing is read by reflection");
        result.CompilationErrors.Should().BeEmpty();
        using var stream = new MemoryStream();
        result.OutputCompilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.Should().BeTrue();
        var emitted = new EmittedAssembly(System.Reflection.Assembly.Load(stream.ToArray()));

        ((IRequireAccess)emitted.New("Shop.Billing.Invoices")).RequiredAccess.Should().Be(TenancyAccess.InTenant());
        var forgotten = (IRequireAccess)emitted.New("Shop.Billing.Forgotten");
        FluentActions.Invoking(() => forgotten.RequiredAccess).Should().Throw<InvalidOperationException>()
            .WithMessage("Shop.Billing.Forgotten declares no access requirement: *A request that declares nothing lets nobody through.");
    }

    [Fact]
    public void A_request_in_parts_is_read_whichever_part_carries_its_attribute()
    {
        // The first part is bare, and the attribute and the interface are on the second.
        var result = GeneratorTestHost.Create(Billing, "Billing.cs")
            .WithTenancy()
            .WithMembershipAlone()
            .WithSource(Usings + "public partial record MyInvoices;", "First.cs")
            .WithSource(Usings + "[SignedIn] public partial record MyInvoices : IBillingRequest;", "Second.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.MyInvoices => global::DDDToolkit.Access.AccessRequirement.SignedIn(),");
        Asked(result, "MyInvoices").Should().Be(AccessRequirement.SignedIn());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_module_with_a_namespace_of_its_own_called_DDDToolkit_gets_a_line_that_compiles(bool mediator)
    {
        // Contoso.DDDToolkit.Billing: written out, DDDToolkit.Access.IRequireAccess would be looked up in Contoso.DDDToolkit.
        var host = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Access;

            namespace Contoso.DDDToolkit.Billing;

            [AccessRequests]
            public partial interface IBillingRequest : IRequireAccess;

            [SignedIn] public sealed record MyInvoices : IBillingRequest;
            """,
            "Billing.cs");
        var result = (mediator ? host.WithMediator() : host.WithDependencyInjection()).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        var emitted = result.Emit();
        ((IRequireAccess)emitted.New("Contoso.DDDToolkit.Billing.MyInvoices")).RequiredAccess.Should().Be(AccessRequirement.SignedIn());
        emitted.Type("Contoso.DDDToolkit.Billing.IBillingRequest")
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Select(property => property.Name)
            .Should().Equal([typeof(IRequireAccess).FullName + "." + nameof(IRequireAccess.RequiredAccess)], "the member is named after the interface alone, not the alias it is written through");
    }

    [Fact]
    public void Requests_that_share_an_abstract_shape_each_say_their_key_and_read_the_unit_it_carries()
    {
        var result = Module(
            """
            public abstract record UnitCommand([property: AtUnit] OrganizationUnitId Unit) : IBillingRequest;

            [RequiresKey("billing.open-ledger")]
            public sealed record OpenLedger(OrganizationUnitId Unit) : UnitCommand(Unit);

            public abstract record PlainUnitCommand(OrganizationUnitId Unit) : IBillingRequest;

            [RequiresKey("billing.close-ledger")]
            public sealed record CloseLedger([AtUnit] OrganizationUnitId Unit) : PlainUnitCommand(Unit);
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.OpenLedger request => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtUnit(\"billing.open-ledger\", request.Unit),",
            "the marker the base declares on its property");
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.CloseLedger request => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtUnit(\"billing.close-ledger\", request.Unit),",
            "the property the derived record's parameter is, which its base declares");

        var emitted = result.Emit();
        var unit = Guid.NewGuid();
        foreach (var (request, key) in new[] { ("OpenLedger", "billing.open-ledger"), ("CloseLedger", "billing.close-ledger") })
        {
            var asked = ((IRequireAccess)emitted.New("Shop.Billing." + request, IdOf(emitted, "OrganizationUnitId", unit))).RequiredAccess;
            asked.GetType().Name.Should().Be("AtUnit`1");
            asked.ToString().Should().Contain(key).And.Contain(unit.ToString());
        }
    }

    [Fact]
    public void A_request_answered_by_an_interface_between_it_and_the_module_s_answers_with_that_one()
    {
        var result = Module(
            """
            public interface IPublicBillingRequest : IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.AllowAnonymous();
            }

            public sealed record ListPlans : IPublicBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        Asked(result, "ListPlans").Should().Be(AccessRequirement.AllowAnonymous(), "C# takes the more specific default, and the request says nothing else");
    }

    // ------------------------------------------------------------------ where it is written

    [Fact]
    public void Without_the_library_the_interface_gets_its_requirements_and_no_behavior()
    {
        var result = Module("[SignedIn] public sealed record MyInvoices : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "partial interface IBillingRequest");
        result.ShouldNotContain(Hint, "AccessBehavior<TMessage, TResponse>", "the behavior is written only where the project uses the library");
        Asked(result, "MyInvoices").Should().Be(AccessRequirement.SignedIn(), "whatever asks the checks reads the line");
    }

    [Fact]
    public void With_the_library_the_requirements_are_written_beside_the_behavior_in_one_file()
    {
        var result = Module("[SignedIn] public sealed record MyInvoices : Mediator.ICommand, IBillingRequest;", mediator: true).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.GeneratedSources.Where(source => source.HintName.Contains(Hint, StringComparison.Ordinal)).Should().ContainSingle();
        result.ShouldContain(Hint, "partial interface IBillingRequest");
        result.ShouldContain(Hint, "public sealed class BillingAccessBehavior<TMessage, TResponse>");
    }

    [Fact]
    public void An_interface_that_is_not_partial_gets_no_requirements_and_its_requests_say_their_own()
    {
        var result = GeneratorTestHost.Create(Billing.Replace("public partial interface", "public interface", StringComparison.Ordinal), "Billing.cs")
            .WithSource(
                """
                using DDDToolkit.Access;

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

    // ------------------------------------------------------------------ the request with the library

    [Fact]
    public async Task The_behavior_holds_a_request_to_what_its_attribute_says()
    {
        var result = Module(
            """
            [SignedIn] public sealed record MyInvoices : Mediator.ICommand, IBillingRequest;

            public static class Probe
            {
                public static async System.Threading.Tasks.Task<string> SendAsync(ICallerAccessor callers)
                {
                    var behavior = new BillingAccessBehavior<MyInvoices, Mediator.Unit>(new AccessChecks<IBillingRequest>(new IAccessCheck[] { new CallerAccessCheck(callers) }));
                    try
                    {
                        await behavior.Handle(new MyInvoices(), (message, cancellationToken) => new System.Threading.Tasks.ValueTask<Mediator.Unit>(Mediator.Unit.Value), System.Threading.CancellationToken.None);
                        return "handled";
                    }
                    catch (DDDToolkit.Exceptions.RefusalException refused)
                    {
                        return refused.Code;
                    }
                }
            }
            """,
            mediator: true).RunCore();

        result.ShouldCompile();
        var emitted = result.Emit();
        var signedIn = (Task<string>)emitted.CallStatic("Shop.Billing.Probe", "SendAsync", new Answering(DDDToolkit.Abstractions.Access.Caller.User(Guid.NewGuid())))!;
        var anonymous = (Task<string>)emitted.CallStatic("Shop.Billing.Probe", "SendAsync", new Answering(DDDToolkit.Abstractions.Access.Caller.Anonymous))!;
        var system = (Task<string>)emitted.CallStatic("Shop.Billing.Probe", "SendAsync", new Answering(DDDToolkit.Abstractions.Access.Caller.System))!;

        (await signedIn).Should().Be("handled");
        (await anonymous).Should().Be("access.not-signed-in");
        (await system).Should().Be("access.not-signed-in", "the application's own work is nobody's sign-in");
    }

    /// <summary>An accessor that answers the caller a test gives it.</summary>
    public sealed class Answering(DDDToolkit.Abstractions.Access.Caller caller) : ICallerAccessor
    {
        /// <inheritdoc />
        public DDDToolkit.Abstractions.Access.Caller Current => caller;
    }
}
