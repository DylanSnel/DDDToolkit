using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The member template as an application meets it: one line declares a member class and names the resource it
/// is a member of, the generator derives it from the package's parent closed over the application's own ids,
/// and the class is a child entity of an aggregate that is the application's own. Two member classes are
/// declared in one project, the TestHost.
/// </summary>
public sealed class TemplateWiringTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Each_member_class_derives_from_the_parent_closed_over_its_own_ids()
    {
        typeof(DocumentShare).BaseType.Should().Be(typeof(MemberEntity<DocumentShareId, UserId, NamedRole>));
        typeof(FolderMember).BaseType.Should().Be(typeof(MemberEntity<FolderMemberId, StaffCode, NamedRole>));

        // The parent is a toolkit entity over the member class's own id.
        typeof(MemberEntity<DocumentShareId, UserId, NamedRole>).BaseType.Should().Be(typeof(Entity<DocumentShareId>));
    }

    [Fact]
    public void The_template_may_be_declared_once_for_each_kind_of_resource()
    {
        var template = typeof(MemberAttribute<,,,>).GetCustomAttribute<EntityTemplateAttribute>()!;

        template.Parent.Should().Be(typeof(MemberEntity<,,>));
        template.AllowSeveral.Should().BeTrue("an application has members on documents and on folders, each with a class of its own");
        typeof(MemberAttribute<,,,>).GetCustomAttributes<TemplateArgumentAttribute>().Should().BeEmpty(
            "every type is the application's to write where it declares the class: nothing is taken from another class");
    }

    [Fact]
    public void A_member_class_names_the_resource_it_is_a_member_of_and_the_member_itself_does_not_carry_it()
    {
        // The fourth type of the template is the resource: what the registration of that resource is named after
        // and closed over. The parent takes the first three, so a member knows nothing of whose it is.
        static Type[] DeclaredWith(Type memberClass)
            => memberClass.GetCustomAttributes().Single(attribute => attribute.GetType().IsGenericType && attribute.GetType().GetGenericTypeDefinition() == typeof(MemberAttribute<,,,>))
                .GetType().GetGenericArguments();

        DeclaredWith(typeof(DocumentShare)).Should().Equal(typeof(DocumentShareId), typeof(UserId), typeof(NamedRole), typeof(Document));
        DeclaredWith(typeof(FolderMember)).Should().Equal(typeof(FolderMemberId), typeof(StaffCode), typeof(NamedRole), typeof(Folder));
        typeof(MemberEntity<,,>).GetGenericArguments().Should().HaveCount(3);
    }

    [Fact]
    public void A_resources_aggregate_stays_the_applications_own()
    {
        // No package attribute and no package parent on the aggregate: it holds its members, and that is all.
        typeof(Document).BaseType.Should().Be(typeof(AggregateRoot<DocumentId>));
        typeof(Folder).BaseType.Should().Be(typeof(AggregateRoot<FolderId>));
        typeof(Document).GetCustomAttributes().Select(attribute => attribute.GetType().Namespace).Should().NotContain(typeof(MemberAttribute<,,,>).Namespace);
        typeof(Document).GetProperty(nameof(Document.Shares))!.PropertyType.Should().Be(typeof(IReadOnlyList<DocumentShare>));
    }

    [Fact]
    public void The_member_list_of_a_resource_is_written_by_the_toolkit_from_what_the_resource_declares()
    {
        static Type ListOf(Type resource) => resource.GetProperty("Members", BindingFlags.Instance | BindingFlags.NonPublic)!.PropertyType;

        // A document and a folder declare their collection, their owner and their codes, and no list: the
        // package's generator writes it, closed over the member class and the three types it was declared with.
        ListOf(typeof(Document)).Should().Be(typeof(MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>));
        ListOf(typeof(Folder)).Should().Be(typeof(MemberList<FolderMember, FolderMemberId, StaffCode, NamedRole>));
        Repository.Read("Tests/DDDToolkit.Supporting.Membership.TestHost", "Documents/Document.cs").Should().NotContain("MemberList<", "the host writes its own methods, and not the list");
        Repository.Read("Tests/DDDToolkit.Supporting.Membership.TestHost", "Folders/Folder.cs").Should().NotContain("MemberList<");

        // A binder keeps no codes of its own, so it writes its list itself, and the generator leaves that alone.
        ListOf(typeof(TestHost.Binders.Binder)).Should().Be(typeof(MemberList<TestHost.Binders.BinderBorrower, TestHost.Binders.BinderBorrowerId, UserId, NamedRole>));
        Repository.Read("Tests/DDDToolkit.Supporting.Membership.TestHost", "Binders/Binder.cs").Should().Contain("private MemberList<BinderBorrower, BinderBorrowerId, UserId, NamedRole> Members");

        // And the written list is the one the host's own methods work through: the owner is on a new document.
        var owner = UserId.CreateUnique();
        var document = new Document(DocumentId.CreateUnique(), "Minutes", owner, DocumentMembership.Owner, Now);
        document.Shares.Should().ContainSingle().Which.MemberId.Should().Be(owner);
        FluentActions.Invoking(() => document.ShareWith(owner, MemberPeriod.Open(Now), Now, by: owner))
            .Should().Throw<Exceptions.RefusalException>().Which.Code.Should().Be(DocumentRefusals.Membership[MembershipRefusals.AlreadyMember], "under the codes the document declares");
    }

    [Fact]
    public void A_member_class_is_checked_with_the_aggregate_that_holds_it_by_the_parents_rule_and_its_own()
    {
        var keeper = new StaffCode("K-001");
        var folder = new Folder(new FolderId(7), keeper, FolderMembership.Keeper, Now);
        var clerk = folder.Admit(new StaffCode("C-014"), FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: keeper);
        folder.GetInvariantViolations().Should().BeEmpty();

        // A field the host added starts at its default and is the host's to set, and the host's rule about it
        // runs next to the package's.
        clerk.Note.Should().BeNull();
        clerk.Annotate(new string('n', FolderMember.MaxNoteLength + 1));
        Break.ListOf<MemberRole<StaffCode, NamedRole>>(clerk, "_roles").Add(folder.Admit(new StaffCode("C-015"), FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: keeper).Roles.Single());

        var violations = folder.GetInvariantViolations();

        violations.Select(violation => violation.Code).Should().Equal([MembershipRefusals.RoleHeld, "folders.member-note"], "the parent's rules run first, the class's own after");
        violations.Should().OnlyContain(violation => violation.EntityType == typeof(FolderMember) && Equals(violation.EntityId, clerk.Id), "the application's class is the object that is wrong");
    }

    [Fact]
    public void Nothing_about_a_member_is_changed_from_outside_the_member_list()
    {
        foreach (var type in new[] { typeof(MemberEntity<,,>), typeof(MemberRole<,>) })
        {
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(property => property.SetMethod is { IsPublic: true } or { IsFamily: true } or { IsFamilyOrAssembly: true })
                .Select(property => type.Name + "." + property.Name)
                .Should().BeEmpty("a member's state is the member list's to change");

            type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Should().BeSubsetOf(
                    ["AppliesAt", "HoldsAt", "EndedBy", "EnsureInvariants", "EnsureOwnInvariants", "GetInvariantViolations", "GetOwnInvariantViolations"],
                    "what is public on " + type.Name + " only answers: about its periods, and, as the generator writes for every entity, about its rules");
        }

        // And nothing the package decides can be overridden by the application's class.
        typeof(MemberEntity<,,>).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => method.IsAbstract || (method.IsVirtual && !method.IsFinal && method.GetBaseDefinition() == method))
            .Select(method => method.Name)
            .Should().BeEmpty("a host adds members, rules and events of its own; it never overrides what the package decides");
        typeof(MemberList<,,,>).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void A_member_class_that_was_not_declared_with_the_template_cannot_be_made()
    {
        var list = new MemberList<HandWrittenShare, DocumentShareId, UserId, NamedRole>([], UserId.CreateSequential(), DocumentRefusals.Membership);

        FluentActions.Invoking(() => list.Add(UserId.CreateSequential(), MemberPeriod.Open(Now), Now))
            .Should().Throw<Exception>()
            .Which.GetBaseException().Message.Should().Contain("HandWrittenShare").And.Contain("[Member<");
    }

    [Fact]
    public void A_named_role_is_its_name()
    {
        var editor = new NamedRole("contributor");

        editor.Value.Should().Be("contributor");
        editor.ToString().Should().Be("contributor");
        editor.Should().Be(new NamedRole("contributor")).And.NotBe(new NamedRole("Contributor"));
        NamedRole.Parse("contributor").Should().Be(editor);
        NamedRole.MaxLength.Should().Be(64);
        default(NamedRole).IsEmpty.Should().BeTrue();
    }

    /// <summary>A member class derived by hand, with only a constructor the package cannot call.</summary>
    private sealed class HandWrittenShare : MemberEntity<DocumentShareId, UserId, NamedRole>
    {
        public HandWrittenShare(string origin) => Origin = origin;

        public string Origin { get; }
    }
}
