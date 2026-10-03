using System.Reflection;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// The member list of a resource, as the generator of DDDToolkit.Supporting.Membership writes it: a private
/// property <c>Members</c> on the resource a member class names, from the collection, the owner and the codes
/// that resource declares. An application writes its own methods and guards and not that line, and a shape
/// the generator cannot tell without a guess gets no list and is told why, unless the resource writes its list
/// itself.
/// <para>
/// The package is the real one, seen through metadata the way a domain project sees it, without any storage,
/// and each snippet is compiled with what the generators wrote.
/// </para>
/// </summary>
public class MemberListGeneratorTests
{
    private const string Ids =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Shop;

        [EntityId<Guid>]
        public readonly partial record struct UserId;

        [EntityId<Guid>]
        public readonly partial record struct DocumentId;

        [EntityId<Guid>]
        public readonly partial record struct DocumentShareId;
        """;

    /// <summary>A document as an application writes it: the member class in one line, and no member list.</summary>
    internal const string Documents =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Membership;

        namespace Shop;

        [Member<DocumentShareId, UserId, NamedRole, Document>]
        public sealed partial class DocumentShare;

        [AggregateRoot<DocumentId>]
        public sealed partial class Document
        {
            public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

            public Document(DocumentId id, UserId owner, NamedRole ownerRole, DateTimeOffset now) : base(id)
            {
                OwnerId = owner;
                Members.Open(ownerRole, now);
            }

            public UserId OwnerId { get; private set; }

            public partial IReadOnlyList<DocumentShare> Shares { get; }

            public void ShareWith(UserId user, NamedRole role, MemberPeriod period, DateTimeOffset now) => Members.Add(user, role, period, now);
        }
        """;

    /// <summary>A document whose body is the test's to write, so each test says only what differs.</summary>
    private static string Document(string body, string memberTemplate = "Member<DocumentShareId, UserId, NamedRole, Document>")
        => $$"""
             using System;
             using System.Collections.Generic;
             using DDDToolkit.Abstractions.Attributes;
             using DDDToolkit.Supporting.Membership;

             namespace Shop;

             [{{memberTemplate}}]
             public sealed partial class DocumentShare;

             [AggregateRoot<DocumentId>]
             public sealed partial class Document
             {
                 public Document(DocumentId id) : base(id) { }

             {{body}}
             }
             """;

    internal static GeneratorRunOutcome Run(params string[] sources)
    {
        var host = GeneratorTestHost.Create(Ids, "Ids.cs");
        for (var index = 0; index < sources.Length; index++)
        {
            host = host.WithSource(sources[index], "Source" + index + ".cs");
        }

        return host.WithMembershipAlone().RunCoreAnd(GeneratorTestHost.MemberListGenerators());
    }

    private static string Reported(GeneratorRunOutcome result, string at)
        => result.ShouldHaveDiagnostic("DDD00059", at).GetMessage(System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ what is written

    [Fact]
    public void The_member_list_is_written_on_the_resource_from_its_collection_its_owner_and_its_codes()
    {
        var result = Run(Documents);

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();

        var source = result.Source("Document.Members.");
        source.Should().Contain("namespace Shop;");
        source.Should().Contain("partial class Document");
        source.Should().Contain(
            "private global::DDDToolkit.Supporting.Membership.MemberList<global::Shop.DocumentShare, global::Shop.DocumentShareId, global::Shop.UserId, global::DDDToolkit.Supporting.Membership.NamedRole> Members",
            "the four types are the member class and the three its template was declared with");
        source.Should().Contain(
            "=> new(_shares, OwnerId, global::Shop.DocumentShareId.CreateSequential, Codes);",
            "the list of the collection, the one property of the member's id, a new id in time order, and the one static MembershipCodes");
    }

    [Fact]
    public void The_written_list_changes_the_resources_own_collection_and_refuses_under_its_codes()
    {
        var emitted = Run(Documents).Emit();
        var now = DateTimeOffset.UtcNow;
        var owner = emitted.CallStatic("Shop.UserId", "CreateUnique");
        var reader = emitted.CallStatic("Shop.UserId", "CreateUnique");

        var document = emitted.New("Shop.Document", emitted.CallStatic("Shop.DocumentId", "CreateUnique"), owner, new NamedRole("owner"), now);
        emitted.Call(document, "ShareWith", reader, new NamedRole("onlooker"), MemberPeriod.Open(now), now);

        var shares = ((System.Collections.IEnumerable)emitted.Property(document, "Shares")!).Cast<object>().ToList();
        shares.Should().HaveCount(2, "the owner the document was opened with, and the user it was shared with");
        shares.Select(share => emitted.Property(share, "MemberId")).Should().Equal(owner, reader);

        // The rules are the package's, and what a caller reads is the resource's own code for them.
        var again = FluentActions.Invoking(() => emitted.Call(document, "ShareWith", reader, new NamedRole("onlooker"), MemberPeriod.Open(now), now))
            .Should().Throw<TargetInvocationException>().WithInnerException<RefusalException>().Which;
        again.Code.Should().Be("documents.already-member");
    }

    [Fact]
    public void Each_resource_of_a_project_gets_a_list_of_its_own()
    {
        var result = Run(
            Documents,
            """
            using System;
            using System.Collections.Generic;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Membership;

            namespace Shop;

            [EntityId<Guid>]
            public readonly partial record struct FolderMemberId;

            [EntityId<string>]
            public readonly partial record struct StaffCode;

            [Member<FolderMemberId, StaffCode, NamedRole, Folder>]
            public sealed partial class FolderMember;

            [AggregateRoot<long>]
            public sealed partial class Folder
            {
                private static readonly MembershipCodes FolderCodes = MembershipCodes.Under("folders");

                public Folder(FolderId id, StaffCode keeper) : base(id) => Keeper = keeper;

                public StaffCode Keeper { get; private set; }

                // Another property of an id, and not of the member's: the owner is the one of StaffCode.
                public UserId CreatedBy { get; private set; }

                public partial IEnumerable<FolderMember> Staff { get; }

                public void Admit(StaffCode staff, MemberPeriod period, DateTimeOffset now) => Members.Add(staff, period, now);
            }
            """);

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain("Document.Members.", "=> new(_shares, OwnerId, global::Shop.DocumentShareId.CreateSequential, Codes);");
        result.ShouldContain(
            "Folder.Members.",
            "=> new(_staff, Keeper, global::Shop.FolderMemberId.CreateSequential, FolderCodes);",
            "the owner and the codes are found by what they are, whatever they are called, and a field holds the codes as well as a property does");
    }

    [Fact]
    public void A_resource_nested_in_another_class_gets_its_list_where_it_is_declared()
    {
        var result = Run(
            """
            using System;
            using System.Collections.Generic;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Membership;

            namespace Shop;

            [Member<DocumentShareId, UserId, NamedRole, Archive.Document>]
            public sealed partial class DocumentShare;

            public static partial class Archive
            {
                [AggregateRoot<DocumentId>]
                public sealed partial class Document
                {
                    private static MembershipCodes Codes => MembershipCodes.Under("documents");

                    public Document(DocumentId id, UserId owner) : base(id) => OwnerId = owner;

                    public UserId OwnerId { get; private set; }

                    public partial IReadOnlyCollection<DocumentShare> Shares { get; }

                    public void ShareWith(UserId user, MemberPeriod period, DateTimeOffset now) => Members.Add(user, period, now);
                }
            }
            """);

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain("Document.Members.", "partial class Archive");
    }

    [Fact]
    public void An_id_of_another_project_makes_the_new_rows_as_well()
    {
        // The ids of a module usually live in a project of their own, which the domain project sees as metadata.
        var result = GeneratorTestHost.Create(Documents, "Documents.cs")
            .WithMembershipAlone()
            .WithReferencedAssembly(Ids, "Shop.Contracts")
            .RunCoreAnd(GeneratorTestHost.MemberListGenerators());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain("Document.Members.", "global::Shop.DocumentShareId.CreateSequential");
    }

    // ------------------------------------------------------------------ what is left alone

    [Theory]
    [InlineData("Members")]
    [InlineData("Sharing")]
    public void A_resource_that_writes_its_list_itself_is_left_alone_and_hears_nothing(string name)
    {
        // Two properties of the member's id and codes kept elsewhere: nothing the list could be written from,
        // and nothing to say about it, since the class has its list.
        var result = Run(
            Document(
                $$"""
                  public UserId OwnerId { get; private set; }

                  public UserId WrittenBy { get; private set; }

                  public partial IReadOnlyList<DocumentShare> Shares { get; }

                  private MemberList<DocumentShare, DocumentShareId, UserId, NamedRole> {{name}}
                      => new(_shares, OwnerId, DocumentShareId.CreateSequential, DocumentRefusals.Membership);

                  public void ShareWith(UserId user, MemberPeriod period, DateTimeOffset now) => {{name}}.Add(user, period, now);
                  """),
            """
            using DDDToolkit.Supporting.Membership;

            namespace Shop;

            public static class DocumentRefusals
            {
                public static MembershipCodes Membership { get; } = MembershipCodes.Under("documents");
            }
            """);

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldNotHaveGeneratedFor("Document.Members");
    }

    [Fact]
    public void A_resource_of_a_project_that_does_not_declare_it_is_not_written_into()
    {
        // The member class names a class this project only references: there is no part of it to write here.
        var result = GeneratorTestHost.Create(Ids, "Ids.cs")
            .WithMembershipAlone()
            .WithReferencedAssembly(
                """
                namespace Library;

                public class Shelf;
                """,
                "Library")
            .WithSource(
                """
                using DDDToolkit.Supporting.Membership;

                namespace Shop;

                [Member<DocumentShareId, UserId, NamedRole, Library.Shelf>]
                public sealed partial class ShelfReader;
                """,
                "ShelfReader.cs")
            .RunCoreAnd(GeneratorTestHost.MemberListGenerators());

        result.ShouldNotHaveDiagnostic("DDD00059");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains(".Members.", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ what cannot be told

    [Fact]
    public void A_resource_with_two_properties_of_the_members_id_is_given_no_list_and_told_which_they_are()
    {
        var result = Run(Document(
            """
                public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

                public UserId OwnerId { get; private set; }

                public UserId WrittenBy { get; private set; }

                public partial IReadOnlyList<DocumentShare> Shares { get; }
            """));

        result.ShouldNotHaveGeneratedFor("Document.Members");
        result.Count("DDD00059").Should().Be(1);
        Reported(result, at: "Document").Should()
            .StartWith("'Document' has no member list, and the toolkit cannot write one over its 'DocumentShare': it declares more than one property of 'UserId' (OwnerId, WrittenBy), so which of them is its owner cannot be told.")
            .And.EndWith("write the list yourself: private MemberList<DocumentShare, DocumentShareId, UserId, NamedRole> Members => new(members, owner, newId, codes);.");
    }

    [Fact]
    public void A_resource_without_an_owner_or_codes_of_its_own_is_told_everything_that_is_missing_at_once()
    {
        var result = Run(Document(
            """
                public partial IReadOnlyList<DocumentShare> Shares { get; }
            """));

        result.ShouldNotHaveGeneratedFor("Document.Members");
        Reported(result, at: "Document").Should()
            .Contain("it declares no property of 'UserId', which its owner is kept in; ")
            .And.Contain("it declares no static property or field of MembershipCodes, the codes the rules about its members refuse under.");
    }

    [Fact]
    public void A_resource_with_two_sets_of_codes_is_told_which_they_are()
    {
        var result = Run(Document(
            """
                public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

                private static readonly MembershipCodes Older = MembershipCodes.Under("papers");

                public UserId OwnerId { get; private set; }

                public partial IReadOnlyList<DocumentShare> Shares { get; }
            """));

        Reported(result, at: "Document").Should().Contain("it declares more than one static member of MembershipCodes (Codes, Older)");
    }

    [Fact]
    public void A_resource_without_a_collection_the_toolkit_keeps_in_a_list_is_told_which_collections_are()
    {
        // A set is kept in a HashSet, which a member list is not made over.
        var result = Run(Document(
            """
                public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

                public UserId OwnerId { get; private set; }

                public partial IReadOnlySet<DocumentShare> Shares { get; }
            """));

        Reported(result, at: "Document").Should().Contain(
            "it declares no collection of 'DocumentShare' the toolkit keeps in a list: a get-only partial property of IReadOnlyList<DocumentShare>, IReadOnlyCollection<DocumentShare> or IEnumerable<DocumentShare>");
    }

    [Fact]
    public void A_resource_with_two_collections_of_its_members_is_told_which_they_are()
    {
        var result = Run(Document(
            """
                public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

                public UserId OwnerId { get; private set; }

                public partial IReadOnlyList<DocumentShare> Shares { get; }

                public partial IReadOnlyList<DocumentShare> Former { get; }
            """));

        Reported(result, at: "Document").Should().Contain("it declares more than one collection of 'DocumentShare' (Shares, Former), so which of them holds its members cannot be told");
    }

    [Fact]
    public void A_member_row_keyed_by_something_else_than_a_guid_is_told_that_no_new_id_can_be_made()
    {
        var result = Run(
            """
            using DDDToolkit.Abstractions.Attributes;

            namespace Shop;

            [EntityId<long>]
            public readonly partial record struct ShareNumber;
            """,
            Document(
                """
                    public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

                    public UserId OwnerId { get; private set; }

                    public partial IReadOnlyList<DocumentShare> Shares { get; }
                """,
                memberTemplate: "Member<ShareNumber, UserId, NamedRole, Document>"));

        Reported(result, at: "Document").Should().Contain("'ShareNumber', the id of a member's row, is not an [EntityId<Guid>], so there is no telling how a new one is made");
    }

    [Fact]
    public void A_resource_whose_collection_is_called_Members_is_told_the_name_is_taken()
    {
        var result = Run(Document(
            """
                public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

                public UserId OwnerId { get; private set; }

                public partial IReadOnlyList<DocumentShare> Members { get; }
            """));

        result.ShouldCompile();
        result.ShouldNotHaveGeneratedFor("Document.Members");
        Reported(result, at: "Document").Should().Contain("it has a member called Members already, which the list would be called too");
    }

    [Fact]
    public void Two_member_classes_of_one_resource_get_no_list_since_both_would_be_called_the_same()
    {
        var result = Run(
            Document(
                """
                    public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

                    public UserId OwnerId { get; private set; }

                    public partial IReadOnlyList<DocumentShare> Shares { get; }

                    public partial IReadOnlyList<DocumentWatcher> Watchers { get; }
                """),
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Membership;

            namespace Shop;

            [EntityId<Guid>]
            public readonly partial record struct DocumentWatcherId;

            [Member<DocumentWatcherId, UserId, NamedRole, Document>]
            public sealed partial class DocumentWatcher;
            """);

        // The document keeps both, so there is no telling which is its member class: no list, said once.
        result.ShouldCompile();
        result.ShouldNotHaveGeneratedFor("Document.Members");
        result.Count("DDD00059").Should().Be(1, "the cause is one, whatever the number of member classes");
        Reported(result, at: "Document").Should().Contain("it is the resource of more than one member class ('DocumentShare', 'DocumentWatcher'), and each list needs a name of its own");
        result.Count("DDD00060").Should().Be(0);
    }

    [Fact]
    public void A_second_member_class_of_a_resource_that_keeps_its_members_as_the_first_is_said_once_on_the_second()
    {
        // The document keeps its shares, and the watcher names it too. The shares are its members: their list is
        // written, so the document's own methods compile, and the watcher is the class to fix.
        var result = Run(
            Documents,
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Membership;

            namespace Shop;

            [EntityId<Guid>]
            public readonly partial record struct DocumentWatcherId;

            [Member<DocumentWatcherId, UserId, NamedRole, Document>]
            public sealed partial class DocumentWatcher;
            """);

        result.ShouldCompile();
        result.Source("Document.Members.").Should().Contain("MemberList<global::Shop.DocumentShare,");
        result.ReportedDiagnostics.Should().ContainSingle("one cause, said once, and nothing else: no member list is missing")
            .Which.Id.Should().Be("DDD00060");
        result.ShouldHaveDiagnostic("DDD00060", at: "DocumentWatcher").GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Be(
            "'DocumentWatcher' is declared a member of 'Document', and 'Document' keeps its members as 'DocumentShare' already, in 'Shares': a resource has one member class. "
            + "Name the resource this class is a member of, or remove it");
    }

    [Fact]
    public void Where_the_registrations_say_it_a_second_member_class_is_not_said_again()
    {
        // A project that also gets the package's registrations hears the two classes from them, as an error, once.
        var result = GeneratorTestHost.Create(Ids, "Ids.cs")
            .WithSource(Documents, "Documents.cs")
            .WithSource(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Supporting.Membership;

                namespace Shop;

                [EntityId<Guid>]
                public readonly partial record struct DocumentWatcherId;

                [Member<DocumentWatcherId, UserId, NamedRole, Document>]
                public sealed partial class DocumentWatcher;
                """,
                "Watchers.cs")
            .WithMembership()
            .RunCoreAnd(GeneratorTestHost.MemberListGenerators());

        result.ReportedDiagnostics.Select(diagnostic => diagnostic.Id).Should().Equal(["DDD00045"], "the registrations say it, and the member list is written for the class the document keeps");
        result.ShouldHaveDiagnostic("DDD00045", at: "DocumentWatcher");
        result.Source("Document.Members.").Should().Contain("MemberList<global::Shop.DocumentShare,");
    }

    [Fact]
    public void A_member_known_by_an_id_another_generator_writes_is_told_to_declare_that_id()
    {
        // The id of an aggregate declared over a raw value is written by the entity generator, and one generator
        // never sees what another writes: the list cannot be closed over it, and saying nothing would leave the
        // application with "Members does not exist" and no reason.
        var result = Run(
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;

            namespace Shop;

            [AggregateRoot<Guid>]
            public sealed partial class Patron;
            """,
            Document(
                """
                    public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

                    public PatronId OwnerId { get; private set; }

                    public partial IReadOnlyList<DocumentShare> Shares { get; }
                """,
                memberTemplate: "Member<DocumentShareId, PatronId, NamedRole, Document>"));

        result.ShouldNotHaveGeneratedFor("Document.Members");
        result.Count("DDD00059").Should().Be(1);
        Reported(result, at: "Document").Should().Contain(
            "'PatronId' is a type it cannot see, one another generator writes or nobody declares: declare it yourself, with [EntityId<T>], where the member class can see it",
            "the one thing that stands in the way")
            .And.NotContain("property of 'PatronId'", "and not the owner, which it could not judge without the type");
    }

    [Fact]
    public void A_resource_that_writes_its_list_itself_over_an_id_another_generator_writes_hears_nothing()
    {
        var result = Run(
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;

            namespace Shop;

            [AggregateRoot<Guid>]
            public sealed partial class Patron;
            """,
            Document(
                """
                    public PatronId OwnerId { get; private set; }

                    public partial IReadOnlyList<DocumentShare> Shares { get; }

                    private MemberList<DocumentShare, DocumentShareId, PatronId, NamedRole> Members
                        => new(_shares, OwnerId, DocumentShareId.CreateSequential, MembershipCodes.Under("documents"));

                    public void ShareWith(PatronId patron, MemberPeriod period, DateTimeOffset now) => Members.Add(patron, period, now);
                """,
                memberTemplate: "Member<DocumentShareId, PatronId, NamedRole, Document>"));

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00059");
        result.ShouldNotHaveGeneratedFor("Document.Members");
    }

    [Fact]
    public void A_resource_that_is_no_aggregate_root_is_told_that_no_collection_of_it_is_kept()
    {
        var result = Run(
            """
            using DDDToolkit.Supporting.Membership;

            namespace Shop;

            [Member<DocumentShareId, UserId, NamedRole, Document>]
            public sealed partial class DocumentShare;

            public sealed partial class Document
            {
                public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

                public UserId OwnerId { get; private set; }
            }
            """);

        // Said of the member class, which names it, and not of the document, which has no members to keep.
        result.ReportedDiagnostics.Should().ContainSingle().Which.Id.Should().Be("DDD00060");
        result.ShouldHaveDiagnostic("DDD00060", at: "DocumentShare").GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Be(
            "'DocumentShare' is declared a member of 'Document', and 'Document' is not declared an aggregate root: members are kept with the aggregate root they belong to, "
            + "and their list on it. Name that aggregate root as the resource");
    }

    [Fact]
    public void A_member_class_that_names_itself_as_its_resource_is_told_so_on_itself_and_nothing_else_is_said()
    {
        // The member class is an entity of the document's: no aggregate root, and no member list to speak of.
        var result = Run(Document("    public UserId OwnerId { get; private set; }\n\n    public partial IReadOnlyList<DocumentShare> Shares { get; }", memberTemplate: "Member<DocumentShareId, UserId, NamedRole, DocumentShare>"));

        result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id.StartsWith("DDD", StringComparison.Ordinal)).Should().ContainSingle()
            .Which.Id.Should().Be("DDD00060", "no member list of the document is missing: no member class names it");
        result.ShouldHaveDiagnostic("DDD00060", at: "DocumentShare").GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("'DocumentShare' is declared a member of 'DocumentShare', and 'DocumentShare' is not declared an aggregate root");
    }

    [Fact]
    public void The_warning_is_one_a_project_can_turn_off()
    {
        var result = GeneratorTestHost.Create(Ids, "Ids.cs")
            .WithSource(Document("    public partial IReadOnlyList<DocumentShare> Shares { get; }"), "Document.cs")
            .WithMembershipAlone()
            .WithNoWarn("DDD00059")
            .RunCoreAnd(GeneratorTestHost.MemberListGenerators());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == "DDD00059" && !diagnostic.IsSuppressed);
    }

    [Fact]
    public void A_project_that_does_not_reference_the_package_gets_nothing_and_hears_nothing()
    {
        var result = GeneratorTestHost.Create(Ids, "Ids.cs").RunCoreAnd(GeneratorTestHost.MemberListGenerators());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
    }
}
