using System.Reflection;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// The managers Membership's generator writes beside a resource's member list: the member manager named after the member
/// class, the role manager named after each role class, in the global namespace, and the resource's and the role's hooks,
/// partial methods the application writes its guard and its events in. A manager is written where the list is, and a
/// type of its name the project declares keeps it out.
/// </summary>
public class MemberManagerGeneratorTests
{
    /// <summary>A document whose guard and events are its hooks, and nothing else of its members' changes.</summary>
    private const string Hooked =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Exceptions;
        using DDDToolkit.Supporting.Membership;

        namespace Shop;

        [Member<DocumentShareId, UserId, NamedRole, Document>]
        public sealed partial class DocumentShare;

        [AggregateRoot<DocumentId>]
        public sealed partial class Document
        {
            public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");

            public Document(DocumentId id, UserId owner, DateTimeOffset now) : base(id)
            {
                OwnerId = owner;
                Members.Open(now);
            }

            [Owner] public UserId OwnerId { get; private set; }

            public partial IReadOnlyList<DocumentShare> Shares { get; }

            public bool Archived { get; set; }

            public List<string> Said { get; } = [];

            partial void OnChangingMembers()
            {
                if (Archived)
                {
                    throw new RefusalException("documents.archived", RefusalKind.Conflict, "Archived.");
                }
            }

            partial void OnMemberAdded(DocumentShare member) => Said.Add("added " + member.MemberId);

            partial void OnRoleGiven(UserId member, MemberRole<UserId, NamedRole> role) => Said.Add("given " + role.RoleId);

            partial void OnOwnerNamed(OwnerNamed<UserId, NamedRole> named) => Said.Add("owner " + named.Owner);
        }

        public static class Manage
        {
            public static void Add(Document document, UserId member, NamedRole? role, DateTimeOffset now)
                => ((IMembershipResource<DocumentShare, UserId, NamedRole>)document).AddMember(member, role, MemberPeriod.Open(now), now, null);

            public static void NameOwner(Document document, UserId member, DateTimeOffset now)
                => ((IMembershipResource<DocumentShare, UserId, NamedRole>)document).NameOwner(member, FormerOwner.Leaves, now);

            public static IReadOnlyList<Type> Managers() => Of<Document>();

            private static IReadOnlyList<Type> Of<T>()
                where T : IMembershipResource<DocumentShare, UserId, NamedRole>
                => T.MembershipManagers;
        }
        """;

    private const string Roles =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Membership;
        using DDDToolkit.Supporting.Membership.Access;

        namespace Shop;

        [EntityId<Guid>]
        public readonly partial record struct DocumentRoleId;

        [CustomRole<DocumentRoleId, Document>]
        public sealed partial class DocumentRole
        {
            public DocumentRole(DocumentRoleId id, CustomRoleDraft draft, MembershipRules rules) : base(id, draft, rules) { }

            public int Renamed { get; private set; }

            partial void OnRenamed() => Renamed++;
        }
        """;

    private static GeneratorRunOutcome Run(params string[] sources) => MemberListGeneratorTests.Run(sources);

    [Fact]
    public void The_member_manager_is_written_in_the_global_namespace_named_after_the_member_class_and_closed_over_the_resource()
    {
        var result = Run(MemberListGeneratorTests.Documents);

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();

        var manager = result.Source("DocumentShareManager");
        manager.Should().NotContain("namespace", "the manager is in the global namespace, as Tenancy's are");
        manager.Should().Contain(
            "public sealed class DocumentShareManager : global::DDDToolkit.Supporting.Membership.Managers.MemberManager<global::Shop.Document, global::Shop.DocumentId, "
            + "global::Shop.DocumentShare, global::Shop.DocumentShareId, global::Shop.UserId, global::DDDToolkit.Supporting.Membership.NamedRole>");
        manager.Should().Contain("    : base(access, store, user, clock, roles, directory, policy, ownPlace)");

        // The way to the members is explicit, so nothing of it shows on the document; the hooks are partial methods.
        var resource = result.Source("Document.MemberManagement.");
        resource.Should().Contain("partial class Document : global::DDDToolkit.Supporting.Membership.IMembershipResource<global::Shop.DocumentShare, global::Shop.UserId, global::DDDToolkit.Supporting.Membership.NamedRole>");
        foreach (var hook in new[]
                 {
                     "partial void OnChangingMembers();",
                     "partial void OnMemberAdded(global::Shop.DocumentShare member);",
                     "partial void OnMemberRemoved(global::Shop.UserId member);",
                     "partial void OnRoleGiven(global::Shop.UserId member, global::DDDToolkit.Supporting.Membership.MemberRole<global::Shop.UserId, global::DDDToolkit.Supporting.Membership.NamedRole> role);",
                     "partial void OnRoleTaken(global::Shop.UserId member, global::DDDToolkit.Supporting.Membership.MemberRole<global::Shop.UserId, global::DDDToolkit.Supporting.Membership.NamedRole> role);",
                     "partial void OnOwnerNamed(global::DDDToolkit.Supporting.Membership.OwnerNamed<global::Shop.UserId, global::DDDToolkit.Supporting.Membership.NamedRole> named);",
                 })
        {
            resource.Should().Contain(hook);
        }

        resource.Should().Contain("    [typeof(global::DocumentShareManager)];", "the resource names the managers its registration registers");
        resource.Should().Contain("OwnerId = named.Owner;", "naming an owner sets the property marked [Owner]");
    }

    [Fact]
    public void Each_change_calls_the_guard_then_the_list_then_the_hook_that_says_what_happened()
    {
        var emitted = Run(Hooked).Emit();
        var now = DateTimeOffset.UtcNow;
        var owner = emitted.CallStatic("Shop.UserId", "CreateUnique");
        var reader = emitted.CallStatic("Shop.UserId", "CreateUnique");
        var document = emitted.New("Shop.Document", emitted.CallStatic("Shop.DocumentId", "CreateUnique"), owner, now);

        emitted.CallStatic("Shop.Manage", "Add", document, reader, new NamedRole("onlooker"), now);
        ((List<string>)emitted.Property(document, "Said")!).Should().Equal("added " + reader, "given onlooker");

        emitted.CallStatic("Shop.Manage", "NameOwner", document, reader, now);
        emitted.Property(document, "OwnerId").Should().Be(reader);
        ((List<string>)emitted.Property(document, "Said")!).Last().Should().Be("owner " + reader);

        // The guard runs in front of the list: an archived document refuses before anything changes.
        emitted.Call(document, "set_Archived", true);
        FluentActions.Invoking(() => emitted.CallStatic("Shop.Manage", "Add", document, emitted.CallStatic("Shop.UserId", "CreateUnique"), null, now))
            .Should().Throw<TargetInvocationException>().WithInnerException<RefusalException>().Which.Code.Should().Be("documents.archived");
        ((System.Collections.IEnumerable)emitted.Property(document, "Shares")!).Cast<object>().Should().HaveCount(1, "the new owner, and the old owner left");

        ((IReadOnlyList<Type>)emitted.CallStatic("Shop.Manage", "Managers")!).Select(type => type.Name).Should().Equal("DocumentShareManager");
    }

    [Fact]
    public void A_role_class_gets_its_hooks_and_a_role_manager_which_the_resource_names_beside_its_member_manager()
    {
        var result = Run(Hooked, Roles);

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            "DocumentRoleManager",
            "public sealed class DocumentRoleManager : global::DDDToolkit.Supporting.Membership.Managers.CustomRoleManager<global::Shop.DocumentRole, global::Shop.DocumentRoleId, global::Shop.Document>");
        result.ShouldContain("DocumentRole.RoleHooks.", "partial class DocumentRole : global::DDDToolkit.Supporting.Membership.ICustomRoleHooks");
        result.ShouldContain("DocumentRole.RoleHooks.", "partial void OnPermissionsSet(global::DDDToolkit.Supporting.Membership.RolePermissionsSet set);");
        result.ShouldContain("Document.MemberManagement.", "    [typeof(global::DocumentShareManager), typeof(global::DocumentRoleManager)];");

        var emitted = result.Emit();
        ((IReadOnlyList<Type>)emitted.CallStatic("Shop.Manage", "Managers")!).Select(type => type.Name).Should().Equal("DocumentShareManager", "DocumentRoleManager");
    }

    [Fact]
    public void A_type_of_the_managers_name_keeps_it_out_and_one_in_a_namespace_is_told_why()
    {
        // In a namespace: the manager in the global namespace would hide it wherever that namespace is imported.
        var namespaced = Run(MemberListGeneratorTests.Documents, "namespace Shop.Sharing;\n\npublic sealed class DocumentShareManager;\n");
        namespaced.ShouldCompile();
        namespaced.ShouldNotHaveGeneratedFor("DocumentShareManager.g");
        namespaced.ShouldHaveDiagnostic("DDD00106", at: "DocumentShareManager").GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Be(
            "'DocumentShareManager' is not written for 'Document': the project declares a type of that name in 'Shop.Sharing', which the manager in the global namespace "
            + "would hide wherever that namespace is imported: rename that type");
        namespaced.ShouldContain("Document.MemberManagement.", "    [];", "the hooks are written all the same, and the resource names no manager");

        // In the global namespace: the project wrote its own, and hears nothing.
        var global = Run(MemberListGeneratorTests.Documents, "public sealed class DocumentShareManager;\n");
        global.ShouldCompile();
        global.ShouldNotHaveGeneratedFor("DocumentShareManager.g");
        global.ShouldNotHaveDiagnostic("DDD00106");
    }

    [Fact]
    public void No_manager_is_written_where_the_member_list_is_not()
    {
        // No [Owner]: DDD00059 says why there is no list, and so there is no manager either.
        var unwritten = Run(
            """
            using System.Collections.Generic;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Membership;

            namespace Shop;

            [Member<DocumentShareId, UserId, NamedRole, Document>]
            public sealed partial class DocumentShare;

            [AggregateRoot<DocumentId>]
            public sealed partial class Document
            {
                public Document(DocumentId id) : base(id) { }

                public UserId OwnerId { get; private set; }

                public partial IReadOnlyList<DocumentShare> Shares { get; }
            }
            """);
        unwritten.ShouldHaveDiagnostic("DDD00059", at: "Document");
        unwritten.ShouldNotHaveGeneratedFor("DocumentShareManager");
        unwritten.ShouldNotHaveGeneratedFor("Document.MemberManagement.");
        unwritten.ShouldNotHaveDiagnostic("DDD00106");

        // A list the resource declares itself: no list is written, no manager, and nothing is said.
        var declared = Run(
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
                public Document(DocumentId id) : base(id) { }

                [Owner] public UserId OwnerId { get; private set; }

                public partial IReadOnlyList<DocumentShare> Shares { get; }

                private MemberList<DocumentShare, DocumentShareId, UserId, NamedRole> Members => new(_shares, OwnerId, MembershipCodes.Under("documents"));
            }
            """);
        declared.ShouldCompile();
        declared.ShouldNotHaveGeneratedFor("DocumentShareManager");
        declared.ReportedDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void An_owner_the_class_cannot_set_keeps_the_manager_out_and_says_why()
    {
        var result = Run(
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

                public Document(DocumentId id, UserId owner, DateTimeOffset now) : base(id)
                {
                    OwnerId = owner;
                    Members.Open(now);
                }

                [Owner] public UserId OwnerId { get; }

                public partial IReadOnlyList<DocumentShare> Shares { get; }
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveGenerated("Document.Members.");
        result.ShouldNotHaveGeneratedFor("DocumentShareManager");
        result.ShouldNotHaveGeneratedFor("Document.MemberManagement.");
        result.ShouldHaveDiagnostic("DDD00106", at: "DocumentShare").GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("its owner, 'OwnerId', has no setter in the class");
    }
}
