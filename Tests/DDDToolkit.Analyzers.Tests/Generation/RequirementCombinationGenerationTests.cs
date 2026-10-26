using DDDToolkit.Access;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// Requirement attributes stacked on one request all hold, and the generator writes <c>AccessRequirement.All</c> of the
/// calls they stand for, in the order they are written above the request; <c>[OrOwn]</c> beside a key makes the caller's
/// own its alternative, <c>AccessRequirement.Any(AccessRequirement.Own(...), ...)</c>, and <c>[OwnOnly]</c> with
/// <c>[Own]</c> says the caller's own alone. Each test compiles a module of its own and asks the request it made what it
/// requires, so a line that compiled and called the wrong case would fail here.
/// </summary>
public class RequirementCombinationGenerationTests
{
    private const string Hint = "BillingAccessBehavior.";

    /// <summary>A module's own requirement attribute, declared as a package declares one, with the case it stands for.</summary>
    internal const string Plans =
        """
        public enum Plan
        {
            Free,
            Pro,
        }

        public abstract record BillingAccess : AccessRequirement
        {
            private BillingAccess()
            {
            }

            public static OnPlan RequiresPlan(Plan plan) => new(plan);

            public sealed record OnPlan(Plan Plan) : BillingAccess;
        }

        [StatesRequirement(typeof(BillingAccess), nameof(BillingAccess.RequiresPlan))]
        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
        public sealed class RequiresPlanAttribute(Plan plan) : Attribute
        {
            public Plan Plan { get; } = plan;
        }

        """;

    private static object IdOf(EmittedAssembly emitted, string name, Guid value) => emitted.New("Shop.Billing." + name, value);

    private static AccessRequirement Asked(EmittedAssembly emitted, string typeName, params object?[] arguments)
        => ((IRequireAccess)emitted.New("Shop.Billing." + typeName, arguments)).RequiredAccess;

    // ------------------------------------------------------------------ stacked attributes are all

    [Fact]
    public void Stacked_attributes_are_all_of_their_calls_in_the_order_they_are_written()
    {
        var result = RequirementAttributeGenerationTests.Module(
            Plans
            + """
              [RequiresPlan(Plan.Pro)]
              [RequiresKey("boards.export")]
              public sealed record ExportBoard : IBillingRequest;

              [RequiresKey("boards.export"), RequiresPlan(Plan.Pro)]
              public sealed record ExportBoardKeyFirst : IBillingRequest;
              """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.ExportBoard => global::DDDToolkit.Access.AccessRequirement.All(global::Shop.Billing.BillingAccess.RequiresPlan((global::Shop.Billing.Plan)(1)), "
            + "global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtTenant(\"boards.export\")),",
            "like several [Authorize] in ASP.NET Core, each attribute holds, and a module's own stacks as a package's does");

        var emitted = result.Emit();
        var all = Asked(emitted, "ExportBoard").Should().BeOfType<AccessRequirement.AllOf>().Subject;
        all.Cases.Select(each => each.GetType().Name).Should().Equal(["OnPlan", "AtTenant"], "the plan is written first, so it is asked first");
        all.Cases[1].Should().Be(TenancyAccess.AtTenant("boards.export"));
        ((AccessRequirement.AllOf)Asked(emitted, "ExportBoardKeyFirst")).Cases.Select(each => each.GetType().Name)
            .Should().Equal(["AtTenant", "OnPlan"], "the source order is the order of the cases");
    }

    [Fact]
    public void A_key_s_marker_keeps_saying_where_it_is_held_beside_another_attribute()
    {
        var result = RequirementAttributeGenerationTests.Module(
            """
            [SignedIn]
            [RequiresKey("billing.open-ledger")]
            public sealed record OpenLedger(string Name, [AtUnit] OrganizationUnitId Unit) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.OpenLedger request => global::DDDToolkit.Access.AccessRequirement.All(global::DDDToolkit.Access.AccessRequirement.SignedIn(), "
            + "global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtUnit(\"billing.open-ledger\", request.Unit)),");

        var emitted = result.Emit();
        var unit = Guid.NewGuid();
        var all = (AccessRequirement.AllOf)Asked(emitted, "OpenLedger", "Ledger", IdOf(emitted, "OrganizationUnitId", unit));
        all.Cases[0].Should().Be(AccessRequirement.SignedIn());
        all.Cases[1].ToString().Should().Contain("billing.open-ledger").And.Contain(unit.ToString());
    }

    [Fact]
    public void One_attribute_is_its_call_alone()
    {
        var result = RequirementAttributeGenerationTests.Module("[InTenant] public sealed record Invoices : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldNotContain(Hint, "AccessRequirement.All(", "one requirement needs nothing to combine it with");
    }

    // ------------------------------------------------------------------ yours, or the key

    [Fact]
    public void The_caller_s_own_beside_a_key_for_the_whole_tenant_is_any_of_the_two_the_own_first()
    {
        var result = RequirementAttributeGenerationTests.Module(
            """
            [RequiresKey("tenancy.seats.manage")]
            public sealed record RenameSeat([OrOwn] SeatId Seat, string? DisplayName) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.RenameSeat request => global::DDDToolkit.Access.AccessRequirement.Any(global::DDDToolkit.Access.AccessRequirement.Own(request.Seat), "
            + "global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtTenant(\"tenancy.seats.manage\")),",
            "the caller's own reads nothing, so it is asked first; the key only where the seat is not the caller's");

        var emitted = result.Emit();
        var seat = Guid.NewGuid();
        var any = Asked(emitted, "RenameSeat", IdOf(emitted, "SeatId", seat), "Bert").Should().BeOfType<AccessRequirement.AnyOf>().Subject;
        any.Cases[0].GetType().Name.Should().Be("Owned`1");
        any.Cases[0].ToString().Should().Contain(seat.ToString());
        any.Cases[1].Should().Be(TenancyAccess.AtTenant("tenancy.seats.manage"));
    }

    [Fact]
    public void The_caller_s_own_beside_a_key_on_a_resource_is_any_of_the_two_and_the_own_way_still_sees_the_resource()
    {
        var result = RequirementAttributeGenerationTests.Module(
            """
            [RequiresKey("documents.manage")]
            public sealed record LeaveDocument([OnResource] DocumentId Document, [OrOwn] Guid User, [ExpectedVersion] long? ExpectedVersion = null) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.LeaveDocument request => global::DDDToolkit.Access.AccessRequirement.Any(global::DDDToolkit.Access.AccessRequirement.All(global::DDDToolkit.Access.AccessRequirement.Own(request.User), "
            + "global::DDDToolkit.Supporting.Membership.Access.MemberAccess.Sees(\"documents.manage\", request.Document, request.ExpectedVersion)), "
            + "global::DDDToolkit.Supporting.Membership.Access.MemberAccess.On(\"documents.manage\", request.Document, request.ExpectedVersion)),",
            "take yourself off a document you see, or manage the members: either way the document is read, its version compared and its hold kept");

        var user = Guid.NewGuid();
        var document = Guid.NewGuid();
        var emitted = result.Emit();
        var any = (AccessRequirement.AnyOf)Asked(emitted, "LeaveDocument", IdOf(emitted, "DocumentId", document), user, 4L);
        var own = any.Cases[0].Should().BeOfType<AccessRequirement.AllOf>().Subject;
        own.Cases[0].Should().Be(AccessRequirement.Own(user), "a Guid is the caller's verified identity, the core's own, and it reads nothing, so it is asked first");
        own.Cases[1].GetType().Name.Should().Be("Sees");
        own.Cases[1].ToString().Should().Contain(document.ToString()).And.Contain("ExpectedVersion = 4");
        any.Cases[1].GetType().Name.Should().Be("On");
    }

    [Fact]
    public void The_caller_s_own_alone_is_own_of_the_member_marked_own()
    {
        var result = RequirementAttributeGenerationTests.Module(
            Plans
            + """
              [OwnOnly]
              public sealed record LeaveTenant([Own] SeatId Seat) : IBillingRequest;

              [OwnOnly, RequiresPlan(Plan.Pro)]
              public sealed record LeaveTenantStated(SeatId Seat) : IBillingRequest
              {
                  [Own] public SeatId Mine { get; } = Seat;
              }
              """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.LeaveTenant request => global::DDDToolkit.Access.AccessRequirement.Own(request.Seat),");
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.LeaveTenantStated request => global::DDDToolkit.Access.AccessRequirement.All(global::DDDToolkit.Access.AccessRequirement.Own(request.Mine), "
            + "global::Shop.Billing.BillingAccess.RequiresPlan((global::Shop.Billing.Plan)(1))),",
            "[OwnOnly] stacks as any requirement attribute does, beside one that says more than the caller's own, and [Own] goes on an ordinary property too");

        var emitted = result.Emit();
        Asked(emitted, "LeaveTenant", IdOf(emitted, "SeatId", Guid.NewGuid())).GetType().Name.Should().Be("Owned`1");
    }

    [Fact]
    public void A_kind_of_the_application_s_own_is_the_caller_s_own_where_the_project_sees_its_handle()
    {
        var result = RequirementAttributeGenerationTests.OverTheDomain(
            """
            public sealed class CallersUser : ICallersOwn<UserId>
            {
                public UserId? Find(DDDToolkit.Abstractions.Access.Caller caller) => caller.UserId is { } id ? new UserId(id) : null;
            }

            [RequiresKey("profiles.edit")]
            public sealed record EditProfile([OrOwn] UserId User) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.EditProfile request => global::DDDToolkit.Access.AccessRequirement.Any(global::DDDToolkit.Access.AccessRequirement.Own(request.User), ");
    }

    [Fact]
    public void A_handle_of_a_referenced_project_says_the_caller_s_own_as_one_of_the_requests_own_project_does()
    {
        var result = GeneratorTestHost.Create(RequirementAttributeGenerationTests.Billing, "Billing.cs")
            .WithTenancy()
            .WithMembershipAlone()
            .WithReferencedAssembly(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Access;

                namespace Shop.Accounts;

                [EntityId<Guid>] public readonly partial record struct AccountId;

                public sealed class CallersAccount : ICallersOwn<AccountId>
                {
                    public AccountId? Find(DDDToolkit.Abstractions.Access.Caller caller) => caller.UserId is { } id ? new AccountId(id) : null;
                }
                """,
                "Shop.Accounts")
            .WithSource(RequirementAttributeGenerationTests.Usings + "[OwnOnly] public sealed record CloseAccount([Own] Shop.Accounts.AccountId Account) : IBillingRequest;", "Requests.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.CloseAccount request => global::DDDToolkit.Access.AccessRequirement.Own(request.Account),");
    }
}
