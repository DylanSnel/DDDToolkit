using System.Reflection;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// The registration of DDDToolkit.Supporting.Membership as an application gets it: named after the resource
/// its member class names, <c>AddDocumentMembership</c>, and closed over the member class, its types, the
/// resource and the resource's id, so the application says only which context maps the resource. The name is
/// the same whether a project declares one member class or several, so a second resource changes no call
/// that was there. Beside it is a second registration of the same name, which takes the application's class
/// that answers what the resource's rules ask of it, next to the context; and a third, named after the
/// resource as well, <c>AddDocumentMemberAccess</c>, which adds the resource's access check for the request
/// interface a module names.
/// <para>
/// The package is the real one, seen through metadata the way an application sees it, and each snippet is
/// compiled with what the generator wrote.
/// </para>
/// </summary>
public class MembershipRegistrationTests
{
    private const string Documents =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Membership;

        namespace Shop;

        [EntityId<Guid>]
        public readonly partial record struct UserId;

        [EntityId<Guid>]
        public readonly partial record struct DocumentId;

        [EntityId<Guid>]
        public readonly partial record struct DocumentShareId;

        [Member<DocumentShareId, UserId, NamedRole, Document>]
        public sealed partial class DocumentShare;

        [AggregateRoot<DocumentId>]
        public sealed partial class Document
        {
            public Document(DocumentId id, UserId owner) : base(id) => OwnerId = owner;

            public UserId OwnerId { get; private set; }

            public partial IReadOnlyList<DocumentShare> Shares { get; }
        }
        """;

    /// <summary>A second resource, declared over a raw value: its id, <c>FolderId</c>, is generated beside it.</summary>
    private const string Folders =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Membership;

        namespace Shop;

        [EntityId<Guid>]
        public readonly partial record struct FolderMemberId;

        [Member<FolderMemberId, UserId, NamedRole, Folder>]
        public sealed partial class FolderMember;

        [AggregateRoot<long>]
        public sealed partial class Folder
        {
            public Folder(FolderId id, UserId keeper) : base(id) => Keeper = keeper;

            public UserId Keeper { get; private set; }

            public partial IReadOnlyList<FolderMember> Staff { get; }
        }
        """;

    private static string Startup(string calls)
        => $$"""
             using DDDToolkit.Supporting.Membership.Access;
             using DDDToolkit.Supporting.Membership.EntityFramework;
             using Microsoft.EntityFrameworkCore;
             using Microsoft.Extensions.DependencyInjection;

             namespace Shop;

             public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options);

             public static class Startup
             {
                 public static IServiceCollection Register(IServiceCollection services, MembershipRules rules)
                 {
                     {{calls}}
                     return services;
                 }
             }
             """;

    private const string File = "MembershipEntityFrameworkServiceCollectionExtensions.AddMembership.Registration.";

    private const string AccessFile = "MembershipEntityFrameworkServiceCollectionExtensions.AddMemberAccess.Registration.";

    private static string[] RegistrationsOf(GeneratorRunOutcome result)
        => [.. result.Emit().Type("DDDToolkit.Supporting.Membership.EntityFramework.GeneratedMembershipEntityFrameworkServiceCollectionExtensions")
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name + "<" + string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name)) + ">")];

    [Fact]
    public void A_project_with_one_member_class_registers_its_resource_by_the_resources_name()
    {
        var result = GeneratorTestHost.Create(Documents, "Documents.cs")
            .WithSource(Startup("services.AddDocumentMembership<ShopContext>(rules);"), "Startup.cs")
            .WithMembership()
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        var source = result.Source(File);
        source.Should().Contain(
            "AddDocumentMembership<TContext>(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::DDDToolkit.Supporting.Membership.Access.MembershipRules rules)");
        source.Should().Contain("where TContext : global::Microsoft.EntityFrameworkCore.DbContext");
        source.Should().Contain(
            ".AddMembership<global::Shop.DocumentShare, global::Shop.DocumentShareId, global::Shop.UserId, global::DDDToolkit.Supporting.Membership.NamedRole, TContext, global::Shop.Document, global::Shop.DocumentId>(services, rules);",
            "the member class names its resource, so the resource and its id are closed with it");

        RegistrationsOf(result).Should().BeEquivalentTo(
            ["AddDocumentMembership<TContext>", "AddDocumentMembership<TContext, TPorts>", "AddDocumentMemberAccess<TRequests>"],
            "one member class gets the registrations of one resource, named after it and not after the package's method");
    }

    [Fact]
    public void The_registration_that_takes_the_applications_class_is_closed_over_the_member_class_too()
    {
        // What the rules have the application answer, it answers in a class of its own, handed over where the
        // resource is registered: the class and the context are all it writes.
        var result = GeneratorTestHost.Create(Documents, "Documents.cs")
            .WithSource(
                """
                using DDDToolkit.Abstractions.Access;
                using DDDToolkit.Supporting.Membership.Access;

                namespace Shop;

                public sealed class DocumentsOfTheFirm : ICallerMember<DocumentId, UserId>
                {
                    public UserId? Find(Caller caller) => null;
                }
                """,
                "DocumentsOfTheFirm.cs")
            .WithSource(Startup("services.AddDocumentMembership<ShopContext, DocumentsOfTheFirm>(rules);"), "Startup.cs")
            .WithMembership()
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        var source = result.Source(File);
        source.Should().Contain(
            "AddDocumentMembership<TContext, TPorts>(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::DDDToolkit.Supporting.Membership.Access.MembershipRules rules)");
        source.Should().Contain("where TPorts : class");
        source.Should().Contain(
            ".AddMembership<global::Shop.DocumentShare, global::Shop.DocumentShareId, global::Shop.UserId, global::DDDToolkit.Supporting.Membership.NamedRole, TContext, global::Shop.Document, global::Shop.DocumentId, TPorts>(services, rules);",
            "the class that answers is the application's to name, like the context");
    }

    [Fact]
    public void A_second_resource_gets_a_registration_of_its_own_and_the_first_keeps_its_name()
    {
        var result = GeneratorTestHost.Create(Documents, "Documents.cs")
            .WithSource(Folders, "Folders.cs")
            .WithSource(Startup("services.AddDocumentMembership<ShopContext>(rules); services.AddFolderMembership<ShopContext>(rules);"), "Startup.cs")
            .WithMembership()
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            File,
            ".AddMembership<global::Shop.FolderMember, global::Shop.FolderMemberId, global::Shop.UserId, global::DDDToolkit.Supporting.Membership.NamedRole, TContext, global::Shop.Folder, global::Shop.FolderId>(services, rules);",
            "a resource declared over a raw value has its id generated beside it, and that is the id the registration is closed over");

        RegistrationsOf(result).Should().BeEquivalentTo(
            "AddDocumentMembership<TContext>", "AddDocumentMembership<TContext, TPorts>", "AddDocumentMemberAccess<TRequests>",
            "AddFolderMembership<TContext>", "AddFolderMembership<TContext, TPorts>", "AddFolderMemberAccess<TRequests>");
    }

    [Fact]
    public void The_access_check_of_a_resource_is_added_by_the_resources_name_for_the_request_interface_a_module_names()
    {
        // The check is the package's, closed over the resource and its id, which the member class names: the
        // module says only whose requests it is for.
        var result = GeneratorTestHost.Create(Documents, "Documents.cs")
            .WithSource(Folders, "Folders.cs")
            .WithSource(
                """
                using DDDToolkit.Access;

                namespace Shop;

                public interface IShopRequest : IRequireAccess;
                """,
                "IShopRequest.cs")
            .WithSource(
                Startup("services.AddDocumentMembership<ShopContext>(rules); services.AddDocumentMemberAccess<IShopRequest>(); services.AddFolderMemberAccess<IShopRequest>();"),
                "Startup.cs")
            .WithMembership()
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        var source = result.Source(AccessFile);
        source.Should().Contain("AddDocumentMemberAccess<TRequests>(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        source.Should().Contain("where TRequests : class, global::DDDToolkit.Access.IRequireAccess");
        source.Should().Contain(
            ".AddMemberAccess<global::Shop.Document, global::Shop.DocumentId, TRequests>(services);",
            "the resource and its id are closed, and whose requests the check is for is left to the module");
        source.Should().Contain(
            ".AddMemberAccess<global::Shop.Folder, global::Shop.FolderId, TRequests>(services);",
            "a resource whose id is generated beside it is closed over that id");
    }

    [Fact]
    public void The_generated_registration_adds_the_resources_check_to_the_checks_of_that_interface_and_of_no_other()
    {
        var result = GeneratorTestHost.Create(Documents, "Documents.cs")
            .WithSource(
                """
                using DDDToolkit.Access;

                namespace Shop;

                public interface IShopRequest : IRequireAccess;

                public interface IOtherRequest : IRequireAccess;
                """,
                "Requests.cs")
            .WithSource(Startup("services.AddDocumentMemberAccess<IShopRequest>();"), "Startup.cs")
            .WithMembership()
            .RunCore();

        var emitted = result.Emit();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        emitted.CallStatic("Shop.Startup", "Register", services, null);

        var check = typeof(DDDToolkit.Supporting.Membership.Access.MemberAccessCheck<,>).MakeGenericType(emitted.Type("Shop.Document"), emitted.Type("Shop.DocumentId"));
        var checks = typeof(DDDToolkit.Access.AccessChecks<>);
        services.Should().Contain(descriptor => descriptor.ServiceType == check, "the package's check, closed over the resource and its id");
        services.Should().Contain(descriptor => descriptor.ServiceType == checks.MakeGenericType(emitted.Type("Shop.IShopRequest")), "the set of checks of the interface the module named");
        services.Should().NotContain(descriptor => descriptor.ServiceType == checks.MakeGenericType(emitted.Type("Shop.IOtherRequest")));
    }

    [Fact]
    public void Two_member_classes_of_one_resource_are_refused_since_their_registrations_would_be_called_the_same()
    {
        var result = GeneratorTestHost.Create(Documents, "Documents.cs")
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
                "DocumentWatcher.cs")
            .WithMembership()
            .RunCore();

        // Said once, however many registrations the two classes would share the name of, the resource's own and its
        // access check's, and on the class to fix: the document keeps its shares, so the watcher is the one to go.
        var reported = result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00045").Select(diagnostic => diagnostic.GetMessage()).ToList();
        reported.Should().ContainSingle().Which.Should().Contain("'DocumentShare' and 'DocumentWatcher'").And.Contain("the registration of each would be called 'AddDocumentMembership'");
        result.ShouldHaveDiagnostic("DDD00045", at: "DocumentWatcher");
        result.HintNames.Should().NotContain("AddMembership.Registration", "a resource has one member list, and one registration");
        result.HintNames.Should().NotContain("AddMemberAccess.Registration");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Fact]
    public void A_member_class_that_names_no_aggregate_of_the_application_is_refused_where_it_is_declared()
    {
        var result = GeneratorTestHost.Create(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Supporting.Membership;

                namespace Shop;

                [EntityId<Guid>]
                public readonly partial record struct UserId;

                [EntityId<Guid>]
                public readonly partial record struct NoteShareId;

                public sealed class Note;

                [Member<NoteShareId, UserId, NamedRole, Note>]
                public sealed partial class NoteShare;
                """,
                "Notes.cs")
            .WithMembership()
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00050", at: "NoteShare").GetMessage()
            .Should().Contain("takes 'Note' as 'TResourceId', which requires an entity or an aggregate root, whose id it takes");
        result.HintNames.Should().NotContain("AddMembership.Registration");
    }

    [Fact]
    public void A_member_class_that_names_a_child_entity_is_refused_where_it_is_declared()
    {
        // A resource is an aggregate: its members are loaded and saved with it, under its version. A chapter is a
        // child of a book, so it has an id to take and is still no resource.
        var result = GeneratorTestHost.Create(
                """
                using System;
                using System.Collections.Generic;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Supporting.Membership;

                namespace Shop;

                [EntityId<Guid>]
                public readonly partial record struct UserId;

                [EntityId<Guid>]
                public readonly partial record struct BookId;

                [EntityId<Guid>]
                public readonly partial record struct ChapterId;

                [EntityId<Guid>]
                public readonly partial record struct ChapterShareId;

                [AggregateRoot<BookId>]
                public sealed partial class Book
                {
                    public Book(BookId id) : base(id) { }

                    public partial IReadOnlyList<Chapter> Chapters { get; }
                }

                [Entity<ChapterId>]
                public sealed partial class Chapter
                {
                    public Chapter(ChapterId id) : base(id) { }
                }

                [Member<ChapterShareId, UserId, NamedRole, Chapter>]
                public sealed partial class ChapterShare;
                """,
                "Chapters.cs")
            .WithMembership()
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00050", at: "ChapterShare").GetMessage()
            .Should().Contain("takes 'Chapter' as 'TResource', which requires an aggregate root");
        result.Count("DDD00050").Should().Be(1, "once, though both the resource's registration and its access check's refuse it");
        result.HintNames.Should().NotContain("AddMembership.Registration");
        result.HintNames.Should().NotContain("AddMemberAccess.Registration");
        result.CompilationErrors.Should().BeEmpty("the compiler is not left to say it inside the registration");
    }
}
