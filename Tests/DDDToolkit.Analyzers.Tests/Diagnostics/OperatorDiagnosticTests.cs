using System.Globalization;
using DDDToolkit.Analyzers.Tests.Generation;
using DDDToolkit.Supporting.Tenancy.Analyzers;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// What a request marked <c>[Operator]</c> may carry beside it, as Tenancy's analyzer reads it: one tenant for the work of a
/// command (DDD00103), no requirement an operator never meets (DDD00104), and nothing about work on a query, which begins
/// none (DDD00105).
/// </summary>
public class OperatorDiagnosticTests
{
    private static GeneratorRunOutcome Run(string requests, bool mediator = true)
        => RequirementAttributeGenerationTests.Module(requests, mediator).WithAnalyzers(new OperatorRequestAnalyzer()).RunCore();

    private static string Message(GeneratorRunOutcome result, string id, string at)
        => result.ShouldHaveDiagnostic(id, at).GetMessage(CultureInfo.InvariantCulture);

    [Fact]
    public void An_operators_command_that_names_one_tenant_or_none_is_not_reported()
    {
        var result = Run(
            """
            [Operator] public sealed record SuspendLedgers([ForTenant] TenantId Tenant, string Reason) : Mediator.ICommand, IBillingRequest;
            [Operator(Tenant = OperatorTenant.FromRequest)] public sealed record CloseLedgers : Mediator.ICommand, IBillingRequest;
            [Operator(Scope = "billing")] public sealed record OpenLedgers : Mediator.ICommand, IBillingRequest;
            [Operator] public sealed record AllLedgers([ForTenant] TenantId Tenant) : Mediator.IQuery<int>, IBillingRequest;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void FromRequest_beside_a_ForTenant_member_is_DDD00103()
    {
        var result = Run("[Operator(Tenant = OperatorTenant.FromRequest)] public sealed record SuspendLedgers([ForTenant] TenantId Tenant) : Mediator.ICommand, IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00103");
        Message(result, "DDD00103", at: "ForTenant").Should()
            .StartWith("'SuspendLedgers' carries [Operator(Tenant = OperatorTenant.FromRequest)] and marks 'Tenant' [ForTenant], so its work would act in the tenant the request names and in the one the command names");
        result.ShouldHaveDiagnostic("DDD00103", at: "ForTenant").Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("[Operator, InTenant]", "[InTenant]")]
    [InlineData("[Operator, RequiresPermission(\"billing.close\")]", "[RequiresPermission]")]
    public void A_requirement_no_operator_meets_beside_Operator_is_DDD00104(string attributes, string named)
    {
        var result = Run(attributes + " public sealed record CloseLedgers : Mediator.ICommand, IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00104");
        Message(result, "DDD00104", at: "Operator").Should()
            .StartWith("'CloseLedgers' carries [Operator] beside " + named + ", which no operator meets");
        result.ShouldHaveDiagnostic("DDD00104", at: "Operator").Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("Tenant = OperatorTenant.FromRequest", "Mediator.IQuery<int>")]
    [InlineData("Scope = \"billing\"", "Mediator.IStreamQuery<int>")]
    public void What_Operator_says_of_work_on_a_query_is_DDD00105(string arguments, string query)
    {
        var result = Run("[Operator(" + arguments + ")] public sealed record AllLedgers : " + query + ", IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00105");
        Message(result, "DDD00105", at: "Operator(" + arguments + ")").Should()
            .Be("'AllLedgers' is a query and carries [Operator(" + arguments + ")], but nothing begins for a query, so " + arguments
                + " says nothing: take it off, since an operator's query reads across tenants through the database role of the operator's token");
        result.ShouldHaveDiagnostic("DDD00105", at: "Operator(" + arguments + ")").Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Where_a_query_cannot_be_told_nothing_is_said_of_its_work()
    {
        var result = Run("[Operator(Tenant = OperatorTenant.FromRequest)] public sealed record AllLedgers : IBillingRequest;", mediator: false);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }
}
