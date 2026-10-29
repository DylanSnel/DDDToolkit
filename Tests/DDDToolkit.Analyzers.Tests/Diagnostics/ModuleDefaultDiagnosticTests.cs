using System.Globalization;
using DDDToolkit.Analyzers.Tests.Generation;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// The default of a module's requests, the requirement attributes on its interface: DDD00089 for one that may not be a
/// default, since every request that forgets its own takes it; DDD00083 where nothing reads it; DDD00080 for a permission no
/// package says where it is held without a marker; and DDD00084 for a request's own attributes that say what the
/// default says. A request of a module with a default is no DDD00077: it takes the default, on a line of its own.
/// </summary>
public class ModuleDefaultDiagnosticTests
{
    private static GeneratorRunOutcome Run(string defaults, string requests, bool mediator = false) => ModuleDefaultGenerationTests.Module(defaults, requests, mediator).RunCore();

    private static string Message(GeneratorRunOutcome result, string id, string at)
        => result.ShouldHaveDiagnostic(id, at).GetMessage(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ DDD00077: not where the module has a default

    [Fact]
    public void A_request_that_states_nothing_is_no_DDD00077_where_its_module_has_a_default()
    {
        var result = Run("InTenant", "public sealed record CloseInvoice(int Number) : IBillingRequest;");

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_request_of_another_project_that_states_nothing_is_no_DDD00077_where_the_interface_there_has_a_default()
    {
        // The interface's last arm, written where it is declared, answers the default for a request it cannot name.
        var result = GeneratorTestHost.Create(RequirementAttributeGenerationTests.Usings + "public sealed record Forgotten(int Number) : IBillingRequest;", "Requests.cs")
            .WithTenancy()
            .WithMembershipAlone()
            .WithReferencedAssembly(ModuleDefaultGenerationTests.Billing("InTenant"), "Shop.Billing")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    // ------------------------------------------------------------------ DDD00089: what may be a default

    [Fact]
    public void AllowAnonymous_as_the_default_is_DDD00089_since_a_forgotten_requirement_never_opens_a_door()
    {
        var result = Run("AllowAnonymous", "public sealed record CloseInvoice(int Number) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00089");
        Message(result, "DDD00089", at: "AllowAnonymous").Should()
            .Be("'IBillingRequest' carries [AllowAnonymous] as the default of its module's requests: a request of the module that forgets to say what it requires would let anyone send it, "
                + "and a forgotten requirement never opens a door; put [AllowAnonymous] on each request anyone may send");
        result.ShouldHaveDiagnostic("DDD00089", at: "AllowAnonymous").Severity.Should().Be(DiagnosticSeverity.Error);
        result.CompilationErrors.Select(error => error.Id).Should().Contain("CS0592", "its class is for a request alone, so the compiler refuses it on an interface as well");
        result.ShouldContain("BillingAccessBehavior.", "declares no access requirement", "a refused default is no default: the last arm stops what states nothing");
    }

    [Theory]
    [InlineData("SeenWith<DocumentId>(\"documents.view\")", "SeenWith<DocumentId>(\"documents.view\")", "[SeenWith<DocumentId>]")]
    public void A_requirement_for_queries_only_as_the_default_is_DDD00089(string defaults, string at, string named)
    {
        var result = Run(defaults, "public sealed record ArchiveAll : Mediator.ICommand, IBillingRequest;", mediator: true);

        result.ShouldHaveExactlyDiagnostics("DDD00089");
        Message(result, "DDD00089", at: at).Should()
            .Contain("carries " + named + " as the default of its module's requests: its package declares it for queries only, and a command of the module that says nothing would take it too")
            .And.Contain("put " + named + " on each query");
    }

    [Fact]
    public void Operator_as_the_default_is_DDD00089_beside_the_compiler_s_CS0592()
    {
        // [Operator] goes on a request alone: what its command's work acts in is said per request.
        var result = Run("Operator", "public sealed record ArchiveAll : Mediator.ICommand, IBillingRequest;", mediator: true);

        result.ShouldHaveExactlyDiagnostics("DDD00089");
        Message(result, "DDD00089", at: "Operator").Should().Contain("carries [Operator] as the default of its module's requests: the AttributeUsage of its class leaves out interfaces (CS0592)");
        result.CompilationErrors.Select(error => error.Id).Should().Contain("CS0592");
    }

    [Fact]
    public void OwnOnly_as_the_default_is_DDD00089_since_a_default_reads_no_member()
    {
        var result = Run("OwnOnly", "public sealed record LeaveTenant(SeatId Seat) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00089");
        Message(result, "DDD00089", at: "OwnOnly").Should().Contain("it reads the member of a request marked [Own], and a default reads no member of a request");
    }

    [Fact]
    public void A_module_s_own_attribute_whose_class_leaves_out_interfaces_is_DDD00089_beside_the_compiler_s_CS0592()
    {
        var result = Run("RequiresPlan(Plan.Pro)", RequirementCombinationGenerationTests.Plans + "public sealed record ExportBoard : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00089");
        Message(result, "DDD00089", at: "RequiresPlan(Plan.Pro)").Should()
            .Contain("the AttributeUsage of its class leaves out interfaces (CS0592), and a requirement attribute is a default where its class allows AttributeTargets.Interface");
        result.CompilationErrors.Select(error => error.Id).Should().Contain("CS0592");
    }

    [Fact]
    public void A_module_s_own_attribute_for_queries_only_is_DDD00089_where_its_class_allows_an_interface()
    {
        // The compiler takes it on the interface, so only the generator says why it may not be a default there.
        var result = Run(
            "RequiresPlan(Plan.Pro)",
            RequirementCombinationGenerationTests.Plans
                .Replace("AttributeTargets.Class | AttributeTargets.Struct", "AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface", StringComparison.Ordinal)
                .Replace("nameof(BillingAccess.RequiresPlan))]", "nameof(BillingAccess.RequiresPlan), QueriesOnly = true)]", StringComparison.Ordinal)
            + "public sealed record ExportBoard : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00089");
        result.CompilationErrors.Should().BeEmpty();
        Message(result, "DDD00089", at: "RequiresPlan(Plan.Pro)").Should().Contain("its package declares it for queries only");
    }

    // ------------------------------------------------------------------ DDD00080: a permission without a marker

    [Fact]
    public void A_permission_as_the_default_where_no_package_says_where_it_is_held_is_DDD00080()
    {
        var result = GeneratorTestHost.Create(ModuleDefaultGenerationTests.Billing("RequiresPermission(\"documents.read\")").Replace("using DDDToolkit.Supporting.Tenancy.Access;", string.Empty, StringComparison.Ordinal), "Billing.cs")
            .WithMembershipAlone()
            .WithSource("namespace Shop.Billing;\n\npublic sealed record ReadAll : IBillingRequest;", "Requests.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00080");
        Message(result, "DDD00080", at: "IBillingRequest").Should()
            .Contain("'IBillingRequest' requires a permission as the default of its module, and no package this project references says where a permission without a marker is held")
            .And.Contain("a default reads no member of a request");
    }

    [Fact]
    public void A_marker_without_a_permission_of_its_own_is_DDD00080_beside_a_default_with_a_permission_too()
    {
        var result = Run("RequiresPermission(\"billing.read\")", "public sealed record OpenLedger([AtUnit] OrganizationUnitId Unit) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00080");
        Message(result, "DDD00080", at: "AtUnit").Should()
            .Contain("marks 'Unit' [AtUnit], and requires no permission")
            .And.Contain("the permission of its module's default, on 'IBillingRequest', reads no member of a request");
    }

    // ------------------------------------------------------------------ DDD00083: where a default is read

    [Fact]
    public void A_default_on_an_interface_that_is_not_partial_is_DDD00083_with_the_line_to_write()
    {
        var result = GeneratorTestHost.Create(ModuleDefaultGenerationTests.Billing("InTenant").Replace("public partial interface", "public interface", StringComparison.Ordinal), "Billing.cs")
            .WithTenancy()
            .WithSource(
                """
                using DDDToolkit.Access;

                namespace Shop.Billing;

                public sealed record ListUnits : IBillingRequest
                {
                    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.SignedIn();
                }
                """,
                "Requests.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00083");
        Message(result, "DDD00083", at: "IBillingRequest").Should()
            .Contain("'IBillingRequest' carries [InTenant], which nothing turns into what it requires: 'IBillingRequest' is not partial")
            .And.Contain("declare it 'public partial interface IBillingRequest : IRequireAccess;'");
    }

    [Fact]
    public void A_requirement_attribute_on_an_interface_that_is_not_the_module_s_is_DDD00083()
    {
        // Only the interface marked [AccessRequests] has a default; one between it and a request answers the property itself.
        var result = Run(
            "SignedIn",
            """
            [InTenant] public interface ISeatedBillingRequest : IBillingRequest;

            public sealed record ListUnits : ISeatedBillingRequest;
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00083");
        Message(result, "DDD00083", at: "ISeatedBillingRequest").Should()
            .Contain("'ISeatedBillingRequest' carries [InTenant], which nothing turns into what it requires: a requirement attribute on an interface is the default of a module's requests")
            .And.Contain("'IBillingRequest' here")
            .And.Contain("or answer RequiredAccess in 'ISeatedBillingRequest'");
    }

    // ------------------------------------------------------------------ DDD00084: what the default says already

    [Fact]
    public void A_request_s_own_attribute_that_says_what_the_default_says_is_DDD00084_a_warning()
    {
        var result = Run(
            "InTenant",
            """
            [InTenant] public sealed record ListUnits : IBillingRequest;

            [RequiresPermission("billing.read")] public sealed record ListLedgers : IBillingRequest;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00084");
        Message(result, "DDD00084", at: "InTenant").Should()
            .Be("'ListUnits' carries [InTenant] beside the default of its module, [InTenant] on 'IBillingRequest', which requires it already: [InTenant] adds nothing to what the request requires, so take it off");
        result.ShouldHaveDiagnostic("DDD00084", at: "InTenant").Severity.Should().Be(DiagnosticSeverity.Warning);
        result.ShouldContain("BillingAccessBehavior.", "global::Shop.Billing.ListUnits => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.InTenant(),", "a warning leaves the line as it is");
    }

    [Fact]
    public void Several_own_attributes_that_say_what_the_default_says_are_DDD00084_once()
    {
        var result = Run("SignedIn, InTenant", "[SignedIn, InTenant] public sealed record ListUnits : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00084");
        Message(result, "DDD00084", at: "SignedIn").Should().Contain("'ListUnits' carries [SignedIn] and [InTenant] beside the default of its module, [SignedIn] and [InTenant] on 'IBillingRequest'");
    }

    [Theory]
    [InlineData("[SignedIn, InTenant] public sealed record ListUnits : IBillingRequest;", "it says more than the default, a signed-in user in the tenant")]
    [InlineData("[InTenant] public record ListUnits : IBillingRequest;", "a request that derives from it holds to its attribute beside its own, and would require less without it")]
    [InlineData("[InTenant] public abstract record Listing : IBillingRequest; [SignedIn] public sealed record ListUnits : Listing;", "the same: an abstract request is a base")]
    public void A_request_s_own_attribute_that_does_not_say_just_what_the_default_says_is_no_DDD00084(string requests, string because)
    {
        var result = Run("InTenant", requests);

        result.ShouldCompile();
        result.Count("DDD00084").Should().Be(0, because);
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void An_attribute_of_the_default_that_another_there_requires_already_is_DDD00084()
    {
        var result = Run("InTenant, RequiresPermission(\"billing.read\")", "public sealed record ListLedgers : IBillingRequest;");

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00084");
        Message(result, "DDD00084", at: "InTenant").Should().Contain("'IBillingRequest' carries [InTenant] beside [RequiresPermission], which requires it already");
    }
}
