using System.Reflection;
using DDDToolkit.Exceptions;
using DDDToolkit.Localization;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// The first example of docs/membership/resources-and-members.md, read from the page and compiled as it stands, with the generators
/// an application runs: a document, its member class, its rules, its context and registrations, and a request
/// with its handler. The page says the code is all there is to write; this holds it to that, and holds what the
/// page shows the generators write to what they do write.
/// </summary>
public class MembershipDocsExampleTests
{
    private const string Page = "membership/resources-and-members.md";

    private const string Heading = "A document and the people it is shared with";

    /// <summary>What the page leaves out: the usings, and a namespace of the application's own.</summary>
    private const string Header =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using DDDToolkit.Abstractions.Access;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using DDDToolkit.EntityFramework;
        using DDDToolkit.EntityFramework.Conventions;
        using DDDToolkit.Exceptions;
        using DDDToolkit.Supporting.Membership;
        using DDDToolkit.Supporting.Membership.Access;
        using DDDToolkit.Supporting.Membership.EntityFramework;
        using DDDToolkit.Supporting.Membership.Managers;
        using Filing.Converters;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;

        namespace Filing;

        """;

    private static GeneratorRunOutcome Run()
    {
        var examples = DocsExamples.Of(Page, Heading);
        examples.Should().HaveCount(5, "the domain, the permissions, the rules, the wiring, and a request with its handler");

        var host = GeneratorTestHost.Create(Header + examples[0], "Example0.cs");
        for (var index = 1; index < examples.Count; index++)
        {
            host = host.WithSource(Header + examples[index], "Example" + index + ".cs");
        }

        return host
            .WithAssemblyName("Filing")
            .WithModuleFromTheBuild("Filing")
            .WithMembership()
            .RunCoreAnd([.. GeneratorTestHost.EntityFrameworkGenerators(), .. GeneratorTestHost.MemberListGenerators()]);
    }

    [Fact]
    public void The_first_example_compiles_as_the_page_writes_it_and_nothing_is_reported()
    {
        var result = Run();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("the example is complete: nothing a generator could not write, nothing missing");
    }

    [Fact]
    public void What_the_page_says_the_generators_write_is_what_they_write()
    {
        var result = Run();

        foreach (var line in Lines(DocsExamples.Titled(Page, "Document.Members.g.cs")))
        {
            result.Source("Document.Members.").ReplaceLineEndings("\n").Should().Contain(line, "the page shows the member list as it is written");
        }

        result.ShouldContain("AddMembership.Registration.", "AddDocumentMembership<TContext>(");
        result.ShouldContain("AddMemberAccess.Registration.", "AddDocumentMemberAccess<TRequests>(");
    }

    [Fact]
    public void The_example_shares_a_document_in_a_declared_role_and_refuses_under_the_documents_codes()
    {
        var emitted = Run().Emit();
        var now = DateTimeOffset.UtcNow;
        var owner = emitted.CallStatic("Filing.UserId", "CreateUnique");
        var reader = emitted.CallStatic("Filing.UserId", "CreateUnique");

        // The rules read the document's name, codes and permissions off its type: what the generators wrote for it.
        var rules = (MembershipRules)emitted.StaticProperty("Filing.DocumentMembership", "Rules")!;
        rules.Name.Should().Be("documents", "the class's name, made plural");
        rules.Codes.Should().BeSameAs(emitted.StaticProperty("Filing.Document", "Codes"), "the rules refuse under the codes written on the document");
        rules.OwnerPermissions.Should().Equal("documents.view", "documents.edit", "documents.share");
        rules.SystemScopes.Should().Equal(["filing"], "the scope of the document's module");

        // The owner holds no role: it opens the document with nothing else to say. A share is added the way the member
        // manager adds one, through the members the generator writes onto the document, behind the document's hook.
        var document = emitted.New("Filing.Document", emitted.CallStatic("Filing.DocumentId", "CreateSequential"), owner, now);
        object? Share(string role) => Managed(document).GetMethod("AddMember")!.Invoke(document, [reader, new NamedRole(role), MemberPeriod.Open(now), now, owner]);
        Share("contributor");

        ((System.Collections.IEnumerable)emitted.Property(document, "Shares")!).Cast<object>().Should().HaveCount(2);
        rules.PermissionsOf(new NamedRole("contributor")).Should().Equal("documents.view", "documents.edit");

        var twice = FluentActions.Invoking(() => Share("onlooker"))
            .Should().Throw<TargetInvocationException>().WithInnerException<RefusalException>().Which;
        twice.Code.Should().Be("documents.already-member");
    }

    [Fact]
    public void The_examples_hook_keeps_an_archived_document_from_taking_a_share()
    {
        // The hook the example implements is the guard the member manager has run in front of every change.
        var emitted = Run().Emit();
        var now = DateTimeOffset.UtcNow;
        var owner = emitted.CallStatic("Filing.UserId", "CreateUnique");
        var document = emitted.New("Filing.Document", emitted.CallStatic("Filing.DocumentId", "CreateSequential"), owner, now);
        document.GetType().GetProperty("Archived")!.SetValue(document, true);

        FluentActions.Invoking(() => Managed(document).GetMethod("AddMember")!.Invoke(document, [emitted.CallStatic("Filing.UserId", "CreateUnique"), new NamedRole("onlooker"), MemberPeriod.Open(now), now, owner]))
            .Should().Throw<TargetInvocationException>().WithInnerException<RefusalException>().Which.Code.Should().Be("documents.archived");
        ((System.Collections.IEnumerable)emitted.Property(document, "Shares")!).Cast<object>().Should().ContainSingle("the owner alone");
    }

    /// <summary>The way to a resource's members the generator writes for its member manager, as explicit members of the interface.</summary>
    private static Type Managed(object resource)
        => resource.GetType().GetInterfaces().Single(face => face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IMembershipResource<,,>));

    [Fact]
    public void The_first_examples_permissions_say_which_the_database_lock_is_written_from()
    {
        // A reader who follows the first example on Postgres gets the lock on the member tables and the owner column,
        // and no warning at start-up that the rules leave them to whoever may change a document.
        var rules = (MembershipRules)Run().Emit().StaticProperty("Filing.DocumentMembership", "Rules")!;

        (rules.ManagesMembersPermission, rules.NamesOwnerPermission).Should().Be(("documents.share", "documents.share"), "the permission the example's member manager requires to share");
    }

    [Fact]
    public void The_examples_module_registers_the_questions_the_check_and_the_texts_and_says_who_is_calling()
    {
        var emitted = Run().Emit();
        var services = new ServiceCollection();

        emitted.CallStatic("Filing.FilingModule", "AddFiling", services, (Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder>)(_ => { }));

        var documentIdType = emitted.Type("Filing.DocumentId");
        services.Should().Contain(descriptor => descriptor.ServiceType == emitted.Type("Filing.FilingContext"), "the context the questions and the handlers ask for is registered, by the page's own line");
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(MembershipAccessManager<>).MakeGenericType(emitted.Type("Filing.Document")));
        services.Should().Contain(descriptor => descriptor.ServiceType == emitted.Type("DocumentShareManager"), "the member manager the handlers take, written into the global namespace");
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(MemberAccessCheck<,>).MakeGenericType(emitted.Type("Filing.Document"), documentIdType));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(DDDToolkit.Access.AccessChecks<>).MakeGenericType(emitted.Type("Filing.IFilingRequest")));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(FailureTexts), "the texts come with the registration, with no line of the page's");
        services.Should().Contain(descriptor => descriptor.ServiceType == emitted.Type("Filing.DocumentHandlers"));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(DDDToolkit.Access.CurrentUserOptions))
            .Which.ImplementationInstance.Should().BeOfType<DDDToolkit.Access.CurrentUserOptions>()
            .Which.RequireExplicitCurrentUser.Should().BeTrue("work that says nothing about who it runs as fails, as the page says");
    }

    [Fact]
    public void The_pages_write_host_code_outside_the_toolkits_namespaces()
    {
        // HotChocolate's generator writes names inside a namespace that starts with the toolkit's without
        // global::, so code a reader copies stays in a namespace of its own, on every page of Membership's.
        var pages = Directory.GetFiles(MembershipPages(), "*.md", SearchOption.TopDirectoryOnly);
        pages.Should().Contain(path => Path.GetFileName(path) == "resources-and-members.md");

        pages.SelectMany(path => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path), @"^namespace\s+(?<name>[\w.]+);", System.Text.RegularExpressions.RegexOptions.Multiline))
            .Select(match => match.Groups["name"].Value)
            .Where(name => name.StartsWith("DDDToolkit", StringComparison.Ordinal))
            .Should().NotContain(name => name != "DDDToolkit.Supporting.Membership.EntityFramework", "only what a generator writes into the package's own namespace says so");
    }

    private static IEnumerable<string> Lines(string code)
        => code.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && line is not "{" and not "}");

    /// <summary>docs/membership: the overview, the tutorial and the topic pages, without the generated API reference below it.</summary>
    private static string MembershipPages()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DDDToolkit.slnx")))
            {
                return Path.Combine(directory.FullName, "docs", "membership");
            }
        }

        throw new DirectoryNotFoundException($"No DDDToolkit.slnx above '{AppContext.BaseDirectory}'.");
    }
}
