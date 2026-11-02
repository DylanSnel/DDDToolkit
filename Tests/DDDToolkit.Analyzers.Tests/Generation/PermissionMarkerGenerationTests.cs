using DDDToolkit.Access;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A marker on a request's member carries its permission, <c>[OnResource(permission)]</c> or <c>[AtUnit(permission)]</c>,
/// and several on one request all hold: the generator writes <c>AccessRequirement.All</c> of their calls, after the
/// attributes on the request, in the order they are written. A version goes with the one marker it names, and the caller's
/// own pairs with the one permission it names. Each test compiles a module of its own and asks the request it made what it
/// requires, so a line that compiled and called the wrong case would fail here.
/// </summary>
public class PermissionMarkerGenerationTests
{
    private const string Hint = "BillingAccessBehavior.";

    private const string On = "global::DDDToolkit.Supporting.Membership.Access.MemberAccess.On";

    private const string Sees = "global::DDDToolkit.Supporting.Membership.Access.MemberAccess.Sees";

    private const string AtUnit = "global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtUnit";

    private const string Requirement = "global::DDDToolkit.Access.AccessRequirement";

    /// <summary>The resources a request names with the generic forms, in the requests' project so a test can run what it compiled.</summary>
    private const string Resources =
        """
        [AggregateRoot<DocumentId>]
        public sealed partial class Document
        {
            public Document(DocumentId id) : base(id)
            {
            }
        }

        [AggregateRoot<FolderId>]
        public sealed partial class Folder
        {
            public Folder(FolderId id) : base(id)
            {
            }
        }

        """;

    private static GeneratorRunOutcome Run(string requests) => RequirementAttributeGenerationTests.Module(Resources + requests).RunCore();

    private static object IdOf(EmittedAssembly emitted, string name, Guid value) => emitted.New("Shop.Billing." + name, value);

    private static AccessRequirement Asked(EmittedAssembly emitted, string typeName, params object?[] arguments)
        => ((IRequireAccess)emitted.New("Shop.Billing." + typeName, arguments)).RequiredAccess;

    // ------------------------------------------------------------------ several markers

    [Fact]
    public void Markers_on_two_kinds_of_resource_are_all_of_their_calls_and_the_named_version_goes_with_its_resource()
    {
        var result = Run(
            """
            public sealed record FileDocument(
                [OnResource("documents.edit")] DocumentId Document,
                [OnResource("folders.file")] FolderId Folder,
                [ExpectedVersion<Document>] long? Version = null) : IBillingRequest;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.FileDocument request => " + Requirement + ".All(" + On + "(\"documents.edit\", request.Document, request.Version), " + On + "(\"folders.file\", request.Folder)),",
            "the document's check compares the version, and the folder's only reads");

        var emitted = result.Emit();
        var document = Guid.NewGuid();
        var folder = Guid.NewGuid();
        var all = Asked(emitted, "FileDocument", IdOf(emitted, "DocumentId", document), IdOf(emitted, "FolderId", folder), 3L).Should().BeOfType<AccessRequirement.AllOf>().Subject;
        all.Cases.Should().HaveCount(2);
        all.Cases[0].ToString().Should().Contain("documents.edit").And.Contain(document.ToString()).And.Contain("ExpectedVersion = 3");
        all.Cases[1].ToString().Should().Contain("folders.file").And.Contain(folder.ToString()).And.NotContain("ExpectedVersion = 3");
    }

    [Fact]
    public void Two_markers_of_one_kind_are_both_required_in_the_order_they_are_written()
    {
        var result = Run(
            """
            public sealed record MoveLedger([AtUnit("billing.move")] OrganizationUnitId From, [AtUnit("billing.move")] OrganizationUnitId To) : IBillingRequest;

            public sealed record MergeDocuments([OnResource("documents.edit")] DocumentId Into, [OnResource("documents.edit")] DocumentId From) : IBillingRequest;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.MoveLedger request => " + Requirement + ".All(" + AtUnit + "(\"billing.move\", request.From), " + AtUnit + "(\"billing.move\", request.To)),");
        result.ShouldContain(Hint, "global::Shop.Billing.MergeDocuments request => " + Requirement + ".All(" + On + "(\"documents.edit\", request.Into), " + On + "(\"documents.edit\", request.From)),");

        var emitted = result.Emit();
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var all = Asked(emitted, "MoveLedger", IdOf(emitted, "OrganizationUnitId", from), IdOf(emitted, "OrganizationUnitId", to)).Should().BeOfType<AccessRequirement.AllOf>().Subject;
        all.Cases.Select(each => each.GetType().Name).Should().Equal(["AtUnit`1", "AtUnit`1"]);
        all.Cases[0].ToString().Should().Contain(from.ToString(), "the unit it leaves is asked first, as it is written first");
        all.Cases[1].ToString().Should().Contain(to.ToString());
    }

    [Fact]
    public void A_resource_and_a_unit_are_both_required_and_the_bare_version_goes_with_the_one_resource()
    {
        var result = Run(
            """
            public sealed record MoveDocument([OnResource("documents.edit")] DocumentId Document, [AtUnit("documents.open")] OrganizationUnitId To, [ExpectedVersion] long? ExpectedVersion = null) : IBillingRequest;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.MoveDocument request => " + Requirement + ".All(" + On + "(\"documents.edit\", request.Document, request.ExpectedVersion), " + AtUnit + "(\"documents.open\", request.To)),");
    }

    [Fact]
    public void A_permission_without_a_marker_is_one_more_requirement_beside_markers_and_the_attributes_come_first()
    {
        var result = Run(
            """
            public sealed record ShareFolder([OnResource("folders.share")] FolderId Folder, [AtUnit("billing.share")] OrganizationUnitId Unit) : IBillingRequest;

            [SignedIn, RequiresPermission("billing.audit")]
            public sealed record AuditFolder([OnResource("folders.read")] FolderId Folder) : IBillingRequest;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.AuditFolder request => " + Requirement + ".All(" + Requirement + ".SignedIn(), global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtTenant(\"billing.audit\"), "
            + On + "(\"folders.read\", request.Folder)),",
            "the attributes on the request in the order they are written, then the markers on its members");
    }

    [Fact]
    public void The_named_version_goes_with_one_marker_too()
    {
        var result = Run("public sealed record EditDocument([OnResource(\"documents.edit\")] DocumentId Document, [ExpectedVersion<Document>] long? Version = null) : IBillingRequest;");

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.EditDocument request => " + On + "(\"documents.edit\", request.Document, request.Version),");
    }

    // ------------------------------------------------------------------ the caller's own among several permissions

    [Fact]
    public void OrOwn_naming_a_resource_pairs_with_that_marker_alone_in_its_place()
    {
        var result = Run(
            """
            public sealed record UnfileDocument(
                [OnResource("folders.file")] FolderId Folder,
                [OnResource("documents.edit")] DocumentId Document,
                [OrOwn<Folder>] Guid FiledBy) : IBillingRequest;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.UnfileDocument request => " + Requirement + ".All("
            + Requirement + ".Any(" + Requirement + ".All(" + Requirement + ".Own(request.FiledBy), " + Sees + "(\"folders.file\", request.Folder)), " + On + "(\"folders.file\", request.Folder)), "
            + On + "(\"documents.edit\", request.Document)),",
            "the one who filed it takes it off a folder it sees, or whoever files there does, and either way the caller edits the document");

        var emitted = result.Emit();
        var user = Guid.NewGuid();
        var all = Asked(emitted, "UnfileDocument", IdOf(emitted, "FolderId", Guid.NewGuid()), IdOf(emitted, "DocumentId", Guid.NewGuid()), user).Should().BeOfType<AccessRequirement.AllOf>().Subject;
        var any = all.Cases[0].Should().BeOfType<AccessRequirement.AnyOf>().Subject;
        ((AccessRequirement.AllOf)any.Cases[0]).Cases[0].Should().Be(AccessRequirement.Own(user));
        all.Cases[1].GetType().Name.Should().Be("On");
    }

    [Fact]
    public void OrOwn_naming_the_resource_of_the_one_marker_is_the_bare_form_s_line()
    {
        var result = Run(
            """
            public sealed record LeaveFolder([OnResource("folders.staff")] FolderId Folder, [OrOwn<Folder>] Guid User) : IBillingRequest;

            public sealed record LeaveFolderBare([OnResource("folders.staff")] FolderId Folder, [OrOwn] Guid User) : IBillingRequest;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        var expected = Requirement + ".Any(" + Requirement + ".All(" + Requirement + ".Own(request.User), " + Sees + "(\"folders.staff\", request.Folder)), " + On + "(\"folders.staff\", request.Folder)),";
        result.ShouldContain(Hint, "global::Shop.Billing.LeaveFolder request => " + expected);
        result.ShouldContain(Hint, "global::Shop.Billing.LeaveFolderBare request => " + expected);
    }

    [Fact]
    public void A_bare_OrOwn_pairs_with_a_marker_s_permission_where_it_is_the_one_permission()
    {
        var result = Run("[SignedIn] public sealed record RenameFolder([OnResource(\"folders.rename\")] FolderId Folder, [OrOwn] Guid User) : IBillingRequest;");

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.RenameFolder request => " + Requirement + ".All(" + Requirement + ".SignedIn(), " + Requirement + ".Any(");
    }

    // ------------------------------------------------------------------ inherited markers

    [Fact]
    public void A_derived_request_holds_to_the_markers_of_its_base_before_its_own()
    {
        var result = Run(
            """
            public abstract record DocumentCommand([property: OnResource("documents.edit")] DocumentId Document) : IBillingRequest;

            public sealed record MoveDocumentTo(DocumentId Document, [AtUnit("documents.open")] OrganizationUnitId To) : DocumentCommand(Document);
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.MoveDocumentTo request => " + Requirement + ".All(" + On + "(\"documents.edit\", request.Document), " + AtUnit + "(\"documents.open\", request.To)),");
        result.ShouldContain(Hint, "global::Shop.Billing.DocumentCommand request => " + On + "(\"documents.edit\", request.Document),");
    }
}
