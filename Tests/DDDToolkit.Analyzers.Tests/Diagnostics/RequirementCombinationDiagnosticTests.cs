using System.Globalization;
using DDDToolkit.Analyzers.Tests.Generation;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00084 to DDD00086: stacked requirement attributes that say less than a neighbour already requires, a warning; and
/// the markers of the caller's own in their place, <c>[OrOwn]</c> beside a permission, <c>[Own]</c> with <c>[OwnOnly]</c>, on a
/// member of a type something says the caller's own of, each an error, since a request that reads as if it let the
/// caller's own through and lets nobody, or anybody, is a door a review would misread.
/// </summary>
public class RequirementCombinationDiagnosticTests
{
    private static GeneratorRunOutcome Run(string requests) => RequirementAttributeGenerationTests.Module(requests).RunCore();

    private static GeneratorRunOutcome OverTheDomain(string requests) => RequirementAttributeGenerationTests.OverTheDomain(requests).RunCore();

    private static string Message(GeneratorRunOutcome result, string id, string at)
        => result.ShouldHaveDiagnostic(id, at).GetMessage(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ DDD00084: says nothing more

    [Theory]
    [InlineData("[InTenant, RequiresPermission(\"billing.close\")] public sealed record CloseTheBooks(int Year) : IBillingRequest;", "InTenant", "[InTenant] beside [RequiresPermission], which requires it already")]
    [InlineData("[RequiresPermission(\"billing.close\"), InTenant] public sealed record CloseTheBooks([AtUnit] OrganizationUnitId Unit) : IBillingRequest;", "InTenant", "[InTenant] beside [RequiresPermission] with [AtUnit], which requires it already")]
    [InlineData("[SignedIn, RequiresOperator] public sealed record CloseTheBooks(int Year) : IBillingRequest;", "SignedIn", "[SignedIn] beside [RequiresOperator], which requires it already")]
    public void A_requirement_attribute_its_neighbour_already_requires_is_DDD00084_a_warning_and_the_line_is_written(string request, string at, string said)
    {
        var result = Run(request);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00084");
        Message(result, "DDD00084", at: at).Should().Contain("carries " + said).And.Contain("adds nothing to what the request requires");
        result.ShouldHaveDiagnostic("DDD00084", at: at).Severity.Should().Be(DiagnosticSeverity.Warning, "it checks nothing wrong, and reads as a condition it is not");
        result.ShouldContain("BillingAccessBehavior.", "global::Shop.Billing.CloseTheBooks", "a warning leaves the line as the attributes say it");
    }

    [Theory]
    [InlineData("[OwnOnly, SignedIn] public sealed record CloseAccount([Own] Guid User) : IBillingRequest;", "SignedIn", "[SignedIn] beside [OwnOnly] with [Own] 'User', which requires it already")]
    [InlineData("[InTenant, OwnOnly] public sealed record LeaveTenant([Own] SeatId Seat) : IBillingRequest;", "InTenant", "[InTenant] beside [OwnOnly] with [Own] 'Seat', which requires it already")]
    [InlineData("[OwnOnly, SignedIn] public sealed record LeaveTenant([Own] SeatId Seat) : IBillingRequest;", "SignedIn", "[SignedIn] beside [OwnOnly] with [Own] 'Seat', which requires it already")]
    [InlineData("[InTenant, RequiresPermission(\"tenancy.seats.manage\")] public sealed record RenameSeat([OrOwn] SeatId Seat) : IBillingRequest;", "InTenant", "[InTenant] beside [RequiresPermission] and [OrOwn] 'Seat', which requires it already")]
    public void What_the_caller_s_own_requires_already_is_counted_and_beside_a_permission_only_what_both_ways_through_require(string request, string at, string said)
    {
        // Only a signed-in user has a verified identity, and only a seat in the tenant a seat of its own there; a permission of
        // Tenancy's for the whole tenant is held in it too, so either way through the caller is in the tenant.
        var result = OverTheDomain(request);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00084");
        Message(result, "DDD00084", at: at).Should().Contain("carries " + said);
    }

    [Theory]
    [InlineData("[SignedIn, RequiresPermission(\"tenancy.seats.manage\")] public sealed record RenameSeat([OrOwn] SeatId Seat) : IBillingRequest;")]
    [InlineData("[InTenant, RequiresPermission(\"tenancy.seats.manage\")] public sealed record RenameUser([OrOwn] Guid User) : IBillingRequest;")]
    [InlineData("[InTenant, OwnOnly] public sealed record Forget([Own] Guid User) : IBillingRequest;")]
    public void An_attribute_one_way_through_does_not_require_already_is_no_DDD00084(string request)
    {
        // System work holds every permission in its tenant and is nobody's sign-in, and a caller's verified identity is no seat.
        var result = OverTheDomain(request);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_module_s_own_attribute_says_what_it_requires_already_and_is_held_to_it_the_same_way()
    {
        var result = Run(
            RequirementCombinationGenerationTests.Plans.Replace(
                "[StatesRequirement(typeof(BillingAccess), nameof(BillingAccess.RequiresPlan))]",
                "[StatesRequirement(typeof(BillingAccess), nameof(BillingAccess.RequiresPlan), Implies = [typeof(SignedInAttribute)])]",
                StringComparison.Ordinal)
            + "[RequiresPlan(Plan.Pro), SignedIn] public sealed record ExportBoard : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00084");
        Message(result, "DDD00084", at: "SignedIn").Should().Contain("carries [SignedIn] beside [RequiresPlan], which requires it already");
    }

    // ------------------------------------------------------------------ DDD00085: the caller's own in its place

    [Fact]
    public void OrOwn_without_a_permission_is_DDD00085()
    {
        var result = OverTheDomain("[InTenant] public sealed record RenameSeat([OrOwn] SeatId Seat) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00085");
        Message(result, "DDD00085", at: "OrOwn").Should().Contain("marks 'Seat' [OrOwn], and requires no permission").And.Contain("[OwnOnly]");
        result.ShouldNotContain("BillingAccessBehavior.", "RenameSeat");
    }

    [Fact]
    public void Own_without_OwnOnly_and_OwnOnly_without_Own_are_DDD00085()
    {
        var own = OverTheDomain("[InTenant] public sealed record LeaveTenant([Own] SeatId Seat) : IBillingRequest;");
        own.ShouldHaveExactlyDiagnostics("DDD00085");
        Message(own, "DDD00085", at: "Own").Should().Contain("marks 'Seat' [Own], and is not [OwnOnly]");

        var ownOnly = OverTheDomain("[OwnOnly] public sealed record LeaveTenant(SeatId Seat) : IBillingRequest;");
        ownOnly.ShouldHaveExactlyDiagnostics("DDD00085");
        Message(ownOnly, "DDD00085", at: "LeaveTenant").Should().Contain("carries [OwnOnly] and marks no member [Own]");
    }

    [Fact]
    public void OrOwn_beside_AtUnit_is_DDD00085_and_beside_OnResource_it_is_not()
    {
        var atUnit = OverTheDomain("[RequiresPermission(\"tenancy.seats.manage\")] public sealed record MoveSeat([OrOwn] SeatId Seat, [AtUnit] OrganizationUnitId Unit) : IBillingRequest;");

        atUnit.ShouldHaveExactlyDiagnostics("DDD00085");
        Message(atUnit, "DDD00085", at: "OrOwn").Should().Contain("marks 'Seat' [OrOwn] beside 'Unit' [AtUnit], which says where the permission is checked and not what the request acts on")
            .And.Contain("would send one where it holds the permission for another subject");
        atUnit.ShouldNotContain("BillingAccessBehavior.", "MoveSeat");

        OverTheDomain("[RequiresPermission(\"documents.manage\")] public sealed record LeaveDocument([OnResource] DocumentId Document, [OrOwn] Guid User) : IBillingRequest;")
            .ShouldHaveExactlyDiagnostics();
    }

    [Theory]
    [InlineData("[RequiresPermission(\"k.manage\")] public sealed record Swap([OrOwn] SeatId From, [OrOwn] SeatId To) : IBillingRequest;", "OrOwn", "marks the caller's own more than once")]
    [InlineData("[OwnOnly, RequiresPermission(\"k.manage\")] public sealed record Swap([Own, OrOwn] SeatId Seat) : IBillingRequest;", "OrOwn", "marks 'Seat' [Own] and [OrOwn]")]
    [InlineData("[RequiresPermission(\"k.manage\")] public sealed class Swap(SeatId seat) : IBillingRequest { [OrOwn] private SeatId Seat { get; } = seat; }", "OrOwn", "which 'IBillingRequest', where the requirement is written, cannot read")]
    public void The_caller_s_own_is_one_member_the_line_can_read_or_it_is_DDD00085(string request, string at, string said)
    {
        var result = OverTheDomain(request);

        result.ShouldHaveDiagnostic("DDD00085", at: at).GetMessage(CultureInfo.InvariantCulture).Should().Contain(said);
        result.ShouldNotContain("BillingAccessBehavior.", "Swap");
    }

    // ------------------------------------------------------------------ DDD00086: a type with an own

    [Fact]
    public void OrOwn_on_a_type_nothing_says_the_caller_s_own_of_is_DDD00086()
    {
        var result = OverTheDomain("[RequiresPermission(\"profiles.edit\")] public sealed record EditProfile([OrOwn] UserId User) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00086");
        Message(result, "DDD00086", at: "OrOwn").Should().Contain("marks 'User' [OrOwn], which is a 'UserId': nothing says what the caller's own UserId is")
            .And.Contain("services.AddCallersOwn<UserId, ...>()");
        result.ShouldNotContain("BillingAccessBehavior.", "EditProfile", "a request stopped every time it is sent gets no line");
    }

    [Fact]
    public void Own_on_a_member_that_may_be_null_is_DDD00086()
    {
        var result = OverTheDomain("[OwnOnly] public sealed record LeaveTenant([Own] SeatId? Seat) : IBillingRequest;");

        result.ShouldHaveExactlyDiagnostics("DDD00086");
        Message(result, "DDD00086", at: "Own").Should().Contain("which is 'SeatId?' and may be null");
    }

    [Fact]
    public void The_seat_id_tenancy_says_the_caller_s_own_of_and_the_core_s_identity_are_no_DDD00086()
    {
        OverTheDomain(
                """
                [RequiresPermission("tenancy.seats.manage")] public sealed record RenameSeat([OrOwn] SeatId Seat) : IBillingRequest;
                [OwnOnly] public sealed record Forget([Own] Guid User) : IBillingRequest;
                """)
            .ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void Where_the_project_sees_no_class_of_the_seat_s_template_nothing_can_be_told_and_nothing_is_reported()
    {
        // Tenancy names its seat id by the template the application declares its seat with: a project that sees no such
        // class cannot tell its seat id from another, so it is silent, and the check stops the request when it is sent.
        var result = Run("[RequiresPermission(\"profiles.edit\")] public sealed record EditProfile([OrOwn] UserId User) : IBillingRequest;");

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Theory]
    [InlineData("[RequiresPermission(\"tenancy.seats.manage\")] public sealed record RenameByName([OrOwn] string Name) : IBillingRequest;", "OrOwn", "which is a 'string'")]
    [InlineData("public sealed record UserRef(Guid Id); [OwnOnly] public sealed record CloseRef([Own] UserRef User) : IBillingRequest;", "Own", "which is a 'UserRef'")]
    [InlineData("public enum Shift { Day, Night } [OwnOnly] public sealed record TakeShift([Own] Shift Shift) : IBillingRequest;", "Own", "which is a 'Shift'")]
    public void A_member_no_handle_could_ever_say_the_caller_s_own_of_is_DDD00086_where_nothing_else_can_be_told(string request, string at, string said)
    {
        // The case compares a value that equals itself: a string, a class or an enum has no own whatever the project sees,
        // so it is told without a seat class to read, and no line is written that would not compile.
        var result = Run(request);

        result.ShouldHaveExactlyDiagnostics("DDD00086");
        Message(result, "DDD00086", at: at).Should().Contain(said).And.Contain("a struct that is IEquatable of itself");
        result.ShouldNotContain("BillingAccessBehavior.", ".Own(request.");
    }

    // ------------------------------------------------------------------ a request and the one it derives from

    [Fact]
    public void A_derived_request_s_attribute_that_its_base_requires_already_is_DDD00084()
    {
        var result = OverTheDomain(
            """
            [RequiresPermission("billing.close")] public abstract record Closing : IBillingRequest;
            [InTenant] public sealed record CloseTheBooks(int Year) : Closing;

            [SignedIn] public abstract record Report : IBillingRequest;
            [SignedIn] public sealed record MyReport : Report;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00084", "DDD00084");
        result.ShouldHaveDiagnostic("DDD00084", at: "InTenant").GetMessage(CultureInfo.InvariantCulture)
            .Should().Contain("'CloseTheBooks' carries [InTenant] beside [RequiresPermission] of 'Closing', which requires it already");
        result.ShouldHaveDiagnostic("DDD00084", at: "SignedIn").GetMessage(CultureInfo.InvariantCulture)
            .Should().Contain("'MyReport' carries [SignedIn] beside 'Report' it derives from, which requires it already");
    }

    [Fact]
    public void A_derived_request_that_opens_the_door_its_base_closes_or_requires_a_second_permission_is_reported()
    {
        var anonymous = OverTheDomain("[SignedIn] public abstract record Report : IBillingRequest; [AllowAnonymous] public sealed record PublicReport : Report;");
        anonymous.ShouldHaveExactlyDiagnostics("DDD00079");
        Message(anonymous, "DDD00079", at: "PublicReport").Should().Contain("[SignedIn] of 'Report'");

        var below = OverTheDomain("[AllowAnonymous] public abstract record Open : IBillingRequest; [SignedIn] public sealed record Closed : Open;");
        below.ShouldHaveExactlyDiagnostics("DDD00079");
        Message(below, "DDD00079", at: "Closed").Should().Contain("beside [SignedIn], deriving from 'Open', which carries [AllowAnonymous]");

        var twoPermissions = OverTheDomain("[RequiresPermission(\"billing.close\")] public abstract record Closing : IBillingRequest; [RequiresPermission(\"billing.audit\")] public sealed record AuditTheBooks : Closing;");
        twoPermissions.ShouldHaveExactlyDiagnostics("DDD00080");
        Message(twoPermissions, "DDD00080", at: "AuditTheBooks").Should().Contain("requires two permissions, [RequiresPermission] of 'Closing' and [RequiresPermission]");
        twoPermissions.ShouldNotContain("BillingAccessBehavior.", "AuditTheBooks =>");
    }
}
