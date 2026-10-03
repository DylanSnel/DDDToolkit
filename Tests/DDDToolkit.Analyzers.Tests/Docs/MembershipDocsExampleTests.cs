using System.Reflection;
using DDDToolkit.Exceptions;
using DDDToolkit.Localization;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// The first example of docs/membership.md, read from the page and compiled as it stands, with the generators
/// an application runs: a document, its member class, its rules, its context and registrations, and a request
/// with its handler. The page says the code is all there is to write; this holds it to that, and holds what the
/// page shows the generators write to what they do write.
/// </summary>
public class MembershipDocsExampleTests
{
    private const string Page = "membership.md";

    private const string Heading = "A document and the people it is shared with";

    /// <summary>What the page leaves out: the usings, and a namespace of the application's own.</summary>
    private const string Header =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using DDDToolkit.EntityFramework.Conventions;
        using DDDToolkit.Exceptions;
        using DDDToolkit.Supporting.Membership;
        using DDDToolkit.Supporting.Membership.Access;
        using DDDToolkit.Supporting.Membership.EntityFramework;
        using DDDToolkit.Supporting.Membership.UseCases;
        using Filing.Converters;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;

        namespace Filing;

        """;

    private static GeneratorRunOutcome Run()
    {
        var examples = DocsExamples.Of(Page, Heading);
        examples.Should().HaveCount(4, "the domain, the rules, the wiring, and a request with its handler");

        var host = GeneratorTestHost.Create(Header + examples[0], "Example0.cs");
        for (var index = 1; index < examples.Count; index++)
        {
            host = host.WithSource(Header + examples[index], "Example" + index + ".cs");
        }

        return host
            .WithAssemblyName("Filing")
            .WithModule("Filing")
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

        // The owner's role is the one the rules added, as the handler finds it.
        var rules = (MembershipRules)emitted.StaticProperty("Filing.DocumentMembership", "Rules")!;
        rules.OwnerRole.Should().Be(MembershipRules.DefaultOwnerRole);
        rules.Codes.Should().BeSameAs(emitted.StaticProperty("Filing.Document", "Codes"), "the rules refuse under the codes the document declares");

        var document = emitted.New("Filing.Document", emitted.CallStatic("Filing.DocumentId", "CreateSequential"), owner, new NamedRole(rules.OwnerRole), now);
        emitted.Call(document, "ShareWith", reader, new NamedRole("contributor"), MemberPeriod.Open(now), now, owner);

        ((System.Collections.IEnumerable)emitted.Property(document, "Shares")!).Cast<object>().Should().HaveCount(2);
        rules.KeysOf(new NamedRole("contributor")).Should().Equal("documents.view", "documents.edit");

        var twice = FluentActions.Invoking(() => emitted.Call(document, "ShareWith", reader, new NamedRole("onlooker"), MemberPeriod.Open(now), now, owner))
            .Should().Throw<TargetInvocationException>().WithInnerException<RefusalException>().Which;
        twice.Code.Should().Be("documents.already-member");
    }

    [Fact]
    public void The_first_examples_rules_name_the_keys_the_database_lock_is_written_from()
    {
        // A reader who follows the first example on Postgres gets the lock on the member tables and the owner column,
        // and no warning at start-up that the rules leave them to whoever may change a document.
        var rules = (MembershipRules)Run().Emit().StaticProperty("Filing.DocumentMembership", "Rules")!;

        (rules.ChangeMembersKey, rules.ChangeOwnerKey).Should().Be(("documents.share", "documents.share"), "the key the example's command that shares requires");
    }

    [Fact]
    public void The_examples_module_registers_the_questions_the_check_and_the_texts_and_says_who_is_calling()
    {
        var emitted = Run().Emit();
        var services = new ServiceCollection();

        emitted.CallStatic("Filing.FilingModule", "AddFiling", services, (Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder>)(_ => { }));

        var documentIdType = emitted.Type("Filing.DocumentId");
        services.Should().Contain(descriptor => descriptor.ServiceType == emitted.Type("Filing.FilingContext"), "the context the questions and the handlers ask for is registered, by the page's own line");
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IMemberQuestions<>).MakeGenericType(documentIdType));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(MemberAccessCheck<,>).MakeGenericType(emitted.Type("Filing.Document"), documentIdType));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(DDDToolkit.Access.AccessChecks<>).MakeGenericType(emitted.Type("Filing.IFilingRequest")));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(FailureTexts), "the texts come with the registration, with no line of the page's");
        services.Should().Contain(descriptor => descriptor.ServiceType == emitted.Type("Filing.DocumentHandlers"));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(DDDToolkit.Access.CallerOptions))
            .Which.ImplementationInstance.Should().BeOfType<DDDToolkit.Access.CallerOptions>()
            .Which.RequireExplicitCallers.Should().BeTrue("work that says nothing about who it runs as fails, as the page says");
    }

    [Fact]
    public void The_page_writes_host_code_outside_the_toolkits_namespaces()
    {
        // HotChocolate's generator writes names inside a namespace that starts with the toolkit's without
        // global::, so code a reader copies stays in a namespace of its own.
        System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(PagePath()), @"^namespace\s+(?<name>[\w.]+);", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(match => match.Groups["name"].Value)
            .Where(name => name.StartsWith("DDDToolkit", StringComparison.Ordinal))
            .Should().OnlyContain(name => name == "DDDToolkit.Supporting.Membership.EntityFramework", "only what a generator writes into the package's own namespace says so");
    }

    private static IEnumerable<string> Lines(string code)
        => code.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && line is not "{" and not "}");

    private static string PagePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DDDToolkit.slnx")))
            {
                return Path.Combine(directory.FullName, "docs", Page);
            }
        }

        throw new DirectoryNotFoundException($"No DDDToolkit.slnx above '{AppContext.BaseDirectory}'.");
    }
}
