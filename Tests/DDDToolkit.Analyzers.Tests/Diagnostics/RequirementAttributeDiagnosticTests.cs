using System.Globalization;
using DDDToolkit.Analyzers.Tests.Generation;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00077 to DDD00083: what a request says it requires with attributes, held to saying one thing, in one way, where
/// the generator can write it, with its markers in their place and on the ids they mark. Each is an error, because a
/// request that reads as if it said what it requires and says something else, or nothing, is a door a review would
/// take for closed. A request that says nothing at all stays an error too.
/// </summary>
public class RequirementAttributeDiagnosticTests
{
    private static GeneratorRunOutcome Run(string requests, bool mediator = false) => RequirementAttributeGenerationTests.Module(requests, mediator).RunCore();

    private static string Message(GeneratorRunOutcome result, string id, string at)
        => result.ShouldHaveDiagnostic(id, at).GetMessage(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ DDD00077: a request says what it requires

    [Fact]
    public void A_request_that_says_nothing_of_what_it_requires_is_DDD00077_where_the_interface_would_answer_for_it()
    {
        var result = Run("public sealed record CloseInvoice(int Number) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00077");
        Message(result, "DDD00077", at: "CloseInvoice").Should()
            .Contain("'CloseInvoice' implements 'IBillingRequest' and says nothing of what it requires of its caller")
            .And.Contain("[AllowAnonymousCallers] where anyone may send it");
        result.ShouldHaveDiagnostic("DDD00077", at: "CloseInvoice").Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public void An_abstract_request_is_not_DDD00077_and_what_derives_from_it_is()
    {
        var result = Run(
            """
            public abstract record Report : IBillingRequest;
            public sealed record MonthlyReport(int Month) : Report;
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00077");
        result.ShouldHaveDiagnostic("DDD00077", at: "MonthlyReport");
    }

    [Fact]
    public void Where_the_interface_is_not_partial_the_compiler_asks_for_the_property_itself()
    {
        var result = GeneratorTestHost.Create(RequirementAttributeGenerationTests.Billing.Replace("public partial interface", "public interface", StringComparison.Ordinal))
            .WithSource("namespace Shop.Billing;\n\npublic sealed record CloseInvoice(int Number) : IBillingRequest;", "Requests.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics();
        result.CompilationErrors.Select(error => error.Id).Should().Equal(["CS0535"], "a forgotten requirement is an error either way, the compiler's here");
    }

    [Fact]
    public void A_request_of_another_project_than_its_interface_is_DDD00077_and_its_attributes_DDD00083()
    {
        var result = GeneratorTestHost.Create(RequirementAttributeGenerationTests.Usings
                + """
                  public sealed record Forgotten(int Number) : IBillingRequest;

                  [SignedIn] public sealed record Attributed : IBillingRequest;
                  """, "Requests.cs")
            .WithTenancy()
            .WithMembershipAlone()
            .WithReferencedAssembly(RequirementAttributeGenerationTests.Billing + "\n\n[DDDToolkit.Access.SignedIn] public sealed record Own : IBillingRequest;", "Shop.Billing")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00077", "DDD00083");
        Message(result, "DDD00077", at: "Forgotten").Should().Contain("is declared in 'Shop.Billing'").And.Contain("knows only the requests of that project");
        Message(result, "DDD00083", at: "Attributed").Should().Contain("its module's interface 'IBillingRequest' is declared in 'Shop.Billing'");
    }

    [Fact]
    public void A_request_in_parts_that_says_nothing_is_DDD00077_whichever_part_implements_the_interface()
    {
        // The first part is bare, which is not where the request is read: the second, with the interface, is.
        var result = GeneratorTestHost.Create(RequirementAttributeGenerationTests.Billing, "Billing.cs")
            .WithSource("namespace Shop.Billing;\n\npublic partial record SplitForgotten;", "First.cs")
            .WithSource("namespace Shop.Billing;\n\npublic partial record SplitForgotten : IBillingRequest;", "Second.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00077");
        Message(result, "DDD00077", at: "SplitForgotten").Should().Contain("'SplitForgotten' implements 'IBillingRequest' and says nothing of what it requires");
    }

    [Fact]
    public void A_request_an_interface_between_it_and_the_module_s_answers_is_not_DDD00077()
    {
        var result = Run(
            """
            public interface IPublicBillingRequest : IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.AllowAnonymous();
            }

            public sealed record ListPlans : IPublicBillingRequest;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    // ------------------------------------------------------------------ DDD00078: in one way

    [Fact]
    public void An_attribute_and_the_property_on_one_request_is_DDD00078()
    {
        var result = Run(
            """
            [SignedIn]
            public sealed record MyInvoices : IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.AllowAnonymous();
            }
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00078");
        Message(result, "DDD00078", at: "MyInvoices").Should().Contain("carries [SignedIn] and answers RequiredAccess as well, and the property wins over the attribute");
    }

    [Fact]
    public void An_attribute_on_a_request_an_interface_between_it_and_the_module_s_answers_is_DDD00078()
    {
        // C# takes the sub-interface's default over the module's line, so the attribute would say what nothing reads:
        // here a door the key closes would stand open to anyone.
        var result = Run(
            """
            public interface IPublicBillingRequest : IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.AllowAnonymous();
            }

            [RequiresKey("billing.close")]
            public sealed record CloseTheBooks(int Year) : IPublicBillingRequest;
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00078");
        Message(result, "DDD00078", at: "CloseTheBooks").Should()
            .Contain("carries [RequiresKey] and answers RequiredAccess as well, through 'IPublicBillingRequest', and the property wins over the attribute");
        result.ShouldNotContain("BillingAccessBehavior.", "CloseTheBooks", "no line is written that nothing would read");
    }

    // ------------------------------------------------------------------ DDD00079: one requirement attribute

    [Theory]
    [InlineData("[AllowAnonymousCallers, RequiresKey(\"billing.close\")]", "[AllowAnonymousCallers] and [RequiresKey]")]
    [InlineData("[InTenant, RequiresKey(\"billing.close\")]", "[InTenant] and [RequiresKey]")]
    [InlineData("[SignedIn, InTenant]", "[SignedIn] and [InTenant]")]
    public void Two_requirement_attributes_on_one_request_is_DDD00079(string attributes, string named)
    {
        var result = Run(attributes + " public sealed record CloseTheBooks(int Year) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00079");
        Message(result, "DDD00079", at: "CloseTheBooks").Should().Contain("carries " + named + ": a request requires one thing of its caller");
        result.ShouldNotContain("BillingAccessBehavior.", "CloseTheBooks", "no line is written for a request that says two things");
    }

    // ------------------------------------------------------------------ DDD00080: a marker in its place

    [Fact]
    public void A_marker_without_RequiresKey_is_DDD00080()
    {
        var result = Run("[InTenant] public sealed record OpenLedger([AtUnit] OrganizationUnitId Unit) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00080");
        Message(result, "DDD00080", at: "AtUnit").Should().Contain("marks 'Unit' [AtUnit], and requires no key");
    }

    [Fact]
    public void An_abstract_shape_that_marks_the_unit_is_not_DDD00080_and_a_request_that_inherits_it_without_a_key_is()
    {
        // The shape is shared, and each request that derives from it says its key: the one that does not is reported,
        // where it is declared, since the marker it inherits is the shape's.
        var result = Run(
            """
            public abstract record UnitCommand([property: AtUnit] OrganizationUnitId Unit) : IBillingRequest;

            [RequiresKey("billing.open-ledger")]
            public sealed record OpenLedger(OrganizationUnitId Unit) : UnitCommand(Unit);

            [SignedIn]
            public sealed record PeekLedger(OrganizationUnitId Unit) : UnitCommand(Unit);
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00080");
        Message(result, "DDD00080", at: "PeekLedger").Should().Contain("'PeekLedger' inherits 'Unit' [AtUnit] from 'UnitCommand', and requires no key");
    }

    [Fact]
    public void A_request_that_takes_its_base_s_line_is_not_DDD00080_for_the_marker_that_line_reads()
    {
        var result = Run(
            """
            [RequiresKey("billing.open-ledger")]
            public abstract record LedgerCommand : IBillingRequest
            {
                [AtUnit] public required OrganizationUnitId Unit { get; init; }
            }

            public sealed record OpenLedger : LedgerCommand;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Theory]
    [InlineData("[AtUnit] OrganizationUnitId Unit, [OnResource] DocumentId Document", "'Unit' [AtUnit] and 'Document' [OnResource]")]
    [InlineData("[AtUnit] OrganizationUnitId From, [AtUnit] OrganizationUnitId To", "'From' [AtUnit] and 'To' [AtUnit]")]
    public void Two_markers_of_where_the_key_is_held_is_DDD00080(string parameters, string named)
    {
        var result = Run("[RequiresKey(\"billing.move\")] public sealed record MoveLedger(" + parameters + ") : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00080");
        result.ReportedDiagnostics.Single().GetMessage(CultureInfo.InvariantCulture).Should().Contain("marks where its key is held more than once, " + named);
    }

    [Theory]
    [InlineData("[AtUnit] OrganizationUnitId Unit, [ExpectedVersion] long? ExpectedVersion = null", "beside [AtUnit]")]
    [InlineData("int Number, [ExpectedVersion] long? ExpectedVersion = null", "without a marker")]
    public void The_version_without_a_resource_is_DDD00080(string parameters, string where)
    {
        var result = Run("[RequiresKey(\"billing.close\")] public sealed record CloseLedger(" + parameters + ") : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00080");
        Message(result, "DDD00080", at: "ExpectedVersion").Should()
            .Contain("marks 'ExpectedVersion' [ExpectedVersion], which goes with [OnResource], and marks no member [OnResource]", where);
    }

    [Fact]
    public void The_version_marked_twice_is_DDD00080()
    {
        var result = Run("[RequiresKey(\"documents.share\")] public sealed record ShareDocument([OnResource] DocumentId Document, [ExpectedVersion] long? Read, [ExpectedVersion] long? Seen) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00080");
        result.ReportedDiagnostics.Single().GetMessage(CultureInfo.InvariantCulture).Should().Contain("marks 'Read' and 'Seen' [ExpectedVersion]: the call takes it once");
    }

    [Fact]
    public void A_marker_on_a_parameter_of_a_class_s_constructor_is_DDD00080()
    {
        var result = Run("[RequiresKey(\"billing.open\")] public sealed class OpenLedger([AtUnit] OrganizationUnitId unit) : IBillingRequest { public OrganizationUnitId Unit { get; } = unit; }");

        result.ShouldHaveExactlyDiagnostics("DDD00080");
        Message(result, "DDD00080", at: "AtUnit").Should().Contain("marks the parameter 'unit' of its constructor [AtUnit], which is no property the requirement can read");
    }

    [Fact]
    public void A_marker_on_a_property_the_interface_cannot_read_is_DDD00080()
    {
        var result = Run("[RequiresKey(\"billing.open\")] public sealed class OpenLedger : IBillingRequest { [AtUnit] private OrganizationUnitId Unit { get; } }");

        result.ShouldHaveExactlyDiagnostics("DDD00080");
        Message(result, "DDD00080", at: "AtUnit").Should().Contain("which 'IBillingRequest', where the requirement is written, cannot read");
    }

    [Fact]
    public void A_key_without_a_marker_where_no_package_says_where_it_is_held_is_DDD00080()
    {
        // Membership alone: a resource with members, and no tenant to hold a key for the whole of.
        var result = GeneratorTestHost.Create(RequirementAttributeGenerationTests.Billing, "Billing.cs")
            .WithMembershipAlone()
            .WithSource(RequirementAttributeGenerationTests.Ids, "Ids.cs")
            .WithSource(
                """
                using DDDToolkit.Access;
                using DDDToolkit.Supporting.Membership.Access;

                namespace Shop.Billing;

                [RequiresKey("documents.read")] public sealed record ReadAll : IBillingRequest;

                [RequiresKey("documents.read")] public sealed record ReadOne([OnResource] DocumentId Document) : IBillingRequest;
                """,
                "Requests.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00080");
        Message(result, "DDD00080", at: "ReadAll").Should()
            .Contain("requires a key and marks no member of where it is held, and no package this project references says where a key without a marker is held")
            .And.Contain("[OnResource]");
    }

    // ------------------------------------------------------------------ DDD00081: the id it marks

    [Fact]
    public void A_unit_marked_on_another_id_is_DDD00081_where_the_unit_class_is_seen()
    {
        var result = RequirementAttributeGenerationTests.OverTheDomain("[RequiresKey(\"billing.open\")] public sealed record OpenLedger([AtUnit] SeatId Unit) : IBillingRequest;").RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00081");
        Message(result, "DDD00081", at: "AtUnit").Should()
            .Be("'OpenLedger' marks 'Unit' [AtUnit], which is a 'SeatId': it marks 'OrganizationUnitId', the id of the [OrganizationUnit<TUnitId>] class this project sees");
    }

    [Fact]
    public void A_resource_marked_on_another_id_is_DDD00081_where_a_member_class_is_seen()
    {
        var result = RequirementAttributeGenerationTests.OverTheDomain("[RequiresKey(\"documents.share\")] public sealed record ShareDocument([OnResource] SeatId Document) : IBillingRequest;").RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00081");
        Message(result, "DDD00081", at: "OnResource").Should()
            .Contain("which is a 'SeatId': it marks 'DocumentId', the id of the TResource of the [Member<TId, TMemberId, TRoleId, TResource>] class this project sees");
    }

    [Fact]
    public void The_right_ids_over_the_domain_are_not_reported_and_compile()
    {
        var result = RequirementAttributeGenerationTests.OverTheDomain(
            """
            [RequiresKey("billing.open")] public sealed record OpenLedger([AtUnit] OrganizationUnitId Unit) : IBillingRequest;
            [RequiresKey("documents.share")] public sealed record ShareDocument([OnResource] DocumentId Document, [ExpectedVersion] long? ExpectedVersion = null) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void Where_no_unit_class_is_seen_nothing_can_be_told_and_the_check_says_it_when_sent()
    {
        var result = Run("[RequiresKey(\"billing.open\")] public sealed record OpenLedger([AtUnit] SeatId Unit) : IBillingRequest;");

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_unit_that_may_be_null_is_DDD00081_and_asks_for_the_property()
    {
        var result = Run("[RequiresKey(\"billing.read\")] public sealed record ListLedgers([AtUnit] OrganizationUnitId? Unit) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00081");
        Message(result, "DDD00081", at: "AtUnit").Should().Contain("which is 'OrganizationUnitId?' and may be null: a requirement computed from whether it is given is written as RequiredAccess");
    }

    [Fact]
    public void A_version_of_another_type_is_DDD00081()
    {
        var result = Run("[RequiresKey(\"documents.share\")] public sealed record ShareDocument([OnResource] DocumentId Document, [ExpectedVersion] string ExpectedVersion) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00081");
        Message(result, "DDD00081", at: "ExpectedVersion").Should().Contain("which is a 'string': the call takes a 'long?' there");
    }

    // ------------------------------------------------------------------ DDD00082: for queries only

    [Theory]
    [InlineData("[SeenWith<DocumentId>(\"documents.view\")]", "SeenWith<DocumentId>")]
    [InlineData("[RequiresOperator]", "RequiresOperator")]
    public void A_requirement_for_queries_only_on_a_command_is_DDD00082(string attribute, string named)
    {
        var result = Run(attribute + " public sealed record ArchiveAll : Mediator.ICommand, IBillingRequest;", mediator: true);

        result.ShouldHaveExactlyDiagnostics("DDD00082");
        Message(result, "DDD00082", at: "ArchiveAll").Should().Contain("'ArchiveAll' is a command and carries [" + named + "], which its package declares for queries only");
    }

    [Theory]
    [InlineData("[SeenWith<DocumentId>(\"documents.view\")]", "Mediator.IRequest<int>")]
    [InlineData("[SeenWith<DocumentId>(\"documents.view\")]", "Mediator.IRequest")]
    [InlineData("[RequiresOperator]", "Mediator.IRequest<int>")]
    [InlineData("[RequiresOperator]", "Mediator.IStreamRequest<int>")]
    public void A_requirement_for_queries_only_on_a_plain_request_of_Mediator_s_is_DDD00082(string attribute, string message)
    {
        // A plain request may change as much as a command does, and the requirement would hold its caller to nothing.
        var result = Run(attribute + " public sealed record DeleteAll : " + message + ", IBillingRequest;", mediator: true);

        result.ShouldHaveExactlyDiagnostics("DDD00082");
        Message(result, "DDD00082", at: "DeleteAll").Should().Contain("'DeleteAll' is a request of Mediator's that is no query and carries [")
            .And.Contain("make it a query, IQuery or IStreamQuery");
    }

    [Fact]
    public void On_a_query_or_where_a_command_cannot_be_told_it_is_not_reported()
    {
        var withTheLibrary = Run(
            """
            [SeenWith<DocumentId>("documents.view")] public sealed record ListDocuments : Mediator.IQuery<int>, IBillingRequest;
            [RequiresOperator] public sealed record AllLedgers : Mediator.IStreamQuery<int>, IBillingRequest;
            """,
            mediator: true);
        var withoutIt = Run("[SeenWith<DocumentId>(\"documents.view\")] public sealed record ListDocuments : IBillingRequest;");

        withTheLibrary.ShouldCompile();
        withTheLibrary.ShouldHaveExactlyDiagnostics();
        withoutIt.ShouldHaveExactlyDiagnostics();
    }

    // ------------------------------------------------------------------ DDD00083: where they are read

    [Fact]
    public void Attributes_on_a_request_whose_interface_is_not_partial_are_DDD00083_with_the_line_to_write()
    {
        var result = GeneratorTestHost.Create(RequirementAttributeGenerationTests.Billing.Replace("public partial interface", "public interface", StringComparison.Ordinal))
            .WithSource(
                """
                using DDDToolkit.Access;

                namespace Shop.Billing;

                [SignedIn]
                public sealed record MyInvoices : IBillingRequest
                {
                    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.SignedIn();
                }
                """,
                "Requests.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00083", at: "MyInvoices").GetMessage(CultureInfo.InvariantCulture).Should()
            .Contain("'IBillingRequest' is not partial").And.Contain("declare it 'public partial interface IBillingRequest : IRequireAccess;'");
    }

    [Fact]
    public void Attributes_on_a_type_that_is_no_request_are_DDD00083()
    {
        var result = Run("[SignedIn] public sealed record NotSent;");

        result.ShouldHaveExactlyDiagnostics("DDD00083");
        Message(result, "DDD00083", at: "NotSent").Should().Contain("it implements no interface marked [AccessRequests]");
    }

    [Fact]
    public void Attributes_on_a_generic_request_are_DDD00083()
    {
        var result = Run("[SignedIn] public sealed record Page<T> : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00083");
        Message(result, "DDD00083", at: "Page").Should().Contain("it has type parameters");
    }

    [Fact]
    public void Attributes_on_a_request_the_interface_cannot_name_are_DDD00083()
    {
        var result = Run("public static class Outer { [SignedIn] private sealed record Hidden : IBillingRequest; }");

        result.ShouldHaveExactlyDiagnostics("DDD00083");
        Message(result, "DDD00083", at: "Hidden").Should().Contain("'IBillingRequest' cannot name it, nested private as it is in 'Outer'");
    }

    [Fact]
    public void Attributes_on_a_request_of_two_partial_module_interfaces_are_DDD00083()
    {
        // Each interface implements RequiredAccess, and C# takes neither: its CS8705 says what is ambiguous, this what to do.
        var result = Run(
            """
            [AccessRequests] public partial interface IShippingRequest : IRequireAccess;

            [SignedIn] public sealed record Both : IBillingRequest, IShippingRequest;
            """);

        result.ShouldHaveDiagnostic("DDD00083", at: "Both").GetMessage(CultureInfo.InvariantCulture).Should()
            .Contain("it implements 'IBillingRequest' and 'IShippingRequest', each partial with RequiredAccess written into it")
            .And.Contain("answer RequiredAccess itself, or implement one of them");
        result.ShouldNotContain("BillingAccessBehavior.", "Both", "no line is written that C# could not choose");
    }
}
