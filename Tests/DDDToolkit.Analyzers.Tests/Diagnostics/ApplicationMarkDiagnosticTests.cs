using DDDToolkit.Analyzers.Analyzers;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00070: a member a library marks for a package's row access contribution that the project that runs the
/// Supabase export cannot see. That project finds the marks in the libraries it references, and of a library it
/// sees the public surface and nothing else, so a mark kept internal would be passed over there without a word: the
/// library that declares it says so, where it is declared.
/// </summary>
public sealed class ApplicationMarkDiagnosticTests
{
    private const string Usings =
        """
        using System.Collections.Generic;
        using DDDToolkit.Supporting.Membership.Access;
        using DDDToolkit.Supporting.Tenancy;
        using DDDToolkit.Supporting.Tenancy.Catalogue;

        namespace Shop.Catalogue;

        public sealed class DocumentShare;


        """;

    private static GeneratorRunOutcome Run(string declarations, bool application = false)
    {
        var host = GeneratorTestHost.Create(Usings + declarations)
            .WithAssemblyName("Shop.Catalogue")
            .WithSupportingDomainsOnPostgres()
            .WithAnalyzers(new ApplicationMarkAnalyzer());
        return (application ? host.AsApplication() : host).Run();
    }

    [Fact]
    public void A_mark_a_library_does_not_make_public_is_DDD00070_where_it_is_declared()
    {
        var result = Run(
            """
            public static class ShopCatalogue
            {
                [TenancyCatalogue]
                internal static ApplicationCatalogue Application { get; } = new();
            }

            internal static class ShopOperators
            {
                [TenancyOperators]
                public static IReadOnlyList<string> TokenRoles { get; } = ["operator"];
            }

            public static class DocumentMembership
            {
                [MembershipRules<DocumentShare>]
                public static MembershipRules Rules { private get; set; } = null!;
            }
            """);

        result.ShouldNotCrash();
        result.ShouldHaveExactlyDiagnostics("DDD00070", "DDD00070", "DDD00070");
        var catalogue = result.ShouldHaveDiagnostic("DDD00070", at: "Application");
        catalogue.Severity.Should().Be(DiagnosticSeverity.Error);
        catalogue.GetMessage().Should().Be(
            "'ShopCatalogue.Application' is marked [TenancyCatalogue] and is not public, in public types, with a public getter: the project that runs the Supabase "
            + "export references this library and sees nothing less of it, so it would make the package's contribution as if nothing were marked. Make it public.");
        result.ShouldHaveDiagnostic("DDD00070", at: "TokenRoles").GetMessage().Should().StartWith("'ShopOperators.TokenRoles' is marked [TenancyOperators]", "a public member of an internal class is not seen either");
        result.ShouldHaveDiagnostic("DDD00070", at: "Rules").GetMessage().Should().StartWith("'DocumentMembership.Rules' is marked [MembershipRules<DocumentShare>]", "nor one whose getter is private");
    }

    [Fact]
    public void A_public_mark_and_an_applications_own_internal_one_are_not_reported()
    {
        const string marks =
            """
            public static class ShopCatalogue
            {
                [TenancyCatalogue]
                public static ApplicationCatalogue Application { get; } = new();

                [TenancyOperators]
                public static readonly string[] TokenRoles = ["operator"];
            }

            internal static class DocumentMembership
            {
                [MembershipRules<DocumentShare>]
                internal static MembershipRules Rules => null!;
            }
            """;

        Run(marks.Replace("internal static class DocumentMembership", "public static class DocumentMembership", StringComparison.Ordinal)
                .Replace("internal static MembershipRules", "public static MembershipRules", StringComparison.Ordinal))
            .ShouldHaveExactlyDiagnostics();

        // The program that runs the export, or the host, reads its own marks: nothing references it for them.
        Run(marks, application: true).ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_packages_own_mark_is_held_to_it_when_the_package_says_it_is_one()
    {
        var result = Run(
            """
            [DDDToolkit.Abstractions.Attributes.ApplicationMark]
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
            public sealed class PlansAttribute : System.Attribute;

            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
            public sealed class NoteAttribute : System.Attribute;

            public static class ShopPlans
            {
                [Plans]
                internal static readonly string[] All = ["free"];

                [Note]
                internal static readonly string[] Notes = ["kept to itself"];
            }
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00070");
        result.ShouldHaveDiagnostic("DDD00070", at: "All").GetMessage().Should().StartWith("'ShopPlans.All' is marked [Plans]");
    }
}
