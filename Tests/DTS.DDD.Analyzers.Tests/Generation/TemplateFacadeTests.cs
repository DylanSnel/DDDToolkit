using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DTS.DDD.Analyzers.Tests.Generation;

/// <summary>
/// A package asks, with <c>[assembly: TemplateFacade]</c>, that the project that declares the application's classes
/// gets a generic class of the package's closed over them, as a class of its own named as the package's class is
/// without its type parameters: Tenancy's seat manager, generic over nine of the application's classes and ids, is
/// <c>SeatManager</c> there and in every project above it, and no project writes the nine types.
/// <para>
/// Most of these use the real Tenancy package, seen through metadata as an application sees it, and a module split
/// by layer the way the sample is: each project of it is compiled with what the generators wrote, and names the
/// managers and their records itself, so a class that is missing, or closed over the wrong classes, fails to
/// compile there.
/// </para>
/// <para>
/// Where the class cannot be written the project that declares the classes says why, DDD00065, since the projects
/// above would only hear that the name does not exist; and that project may name the class itself, with
/// <c>[assembly: TemplateFacadeName]</c>. Two modules that each declare the classes get two classes of one name, which
/// meet in a project that sees both: DDD00075 says so there, with the line that names one of them.
/// </para>
/// <para>
/// Tenancy asks for more classes than that one: a manager per aggregate, the record a tenant is provisioned from, and
/// its access manager, <c>TenancyAccessManager</c>, closed over the ids, each held to the same rules. These tests are
/// about the seat manager's class; where something is said of every one, they read what is said of that one
/// (<see cref="About"/>), and count one for each (<see cref="TenancyClasses"/>).
/// </para>
/// </summary>
public partial class TemplateFacadeTests
{
    /// <summary>The module's ids, in its contracts: what every module that refers to an organization sees.</summary>
    private const string Ids =
        """
        using System;
        using DTS.DDD.Abstractions.Attributes;

        namespace Shop.Contracts;

        [EntityId<Guid>]
        public readonly partial record struct TenantId;

        [EntityId<Guid>]
        public readonly partial record struct SeatId;

        [EntityId<Guid>]
        public readonly partial record struct OrganizationUnitId;

        [EntityId<Guid>]
        public readonly partial record struct RoleId;

        [EntityId<Guid>]
        public readonly partial record struct InvitationId;
        """;

    /// <summary>The name the class has when nobody names it: Tenancy's class's, without its type parameters.</summary>
    private const string Default = "SeatManager";

    private static string ModuleAttribute(string module) => "[assembly: DTS.DDD.Abstractions.Attributes.Module(\"" + module + "\")]\n";

    /// <summary>
    /// The line an application names one of Tenancy's classes with, the seat manager's unless said otherwise, in place of
    /// the package's name.
    /// </summary>
    private static string NamedItself(string name, string facade = Default)
        => "[assembly: DTS.DDD.Abstractions.Attributes.TemplateFacadeName(\"" + facade + "\", \"" + name + "\")]\n";

    /// <summary>The access manager's name when nobody names it: Tenancy's class's, without its type parameters.</summary>
    private const string AccessManager = "TenancyAccessManager";

    /// <summary>Every class Tenancy asks for, by the package's names, in the order of their names.</summary>
    private static readonly string[] TenancyClasses =
        ["InvitationManager", "OrganizationManager", "RoleManager", "SeatManager", "TenancyAccessManager", "TenantManager", "TenantToProvision"];

    /// <summary>The aliases Tenancy asks for, of its stores, by the package's names without the I, in the order of their names.</summary>
    private static readonly string[] TenancyAliases = ["InvitationStore", "TenancyStore"];

    /// <summary>Every class and alias Tenancy asks for, in the order of their names.</summary>
    private static readonly string[] TenancyNames = [.. TenancyClasses.Concat(TenancyAliases).Order(StringComparer.Ordinal)];

    /// <summary>The lines that name every class and alias Tenancy asks for, each with <paramref name="prefix"/> before the package's name.</summary>
    private static string NamedEvery(string prefix)
        => string.Concat(TenancyNames.Select(facade => NamedItself(prefix + facade, facade)));

    /// <summary>
    /// The one diagnostic of <paramref name="id"/> said of the class called <paramref name="name"/>, the seat manager's unless
    /// said otherwise: Tenancy asks for several classes, and what keeps one out often keeps them all.
    /// </summary>
    private static Diagnostic About(IEnumerable<Diagnostic> diagnostics, string id, string name = Default)
        => diagnostics.Where(diagnostic => diagnostic.Id == id && diagnostic.GetMessage().Contains("'" + name + "'", StringComparison.Ordinal)).Should().ContainSingle().Subject;

    /// <summary>The snippet without these lines: a class left out of <see cref="Classes"/>, its attribute and its declaration.</summary>
    private static string Without(string source, params string[] lines)
        => lines.Aggregate(source, static (left, line) => left.Replace(line, string.Empty));

    /// <summary>The seat in <see cref="Classes"/>.</summary>
    private static readonly string[] SeatLines = ["[SeatAggregate<SeatId>]", "public sealed partial class Seat;"];

    /// <summary>The invitation in <see cref="Classes"/>, whose parent takes the seat's id.</summary>
    private static readonly string[] InvitationLines = ["[InvitationAggregate<InvitationId>]", "public sealed partial class Invitation;"];

    /// <summary>The type a property of a class the snippet declares has: what the name stands for, as the compiler sees it.</summary>
    private static INamedTypeSymbol TypeOf(GeneratorRunOutcome result, string type, string member)
        => (INamedTypeSymbol)((IPropertySymbol)result.OutputCompilation.GetTypeByMetadataName(type)!.GetMembers(member).Single()).Type;

    // ------------------------------------------------------------------ what a package can ask

    /// <summary>A made-up package with several classes it asks for: one every application can have, and ones it cannot.</summary>
    private const string Package =
        """
        using System;
        using DTS.DDD.Abstractions.Attributes;
        using DTS.DDD.Abstractions.Interfaces;

        [assembly: TemplateFacade(typeof(Acme.Billing.BillingUseCases<,>))]
        [assembly: TemplateFacade(typeof(Acme.Billing.Ledger<,>))]
        [assembly: TemplateFacade(typeof(Acme.Billing.Open<,>))]
        [assembly: TemplateFacade(typeof(Acme.Billing.BillingUseCases<,>))]
        [assembly: TemplateFacade(typeof(Acme.Billing.Statics<,>))]
        [assembly: TemplateFacade(typeof(Acme.Billing.Closed<,>))]

        namespace Acme.Billing;

        [AggregateRootBase]
        public abstract partial class AccountAggregate<TAccountId>
            where TAccountId : IEntityId, IEquatable<TAccountId>
        {
            protected AccountAggregate(TAccountId id) : base(id) { }
        }

        [AggregateRootTemplate(typeof(AccountAggregate<>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class AccountAttribute<TAccountId> : Attribute;

        public interface IAudited;

        public abstract class BillingUseCases<
            [TemplateType(typeof(AccountAttribute<>), Take = TemplateArgumentKind.Type)] TAccount,
            [TemplateType(typeof(AccountAttribute<>))] TAccountId>
            where TAccount : AccountAggregate<TAccountId>
            where TAccountId : struct, IEntityId, IEquatable<TAccountId>
        {
            public sealed record Statement(TAccountId Account, decimal Balance);
        }

        // Asks more of the account than its template gives it.
        public abstract class Ledger<
            [TemplateType(typeof(AccountAttribute<>), Take = TemplateArgumentKind.Type)] TAccount,
            [TemplateType(typeof(AccountAttribute<>))] TAccountId>
            where TAccount : AccountAggregate<TAccountId>, IAudited
            where TAccountId : struct, IEntityId, IEquatable<TAccountId>
        {
            public sealed record Line(TAccountId Account);
        }

        // Leaves a type parameter for the application to choose, which a class closed over the classes cannot.
        public abstract class Open<[TemplateType(typeof(AccountAttribute<>))] TAccountId, TCurrency>
            where TAccountId : struct, IEntityId, IEquatable<TAccountId>
        {
            public sealed record Amount(TAccountId Account, TCurrency Currency);
        }

        // Static, and sealed with a constructor of its own: nothing derives from either.
        public static class Statics<[TemplateType(typeof(AccountAttribute<>), Take = TemplateArgumentKind.Type)] TAccount, [TemplateType(typeof(AccountAttribute<>))] TAccountId>;

        public sealed class Closed<[TemplateType(typeof(AccountAttribute<>), Take = TemplateArgumentKind.Type)] TAccount, [TemplateType(typeof(AccountAttribute<>))] TAccountId>
        {
            private Closed() { }
        }
        """;

    private const string Accounts =
        """
        using System;
        using DTS.DDD.Abstractions.Attributes;
        using Acme.Billing;

        namespace Shop.Billing;

        [EntityId<Guid>]
        public readonly partial record struct AccountId;

        [Account<AccountId>]
        public sealed partial class Account
        {
            public Account(AccountId id) : base(id) { }
        }

        public static class Uses
        {
            public static BillingUseCases.Statement Statement(AccountId id) => new(id, 0m);
        }
        """;

    [Fact]
    public void A_package_of_its_own_gets_its_class_and_one_its_classes_do_not_fit_is_told_and_its_own_mistakes_pass_without_a_word()
    {
        // The package is compiled with the application here, its generic class in a namespace of the project: a class
        // of the name with type parameters is no class the generated one would hide.
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Accounts, "Accounts.cs")
            .WithSource(ModuleAttribute("Shop"), "Module.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain("BillingUseCases.TemplateFacade", "public abstract class BillingUseCases : global::Acme.Billing.BillingUseCases<global::Shop.Billing.Account, global::Shop.Billing.AccountId>");
        result.HintNames.Should().NotContain("Ledger.TemplateFacade", "the account does not meet what the ledger asks of it, and a compile error in generated code would fix nothing");
        result.ShouldHaveDiagnostic("DDD00065", at: "Account").GetMessage().Should().Be(
            "'Ledger' is not written, so no project can name the types nested in Ledger through it: it takes 'Account' as 'TAccount', which requires 'IAudited'; 'Account' does not meet it");
        result.ReportedDiagnostics.Should().ContainSingle("what is the package's own mistake is the package's tests' to show, and a type asked twice is asked once");
        result.HintNames.Should().NotContain("Open.TemplateFacade", "the class leaves no type parameter open");
        result.HintNames.Should().NotContain("Statics.TemplateFacade").And.NotContain("Closed.TemplateFacade", "only a class that can be derived from names its nested types through another");
    }

    [Fact]
    public void Two_classes_one_project_gets_that_come_to_one_name_are_both_left_out_and_told()
    {
        // A name the application gives the ledger that the package's other class has already.
        var result = GeneratorTestHost.Create(Package.Replace(", IAudited", string.Empty), "Package.cs")
            .WithSource(Accounts.Replace("BillingUseCases.Statement Statement(AccountId id) => new(id, 0m);", "int Nothing => 0;"), "Accounts.cs")
            .WithSource(ModuleAttribute("Shop"), "Module.cs")
            .WithSource(NamedItself("BillingUseCases", "Ledger"), "Named.cs")
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade", "neither could be told from the other");
        result.ShouldHaveDiagnostic("DDD00075", at: "Account").GetMessage().Should().Be(
            "'BillingUseCases' is the name of more than one class in this project: it would get the one for Acme.Billing.BillingUseCases<,> and the one for "
            + "Acme.Billing.Ledger<,>, and gets neither. Give one of them a name of its own, with [assembly: TemplateFacadeName(\"BillingUseCases\", \"...\")] "
            + "in this project.");
    }

    /// <summary>A handler of the application's that takes both stores, by the names the aliases give them.</summary>
    private static string StoreHandler(string @namespace = "Shop.Application", string store = "TenancyStore", bool invitations = true)
        => $$"""
             namespace {{@namespace}};

             public sealed class RenameSeat({{store}} store)
             {
                 public {{store}} Store => store;
             {{(invitations ? "\n    public InvitationStore? Invitations { get; set; }" : string.Empty)}}
             }
             """;

    // ------------------------------------------------------------------ another library's generator

    /// <summary>A class that names a type in an attribute, as an API project names a record in HotChocolate's <c>[ObjectType&lt;T&gt;]</c>.</summary>
    private static string Described(string type, string @namespace)
        => $$"""
             namespace {{@namespace}}
             {
                 [global::Shop.Describes<{{type}}>]
                 public static partial class SeatOverviewType;
             }

             namespace Shop
             {
                 [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
                 public sealed class DescribesAttribute<T> : global::System.Attribute;
             }
             """;

    /// <summary>
    /// A generator of another library, as HotChocolate's is: it reads the type a class names in an attribute and
    /// writes it out in full, beside whether it could resolve it.
    /// </summary>
    private sealed class DescribesGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var described = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Shop.DescribesAttribute`1",
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) =>
                {
                    var type = attributeContext.Attributes[0].AttributeClass!.TypeArguments[0];
                    return (
                        Class: attributeContext.TargetSymbol.Name,
                        Namespace: attributeContext.TargetSymbol.ContainingNamespace.ToDisplayString(),
                        Type: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Resolved: type.TypeKind != TypeKind.Error);
                });

            context.RegisterSourceOutput(described, static (production, each) => production.AddSource(
                each.Class + ".Describes.g.cs",
                "// " + (each.Resolved ? "resolved" : "unresolved") + "\nnamespace " + each.Namespace + ";\n\npartial class " + each.Class
                + "\n{\n    public static readonly global::System.Type Described = typeof(" + each.Type + ");\n}\n"));
        }
    }
}
