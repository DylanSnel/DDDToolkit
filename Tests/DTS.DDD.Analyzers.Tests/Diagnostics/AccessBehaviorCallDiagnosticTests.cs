using DTS.DDD.Analyzers.Tests.Generation;

namespace DTS.DDD.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00107: a call to the registration of an access behavior that the compiler cannot find. The compiler's CS1061 names
/// no cause, so the diagnostic names it at the same call. A project that writes no such call reports nothing, also when
/// it has no behavior.
/// </summary>
public class AccessBehaviorCallDiagnosticTests
{
    private const string Requests =
        """
        using DTS.DDD.Abstractions.Attributes;
        using DTS.DDD.Access;

        namespace Shop.Billing.Access;

        [AccessRequests]
        public partial interface IBillingRequest : IRequireAccess;
        """;

    /// <summary>A registration class one namespace up from the interface, with the usings it starts with in <paramref name="usings"/>.</summary>
    private static string CallFrom(string namespaceName, string usings = "", string called = "AddBillingAccessBehavior")
        => usings + "\n" +
           $$"""
           using Microsoft.Extensions.DependencyInjection;

           namespace {{namespaceName}};

           public static class BillingApplication
           {
               public static IServiceCollection AddBillingApplication(this IServiceCollection services)
                   => services.{{called}}();
           }
           """;

    private static GeneratorTestHost Project(string requests = Requests)
        => GeneratorTestHost.Create(requests, "Requests.cs").WithAnalyzers(GeneratorTestHost.CoreAnalyzers());

    private static void ShouldBeCs1061(GeneratorRunOutcome result)
        => result.CompilationErrors.Select(error => error.Id).Should().Contain("CS1061", "the compiler still reports the call");

    // ------------------------------------------------------------------ the library is missing

    [Fact]
    public void A_project_without_the_library_reports_DDD00107_naming_the_package_and_the_other_way()
    {
        var result = Project().WithSource(CallFrom("Shop.Billing"), "Application.cs").WithDependencyInjection().RunCore();

        ShouldBeCs1061(result);
        result.ShouldNotCrash();
        result.ShouldHaveExactlyDiagnostics("DDD00107");
        result.ShouldHaveDiagnostic("DDD00107", at: "AddBillingAccessBehavior").GetMessage().Should()
            .StartWith("'AddBillingAccessBehavior', the registration of the access behavior of 'IBillingRequest', cannot be called here.")
            .And.Contain("Add the package Mediator.Abstractions to '" + GeneratorTestHost.DefaultAssemblyName + "'")
            .And.Contain("AccessChecks<IBillingRequest>.RequireAsync");
    }

    [Fact]
    public void A_project_without_the_library_that_asks_the_checks_itself_reports_nothing()
    {
        var result = Project().WithSource(
                """
                using DTS.DDD.Abstractions.Attributes;
                using System.Threading;
                using System.Threading.Tasks;
                using DTS.DDD.Access;
                using Microsoft.Extensions.DependencyInjection;
                using Shop.Billing.Access;

                namespace Shop.Billing;

                [SignedIn]
                public sealed record CloseInvoice(int Invoice) : IBillingRequest;

                public sealed class Invoices(AccessChecks<IBillingRequest> checks)
                {
                    public async Task CloseAsync(CloseInvoice command, CancellationToken cancellationToken)
                    {
                        await checks.RequireAsync(command, cancellationToken);
                        using (checks.BeginWork(command, isQuery: false))
                        {
                            _ = ((IRequireAccess)command).RequiredAccess;
                        }
                    }
                }

                public static class BillingApplication
                {
                    public static IServiceCollection AddBillingApplication(this IServiceCollection services)
                        => services.AddAccessChecks<IBillingRequest>();
                }
                """,
                "Application.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void An_interface_in_a_referenced_project_without_the_library_names_that_project()
    {
        var result = GeneratorTestHost.Create(CallFrom("Shop.Host"), "Application.cs")
            .WithDependencyInjection()
            .WithReferencedAssembly(Requests, "Shop.Billing.Application")
            .WithMediator()
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

        ShouldBeCs1061(result);
        result.ShouldHaveExactlyDiagnostics("DDD00107");
        result.ShouldHaveDiagnostic("DDD00107", at: "AddBillingAccessBehavior").GetMessage().Should()
            .Contain("Add the package Mediator.Abstractions to 'Shop.Billing.Application'");
    }

    [Fact]
    public void A_referenced_project_with_a_behavior_and_no_registration_is_not_told_to_add_the_library()
    {
        var result = GeneratorTestHost.Create(CallFrom("Shop.Host", "using Shop.Billing.Access;"), "Application.cs")
            .WithMediatorAlone()
            .WithReferencedAssembly(Requests, "Shop.Billing.Application")
            .WithDependencyInjection()
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

        ShouldBeCs1061(result);
        result.ShouldHaveExactlyDiagnostics();
    }

    // ------------------------------------------------------------------ the namespace is not imported

    [Fact]
    public void A_registration_class_one_namespace_up_reports_DDD00107_naming_the_using()
    {
        var result = Project().WithSource(CallFrom("Shop.Billing"), "Application.cs").WithMediator().RunCore();

        ShouldBeCs1061(result);
        result.ShouldNotCrash();
        result.ShouldHaveGenerated("BillingAccessBehavior.");
        result.ShouldHaveExactlyDiagnostics("DDD00107");
        result.ShouldHaveDiagnostic("DDD00107", at: "AddBillingAccessBehavior").GetMessage().Should()
            .Contain("declared in the namespace 'Shop.Billing.Access'")
            .And.Contain("Add 'using Shop.Billing.Access;'");
    }

    [Fact]
    public void An_interface_in_a_referenced_project_reports_DDD00107_naming_the_using()
    {
        var result = GeneratorTestHost.Create(CallFrom("Shop.Host"), "Application.cs")
            .WithMediator()
            .WithReferencedAssembly(Requests, "Shop.Billing.Application")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

        ShouldBeCs1061(result);
        result.ShouldHaveExactlyDiagnostics("DDD00107");
        result.ShouldHaveDiagnostic("DDD00107", at: "AddBillingAccessBehavior").GetMessage().Should().Contain("Add 'using Shop.Billing.Access;'");
    }

    [Theory]
    [InlineData("Shop.Billing", "using Shop.Billing.Access;")]
    [InlineData("Shop.Billing", "global using Shop.Billing.Access;")]
    [InlineData("Shop.Billing.Access", "")]
    [InlineData("Shop.Billing.Access.Handlers", "")]
    public void A_call_that_sees_the_namespace_compiles_and_reports_nothing(string namespaceName, string usings)
    {
        var result = Project().WithSource(CallFrom(namespaceName, usings), "Application.cs").WithMediator().RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void Two_interfaces_that_give_one_call_in_two_namespaces_each_name_their_using()
    {
        var result = Project()
            .WithSource(Requests.Replace("Shop.Billing.Access", "Shop.Payroll.Access", StringComparison.Ordinal), "Payroll.cs")
            .WithSource(CallFrom("Shop.Billing"), "Application.cs")
            .WithMediator()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00107", "DDD00107");
        result.ReportedDiagnostics.Select(diagnostic => diagnostic.GetMessage()).Should()
            .ContainSingle(message => message.Contains("using Shop.Billing.Access;", StringComparison.Ordinal))
            .And.ContainSingle(message => message.Contains("using Shop.Payroll.Access;", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the name the generator gives

    [Theory]
    [InlineData("IBillingRequest")]
    [InlineData("IBillingRequests")]
    [InlineData("IBilling")]
    public void The_call_is_matched_to_the_interface_by_the_name_the_generator_gives(string interfaceName)
    {
        var result = Project(Requests.Replace("IBillingRequest", interfaceName, StringComparison.Ordinal))
            .WithSource(CallFrom("Shop.Billing"), "Application.cs")
            .WithMediator()
            .RunCore();

        result.ShouldHaveGenerated("BillingAccessBehavior.");
        result.ShouldHaveExactlyDiagnostics("DDD00107");
        result.ShouldHaveDiagnostic("DDD00107", at: "AddBillingAccessBehavior").GetMessage().Should().Contain("'" + interfaceName + "'");
    }

    // ------------------------------------------------------------------ what it leaves alone

    [Fact]
    public void A_call_that_no_marked_interface_gives_is_left_to_the_compiler()
    {
        var result = Project().WithSource(CallFrom("Shop.Billing", called: "AddShippingAccessBehavior"), "Application.cs").WithMediator().RunCore();

        ShouldBeCs1061(result);
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_call_on_something_other_than_the_service_collection_is_left_to_the_compiler()
    {
        var result = Project().WithSource(
                """
                namespace Shop.Billing;

                public static class Other
                {
                    public static object Call(object thing) => thing.AddBillingAccessBehavior();
                }
                """,
                "Other.cs")
            .WithDependencyInjection()
            .RunCore();

        ShouldBeCs1061(result);
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void An_internal_interface_of_a_referenced_project_is_left_to_the_compiler()
    {
        var result = GeneratorTestHost.Create(CallFrom("Shop.Host"), "Application.cs")
            .WithMediator()
            .WithReferencedAssembly(Requests.Replace("public partial", "internal partial", StringComparison.Ordinal), "Shop.Billing.Application")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

        ShouldBeCs1061(result);
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_misshapen_interface_with_the_library_is_said_by_DDD00056_alone()
    {
        var result = Project(Requests.Replace("public partial interface IBillingRequest : IRequireAccess;", "public interface IBillingRequest;", StringComparison.Ordinal))
            .WithSource(CallFrom("Shop.Billing"), "Application.cs")
            .WithMediator()
            .RunCore();

        ShouldBeCs1061(result);
        result.ShouldHaveExactlyDiagnostics("DDD00056");
    }

    [Fact]
    public void A_library_of_an_unknown_shape_is_said_by_DDD00057_alone()
    {
        var result = Project()
            .WithSource(CallFrom("Shop.Billing"), "Application.cs")
            .WithDependencyInjection()
            .WithReferencedAssembly(AccessBehaviorGenerationTests.LibraryDeclaring(handle: "void Handle();"), "Mediator.Abstractions")
            .RunCore();

        ShouldBeCs1061(result);
        result.ShouldHaveExactlyDiagnostics("DDD00057");
    }
}
