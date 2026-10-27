using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A requirement attribute on a module's interface, <c>[AccessRequests, InTenant]</c>, is the module's default: a
/// request of the module that states nothing of its own gets a line with the default's call, so the generated switch
/// shows per request what it got, and the last arm answers the default for a request no line names. A request that
/// states its own keeps only its own: the default replaces nothing and stacks onto nothing. Each test compiles a module
/// of its own and asks the requests it made what they require, so a line that compiled and called the wrong case would
/// fail here.
/// </summary>
public class ModuleDefaultGenerationTests
{
    private const string Hint = "BillingAccessBehavior.";

    /// <summary>The module's interface with <paramref name="defaults"/> beside <c>[AccessRequests]</c>, declared partial.</summary>
    internal static string Billing(string defaults)
        => """
           using DDDToolkit.Abstractions.Attributes;
           using DDDToolkit.Access;
           using DDDToolkit.Supporting.Membership.Access;
           using DDDToolkit.Supporting.Tenancy.Access;

           namespace Shop.Billing;

           """
           + "[AccessRequests, " + defaults + "]\n"
           + "public partial interface IBillingRequest : IRequireAccess;\n";

    /// <summary>
    /// A project of the module's requests and its ids, the interface carrying <paramref name="defaults"/>, with both
    /// packages, and with the library or without it: one assembly, so a test can run what it compiled.
    /// </summary>
    internal static GeneratorTestHost Module(string defaults, string requests, bool mediator = false)
    {
        var host = GeneratorTestHost.Create(Billing(defaults), "Billing.cs")
            .WithTenancy()
            .WithMembershipAlone()
            .WithSource(RequirementAttributeGenerationTests.Ids, "Ids.cs")
            .WithSource(RequirementAttributeGenerationTests.Usings + requests, "Requests.cs");
        return mediator ? host.WithMediator() : host.WithDependencyInjection();
    }

    private static AccessRequirement Asked(EmittedAssembly emitted, string typeName, params object?[] arguments)
        => ((IRequireAccess)emitted.New("Shop.Billing." + typeName, arguments)).RequiredAccess;

    // ------------------------------------------------------------------ the default, taken

    [Fact]
    public void A_request_that_states_nothing_takes_the_module_s_default_on_a_line_of_its_own()
    {
        var result = Module(
            "InTenant",
            """
            public sealed record ListUnits : IBillingRequest;

            public sealed record UnitsById(IReadOnlyList<OrganizationUnitId> Ids) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.ListUnits => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.InTenant(),", "the switch shows per request what it got");
        result.ShouldContain(Hint, "global::Shop.Billing.UnitsById => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.InTenant(),", "a default reads no member, so the line binds none");

        var emitted = result.Emit();
        Asked(emitted, "ListUnits").Should().Be(TenancyAccess.InTenant());
        Asked(emitted, "UnitsById", Array.CreateInstance(emitted.Type("Shop.Billing.OrganizationUnitId"), 0)).Should().Be(TenancyAccess.InTenant());
    }

    [Fact]
    public void A_request_that_states_its_own_keeps_only_its_own()
    {
        var result = Module(
            "InTenant",
            """
            [SignedIn] public sealed record MyInvoices : IBillingRequest;

            [AllowAnonymous] public sealed record ListPlans : IBillingRequest;

            [RequiresPermission("billing.close")] public sealed record CloseTheBooks(int Year) : IBillingRequest;

            [RequiresPermission("billing.open-ledger")] public sealed record OpenLedger(string Name, [AtUnit] OrganizationUnitId Unit) : IBillingRequest;

            public sealed record ListLedgers(OrganizationUnitId? Unit) : IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => Unit is { } unit
                    ? TenancyAccess.AtUnit("billing.read", unit)
                    : TenancyAccess.AtTenant("billing.read");
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.MyInvoices => global::DDDToolkit.Access.AccessRequirement.SignedIn(),");
        result.ShouldContain(Hint, "global::Shop.Billing.ListPlans => global::DDDToolkit.Access.AccessRequirement.AllowAnonymous(),");
        result.ShouldNotContain(Hint, "AccessRequirement.All(", "the default stacks onto nothing a request states itself");
        result.ShouldNotContain(Hint, "ListLedgers", "a request that answers the property itself answers with its own, as without a default");

        var emitted = result.Emit();
        Asked(emitted, "MyInvoices").Should().Be(AccessRequirement.SignedIn(), "a signed-in user with no seat in the tenant is let through, as the request says");
        Asked(emitted, "ListPlans").Should().Be(AccessRequirement.AllowAnonymous(), "the default replaces nothing, an open door said on purpose neither");
        Asked(emitted, "CloseTheBooks", 2026).Should().Be(TenancyAccess.AtTenant("billing.close"));
        Asked(emitted, "OpenLedger", "Ledger", emitted.New("Shop.Billing.OrganizationUnitId", Guid.NewGuid())).GetType().Name.Should().Be("AtUnit`1");
        Asked(emitted, "ListLedgers", new object?[] { null }).Should().Be(TenancyAccess.AtTenant("billing.read"));
    }

    [Fact]
    public void Several_attributes_on_the_interface_are_all_of_their_calls_in_the_order_they_are_written()
    {
        var result = Module("SignedIn, InTenant", "public sealed record ListUnits : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.ListUnits => global::DDDToolkit.Access.AccessRequirement.All(global::DDDToolkit.Access.AccessRequirement.SignedIn(), global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.InTenant()),",
            "stacked on the interface as on a request: each holds, the first written asked first");
        Asked(result.Emit(), "ListUnits").Should().Be(AccessRequirement.All(AccessRequirement.SignedIn(), TenancyAccess.InTenant()));
    }

    [Fact]
    public void The_switch_has_a_line_for_each_request_and_its_last_arm_takes_the_default()
    {
        var result = Module(
            "InTenant",
            """
            public sealed record ListUnits : IBillingRequest;

            [RequiresPermission("billing.close")] public sealed record CloseTheBooks(int Year) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            Hint,
            """
            // What the requests of IBillingRequest that state their requirement with attributes require of their caller: the
            // call each attribute stands for, written out by DDDToolkit, one line per request. A request that answers
            // RequiredAccess itself answers with its own, which C# takes over this one, so it is never asked here.
            // A request that states none takes the module's default, [InTenant] on the interface, on a line of its own.
            partial interface IBillingRequest
            {
                /// <inheritdoc cref="global::DDDToolkit.Access.IRequireAccess.RequiredAccess"/>
                global::DDDToolkit.Access.AccessRequirement DDDToolkit_Generated_IRequireAccess.RequiredAccess => this switch
                {
                    global::Shop.Billing.CloseTheBooks => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtTenant("billing.close"),
                    global::Shop.Billing.ListUnits => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.InTenant(),
                    // A request this project cannot name, one of another project say, takes the module's default as well.
                    _ => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.InTenant(),
                };
            }
            """,
            "every request that takes the default is seen taking it, and the last arm is the default, not the throw");
        result.ShouldNotContain(Hint, "declares no access requirement", "with a default, no request of the module is stopped for stating nothing");
    }

    [Fact]
    public void A_request_no_line_can_name_takes_the_default_in_the_last_arm()
    {
        // A request with type parameters: a line names one type, so the switch has none for it, and the last arm answers.
        var result = Module("InTenant", "public sealed record Page<T> : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldNotContain(Hint, "Page", "a line names one type");

        var page = EmittedAssembly.New(result.Emit().Type("Shop.Billing.Page`1").MakeGenericType(typeof(int)));
        ((IRequireAccess)page).RequiredAccess.Should().Be(TenancyAccess.InTenant(), "what a module default means: any request of it that states nothing");
    }

    [Fact]
    public void A_key_as_the_default_is_held_for_the_whole_tenant()
    {
        var result = Module("RequiresPermission(\"billing.read\")", "public sealed record ListLedgers : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.ListLedgers => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtTenant(\"billing.read\"),");
        Asked(result.Emit(), "ListLedgers").Should().Be(TenancyAccess.AtTenant("billing.read"));
    }

    [Fact]
    public void A_module_s_own_attribute_is_a_default_where_its_class_allows_an_interface()
    {
        var result = Module(
            "RequiresPlan(Plan.Pro)",
            RequirementCombinationGenerationTests.Plans.Replace("AttributeTargets.Class | AttributeTargets.Struct", "AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface", StringComparison.Ordinal)
            + "public sealed record ExportBoard : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.ExportBoard => global::Shop.Billing.BillingAccess.RequiresPlan((global::Shop.Billing.Plan)(1)),");
        Asked(result.Emit(), "ExportBoard").GetType().Name.Should().Be("OnPlan");
    }

    [Fact]
    public void A_derived_request_takes_its_base_s_line_or_the_default_and_never_both()
    {
        var result = Module(
            "InTenant",
            """
            [SignedIn] public abstract record Report : IBillingRequest;
            public sealed record MonthlyReport(int Month) : Report;

            public record Ledger : IBillingRequest;
            public sealed record SpecialLedger : Ledger;
            [RequiresPermission("billing.close")] public sealed record ClosedLedger : Ledger;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        var emitted = result.Emit();

        Asked(emitted, "MonthlyReport", 5).Should().Be(AccessRequirement.SignedIn(), "its base states a requirement, which it inherits, and the default is for a request that inherits none");
        result.ShouldNotContain(Hint, "global::Shop.Billing.MonthlyReport =>", "the base's line takes it");
        Asked(emitted, "Ledger").Should().Be(TenancyAccess.InTenant());
        Asked(emitted, "SpecialLedger").Should().Be(TenancyAccess.InTenant(), "a base that takes the default states nothing to inherit, so the derived request takes the default itself");
        result.ShouldContain(Hint, "global::Shop.Billing.SpecialLedger => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.InTenant(),");
        Asked(emitted, "ClosedLedger").Should().Be(TenancyAccess.AtTenant("billing.close"), "the default of its base is no attribute of its base, and stacks onto nothing");
    }

    // ------------------------------------------------------------------ held to it, with the library

    [Fact]
    public async Task The_behavior_holds_a_request_that_takes_the_default_to_it_and_one_with_its_own_to_its_own()
    {
        var result = Module(
            "SignedIn",
            """
            public sealed record MyInvoices : Mediator.ICommand, IBillingRequest;

            [AllowAnonymous] public sealed record ListPlans : Mediator.ICommand, IBillingRequest;

            public static class Probe
            {
                public static async System.Threading.Tasks.Task<string> SendAsync<TRequest>(TRequest request, DDDToolkit.Abstractions.Access.ICurrentUser user)
                    where TRequest : Mediator.ICommand, IBillingRequest
                {
                    var behavior = new BillingAccessBehavior<TRequest, Mediator.Unit>(new AccessChecks<IBillingRequest>(new IAccessCheck[] { new CurrentUserAccessCheck(user) }));
                    try
                    {
                        await behavior.Handle(request, (message, cancellationToken) => new System.Threading.Tasks.ValueTask<Mediator.Unit>(Mediator.Unit.Value), System.Threading.CancellationToken.None);
                        return "handled";
                    }
                    catch (DDDToolkit.Exceptions.RefusalException refused)
                    {
                        return refused.Code;
                    }
                }

                public static System.Threading.Tasks.Task<string> TakingTheDefault(DDDToolkit.Abstractions.Access.ICurrentUser user) => SendAsync(new MyInvoices(), user);

                public static System.Threading.Tasks.Task<string> StatingItsOwn(DDDToolkit.Abstractions.Access.ICurrentUser user) => SendAsync(new ListPlans(), user);
            }
            """,
            mediator: true).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        var emitted = result.Emit();
        var anonymous = RequirementAttributeGenerationTests.Answering(DDDToolkit.Abstractions.Access.CurrentUserSnapshot.Anonymous);
        var signedIn = RequirementAttributeGenerationTests.Answering(DDDToolkit.Abstractions.Access.CurrentUserSnapshot.User(Guid.NewGuid()));

        (await (Task<string>)emitted.CallStatic("Shop.Billing.Probe", "TakingTheDefault", anonymous)!).Should().Be("access.not-signed-in", "the request said nothing, and the module's default holds it");
        (await (Task<string>)emitted.CallStatic("Shop.Billing.Probe", "TakingTheDefault", signedIn)!).Should().Be("handled");
        (await (Task<string>)emitted.CallStatic("Shop.Billing.Probe", "StatingItsOwn", anonymous)!).Should().Be("handled", "a request that states its own is held to its own, and not to the default");
    }
}
