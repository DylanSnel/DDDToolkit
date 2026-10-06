using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DDDToolkit.EntityFramework.Supabase.Analyzers;

// The row access contributions the export is handed: the ones the packages this project references write by
// themselves, each made in a class written into DDDToolkit.RowAccessContributionsOfPackages.g.cs from what the
// application marks, and the application's own, which this project lists with [assembly: UseRowAccessContribution]:
// a module's among them, which its assembly offers with [assembly: RowAccessContribution] so a forgotten line is heard.
public sealed partial class SupabaseMigrationsGenerator
{
    /// <summary>The namespace the classes are written in, the same as the list of sources.</summary>
    private const string GeneratedNamespace = "DDDToolkit.EntityFramework.Supabase.Generated";

    /// <summary>The file the classes that make the packages' contributions are written into.</summary>
    internal const string PackagesFileName = "DDDToolkit.RowAccessContributionsOfPackages.g.cs";

    /// <summary>
    /// What the export is handed: the packages' contributions, as the classes written for them, and the
    /// application's own, by their fully qualified names, each list in ordinal order.
    /// </summary>
    private sealed record ContributionsToExport(EquatableArray<Made> Made, EquatableArray<string> Listed)
    {
        public static readonly ContributionsToExport None = new(EquatableArray<Made>.Empty, EquatableArray<string>.Empty);
    }

    /// <summary>
    /// One contribution a package writes, as the class written for it: its name, the package's class as it is
    /// made (closed where it is generic), as code and as the docs show it, the assembly that declares it, what an
    /// application writes to leave it out, the arguments it is made with, and the contexts it is left out of.
    /// </summary>
    private sealed record Made(
        string ClassName,
        string Contribution,
        string Shown,
        string Declarer,
        string LeaveOut,
        EquatableArray<Argument> Arguments,
        EquatableArray<string> LeftOutOf);

    /// <summary>
    /// One argument a contribution is made with: the parameter it is for, the code that reads it, and where it
    /// comes from, as the class's comment says it. A parameter left to its default has no code.
    /// </summary>
    private sealed record Argument(string Parameter, string? Expression, string Said);

    /// <summary>A static property or field the application marks with one of the attributes a package's contribution asks.</summary>
    private sealed class Marked(ISymbol member, ITypeSymbol type, INamedTypeSymbol mark, string expression, string? problem)
    {
        public ISymbol Member { get; } = member;

        public ITypeSymbol Type { get; } = type;

        /// <summary>The attribute it is marked with, closed as the application wrote it.</summary>
        public INamedTypeSymbol Mark { get; } = mark;

        /// <summary>What reads it from the exporting project: <c>global::Shop.ShopCatalogue.Application</c>.</summary>
        public string Expression { get; } = expression;

        /// <summary>Why the exporting project cannot read it, as a message says it, or null when it can.</summary>
        public string? Problem { get; } = problem;

        /// <summary>How a message names it: <c>Shop.ShopCatalogue.Application</c>.</summary>
        public string Shown => Member.ContainingType.ToDisplayString() + "." + Member.Name;
    }

    /// <summary>How a package's class is made: the constructor, and for each of its parameters the mark it comes from.</summary>
    private sealed record Plan(IAssemblySymbol Declarer, INamedTypeSymbol Definition, IMethodSymbol Constructor, IReadOnlyList<ParameterSource> Sources);

    /// <summary>Where one parameter comes from: the attribute it is marked with, whether every member so marked, and whether the mark closes the class.</summary>
    private sealed record ParameterSource(IParameterSymbol Parameter, INamedTypeSymbol Marker, bool Every)
    {
        /// <summary>A generic mark, which closes the class with its type arguments, once for every member so marked.</summary>
        public bool Closes => Marker.IsGenericType;
    }

    /// <summary>
    /// The contributions this project hands the export, and what is wrong with them. A package's is made unless
    /// the project leaves it out; one this project lists that a package writes already is reported (DDD00072) and
    /// left out of the list. A module's is an offer: it is written where this project lists it, and one it does
    /// not list is reported (DDD00069).
    /// </summary>
    private static ContributionsToExport ContributionsOf(Compilation compilation, IReadOnlyList<IAssemblySymbol> searched, List<DiagnosticInfo> diagnostics, CancellationToken cancellationToken)
    {
        var fromApplication = compilation.GetTypeByMetadataName(KnownTypes.FromApplicationAttribute);
        var contributionInterface = compilation.GetTypeByMetadataName(KnownTypes.RowAccessContributionInterface);
        var leftOut = LeftOut(compilation, diagnostics);
        var listed = Listed(compilation.Assembly).ToList();

        // What the packages declare, each once however many assemblies declare it, and what the modules offer: an
        // assembly that declares a module is the application's, and its SQL is the host's to list.
        var declared = new List<(IAssemblySymbol Declarer, INamedTypeSymbol Definition)>();
        var offered = new List<(IAssemblySymbol Declarer, INamedTypeSymbol Definition)>();
        foreach (var assembly in searched)
        {
            var of = ModuleBoundary.ModuleOf(assembly) is null ? declared : offered;
            foreach (var type in ContributionsIn(assembly, KnownTypes.RowAccessContributionAttribute))
            {
                if (!declared.Concat(offered).Any(each => SymbolEqualityComparer.Default.Equals(each.Definition, type.OriginalDefinition)))
                {
                    of.Add((assembly, type.OriginalDefinition));
                }
            }
        }

        var plans = new List<Plan>();
        foreach (var (declarer, definition) in declared)
        {
            // Left out as a whole: nothing of it is asked, so nothing of it is reported either. Every line that names
            // it, a closing or a context as well, names a contribution a package writes.
            if (leftOut.Any(each => each.Context is null && Names(each.Contribution, definition)))
            {
                foreach (var each in leftOut.Where(each => SymbolEqualityComparer.Default.Equals(each.Contribution.OriginalDefinition, definition)))
                {
                    each.Used = true;
                }

                continue;
            }

            if (PlanOf(declarer, definition, compilation, fromApplication, contributionInterface, out var problem) is { } plan)
            {
                plans.Add(plan);
            }
            else
            {
                diagnostics.Add(CannotBeMade(definition, declarer, problem! + " That is the package's to fix."));
                foreach (var each in leftOut.Where(each => SymbolEqualityComparer.Default.Equals(each.Contribution.OriginalDefinition, definition)))
                {
                    each.Used = true;
                }
            }
        }

        var marked = MarkedMembers(compilation, plans, cancellationToken);
        var made = new List<(INamedTypeSymbol Type, Made Made, IAssemblySymbol Declarer)>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = diagnostics.Count;
            foreach (var (type, arguments) in Instances(plan, marked, compilation, diagnostics, reported, cancellationToken))
            {
                var naming = leftOut.Where(each => Matches(each.Contribution, type)).ToList();
                foreach (var each in naming)
                {
                    each.Used = true;
                }

                if (naming.Any(static each => each.Context is null))
                {
                    continue;
                }

                var contexts = naming
                    .Select(static each => each.Context!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static each => each, StringComparer.Ordinal)
                    .ToList();

                made.Add((type, new Made(
                    ClassNameOf(type),
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    type.ToDisplayString(),
                    plan.Declarer.Name,
                    Unbound(plan.Definition),
                    arguments.ToEquatableArray(),
                    contexts.ToEquatableArray()), plan.Declarer));
            }

            // A closing the application leaves out that was not made because what it is made from is wrong has
            // been reported for that; it names a contribution of the package's all the same.
            if (diagnostics.Count > before)
            {
                foreach (var each in leftOut.Where(each => SymbolEqualityComparer.Default.Equals(each.Contribution.OriginalDefinition, plan.Definition)))
                {
                    each.Used = true;
                }
            }
        }

        foreach (var each in leftOut.Where(static each => !each.Used))
        {
            var definition = each.Contribution.OriginalDefinition;
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.LeaveOutRowAccessContributionLeavesNothingOut,
                each.Location,
                each.Written,
                offered.Any(offer => SymbolEqualityComparer.Default.Equals(offer.Definition, definition))
                    ? "'" + Unbound(definition) + "' is a module's own contribution, which only a line of [assembly: UseRowAccessContribution] writes; take that line out instead"
                    : declared.Any(package => SymbolEqualityComparer.Default.Equals(package.Definition, definition))
                        ? "nothing the application marks makes '" + each.Contribution.ToDisplayString() + "'"
                        : "no package this project references declares '" + each.Contribution.ToDisplayString() + "' a contribution it writes"));
        }

        // A contribution another assembly declares, or a module offers, that derives from one a package writes is that SQL again.
        var again = made.Where(candidate => made.Any(other => DerivesFrom(candidate.Type, other.Type))).ToList();
        foreach (var candidate in again)
        {
            var writer = made.First(other => DerivesFrom(candidate.Type, other.Type));
            diagnostics.Add(WrittenAgainDeclared(candidate.Type, candidate.Declarer, writer.Type, writer.Declarer));
        }

        made.RemoveAll(candidate => again.Any(each => ReferenceEquals(each.Made, candidate.Made)));

        var repeated = new List<INamedTypeSymbol>();
        foreach (var (declarer, definition) in offered)
        {
            if (made.FirstOrDefault(each => DerivesFrom(definition, each.Type)) is { Made: not null } writer)
            {
                diagnostics.Add(WrittenAgainDeclared(definition, declarer, writer.Type, writer.Declarer));
                repeated.Add(definition);
            }
            else if (!listed.Any(each => Covers(each.Type, definition)))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.RowAccessContributionOfAModuleNotListed,
                    null,
                    declarer.Name,
                    definition.ToDisplayString(),
                    HowToList(definition)));
            }
        }

        // The application's own, listed in this project; one that is a package's again is reported where it is listed.
        var own = new List<string>();
        foreach (var (type, location) in listed)
        {
            var writer = made.FirstOrDefault(each => SymbolEqualityComparer.Default.Equals(type, each.Type) || DerivesFrom(type, each.Type));
            if (writer.Made is not null)
            {
                var same = SymbolEqualityComparer.Default.Equals(type, writer.Type);
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.RowAccessContributionWrittenTwice,
                    location,
                    "'" + type.ToDisplayString() + "'" + (same ? " is listed with [assembly: UseRowAccessContribution], and is the contribution" : ", listed with [assembly: UseRowAccessContribution], derives from '" + writer.Type.ToDisplayString() + "'"),
                    writer.Declarer.Name,
                    (same ? "Take the line out" : "Take the line out, and the class with it: what it hands the package, mark instead")
                    + ". To write the package's SQL with a class of your own, leave the package's out with [assembly: LeaveOutRowAccessContribution(typeof(" + writer.Made.LeaveOut + "))]"));
                continue;
            }

            if (repeated.Any(each => Covers(type, each)))
            {
                continue;
            }

            own.Add(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        var classes = made.Select(each => each.Made).ToList();
        Uniquely(classes);
        classes.Sort(static (left, right) => string.CompareOrdinal(left.ClassName, right.ClassName));

        return new ContributionsToExport(
            classes.ToEquatableArray(),
            own.Distinct(StringComparer.Ordinal).OrderBy(static each => each, StringComparer.Ordinal).ToEquatableArray());
    }

    /// <summary>DDD00072 for a contribution another assembly declares that derives from one a package writes.</summary>
    private static DiagnosticInfo WrittenAgainDeclared(INamedTypeSymbol type, IAssemblySymbol declarer, INamedTypeSymbol writer, IAssemblySymbol writerDeclarer)
        => DiagnosticInfo.Create(
            DiagnosticDescriptors.RowAccessContributionWrittenTwice,
            null,
            "'" + type.ToDisplayString() + "', which '" + declarer.Name + "' declares, derives from '" + writer.ToDisplayString() + "'",
            writerDeclarer.Name,
            "Take the declaration out of '" + declarer.Name + "', and the class with it: what it hands the package, the application marks instead");

    // ------------------------------------------------------------------ what this project says

    /// <summary>One line of <c>[assembly: LeaveOutRowAccessContribution(typeof(X), Context = typeof(C))]</c>, and whether it left anything out.</summary>
    private sealed class LeaveOut(INamedTypeSymbol contribution, INamedTypeSymbol? context, LocationInfo? location, string written)
    {
        public INamedTypeSymbol Contribution { get; } = contribution;

        /// <summary>The context it is left out of, or null for every one.</summary>
        public INamedTypeSymbol? Context { get; } = context;

        public LocationInfo? Location { get; } = location;

        /// <summary>The line as a message shows it.</summary>
        public string Written { get; } = written;

        /// <summary>Whether it names a contribution a package writes, so that it leaves something out.</summary>
        public bool Used { get; set; }
    }

    /// <summary>The contributions this project lists with <c>[assembly: UseRowAccessContribution(typeof(X))]</c>, with where each is listed.</summary>
    private static IEnumerable<(INamedTypeSymbol Type, LocationInfo? Location)> Listed(IAssemblySymbol assembly)
        => assembly.GetAttributes()
            .Where(static attribute => attribute.AttributeClass?.ToDisplayString() == KnownTypes.UseRowAccessContributionAttribute)
            .Where(static attribute => attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol)
            .Select(static attribute => ((INamedTypeSymbol)attribute.ConstructorArguments[0].Value!, LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation())));

    /// <summary>
    /// What this project leaves out with <c>[assembly: LeaveOutRowAccessContribution(typeof(X), Context = typeof(C))]</c>.
    /// A line whose context is no context, a type that does not derive from <c>DbContext</c> or a generic one left
    /// open, leaves nothing out and is reported (DDD00073): the code written for it would test for what no context is,
    /// or would not compile.
    /// </summary>
    private static List<LeaveOut> LeftOut(Compilation compilation, List<DiagnosticInfo> diagnostics)
    {
        var dbContext = compilation.GetTypeByMetadataName(KnownTypes.DbContext);
        var found = new List<LeaveOut>();
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != KnownTypes.LeaveOutRowAccessContributionAttribute
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol contribution)
            {
                continue;
            }

            var location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());
            var given = attribute.NamedArguments.FirstOrDefault(static argument => argument.Key == "Context").Value;
            var context = given.Value as INamedTypeSymbol;
            var written = "[assembly: LeaveOutRowAccessContribution(typeof(" + contribution.ToDisplayString() + ")"
                          + (given.Value is ITypeSymbol shown ? ", Context = typeof(" + shown.ToDisplayString() + ")" : string.Empty) + ")]";

            var problem = given.Value is null ? null
                : context is null || dbContext is null || !IsA(context, dbContext) ? "'" + ((ITypeSymbol)given.Value).ToDisplayString() + "' is no context: Context names a class derived from DbContext, whose access file it is left out of"
                : context.IsUnboundGenericType ? "'" + context.ToDisplayString() + "' is left open, and Context names one context, closed with the type arguments it is declared with"
                : null;
            if (problem is not null)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.LeaveOutRowAccessContributionLeavesNothingOut, location, written, problem));
                continue;
            }

            found.Add(new LeaveOut(contribution, context, location, written));
        }

        return found;
    }

    /// <summary>Whether <paramref name="type"/> is <paramref name="parent"/> or derives from it.</summary>
    private static bool IsA(INamedTypeSymbol type, INamedTypeSymbol parent)
    {
        for (INamedTypeSymbol? each = type.OriginalDefinition; each is not null; each = each.BaseType?.OriginalDefinition)
        {
            if (SymbolEqualityComparer.Default.Equals(each, parent))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether what an application leaves out, <paramref name="leftOut"/>, is the whole of <paramref name="definition"/>:
    /// the class itself, or a generic one open, which is every closing of it.
    /// </summary>
    private static bool Names(INamedTypeSymbol leftOut, INamedTypeSymbol definition)
        => SymbolEqualityComparer.Default.Equals(leftOut, definition)
           || (leftOut.IsUnboundGenericType && SymbolEqualityComparer.Default.Equals(leftOut.OriginalDefinition, definition));

    /// <summary>
    /// Whether what an application leaves out, <paramref name="leftOut"/>, is <paramref name="made"/>: the class
    /// itself, a generic one open, which is every closing of it, or the one closing named.
    /// </summary>
    private static bool Matches(INamedTypeSymbol leftOut, INamedTypeSymbol made)
        => leftOut.IsUnboundGenericType
            ? SymbolEqualityComparer.Default.Equals(leftOut.OriginalDefinition, made.OriginalDefinition)
            : SymbolEqualityComparer.Default.Equals(leftOut, made);

    /// <summary>Whether <paramref name="type"/> derives from <paramref name="parent"/>, as it is closed.</summary>
    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol parent)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, parent))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the host's <paramref name="listed"/> contribution is a module's <paramref name="offered"/> one: the
    /// same class, a class of the host's derived from it, or, for a generic one, the host's own closing of it.
    /// </summary>
    private static bool Covers(INamedTypeSymbol listed, INamedTypeSymbol offered)
    {
        for (INamedTypeSymbol? type = listed; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, offered)
                || (offered.IsGenericType && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, offered.OriginalDefinition)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What DDD00069 tells the host to write for a module's offer it does not list, as code that compiles once the
    /// host's own types are put in. The export makes what is listed with <c>new X()</c>, so what is listed closes
    /// every type parameter and takes nothing. An offer that does both is listed as it is. A generic one that
    /// takes nothing is closed in the attribute. One whose constructor takes an argument is listed through a class
    /// of the host's that derives from it and hands that over; a type parameter of such a class is closed in that line.
    /// </summary>
    private static string HowToList(INamedTypeSymbol offered)
    {
        var takesNothing = offered.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public);
        var closedWithYours = offered.TypeParameters.Length == 0
            ? offered.Name
            : offered.Name + "<" + string.Join(", ", offered.TypeParameters.Select(static parameter => "Your" + Unprefixed(parameter.Name))) + ">";
        var forEach = offered.TypeParameters.Length == 0
            ? string.Empty
            : ", with a type of yours for " + string.Join(" and ", offered.TypeParameters.Select(static parameter => parameter.Name));
        var qualified = (offered.ContainingType is { } outer ? outer.ToDisplayString() + "."
                            : offered.ContainingNamespace is { IsGlobalNamespace: false } space ? space.ToDisplayString() + "."
                            : string.Empty) + closedWithYours;

        if (takesNothing && offered.TypeParameters.Length == 0)
        {
            return "Add [assembly: UseRowAccessContribution(typeof(" + offered.ToDisplayString() + "))], or list a class of yours that derives from it";
        }

        if (takesNothing)
        {
            return "Add [assembly: UseRowAccessContribution(typeof(" + qualified + "))]" + forEach + ", or list a class of yours that derives from it";
        }

        return "Its constructor takes what only your application can give it, so declare a class of yours that derives from it and hands that over, "
               + "public sealed class YourRowAccess() : " + qualified + "(...)" + forEach + ", and add [assembly: UseRowAccessContribution(typeof(YourRowAccess))]";
    }

    /// <summary>A type parameter's name without its leading <c>T</c>: <c>Member</c> of <c>TMember</c>, and a name that has none as it is.</summary>
    private static string Unprefixed(string name)
        => name.Length > 1 && name[0] == 'T' && char.IsUpper(name[1]) ? name.Substring(1) : name;

    // ------------------------------------------------------------------ how a package's class is made

    /// <summary>
    /// How the build makes <paramref name="definition"/>, or null with the reason it cannot, as a message says it:
    /// a public class that is not abstract and implements the contribution's interface, with a public constructor
    /// whose every parameter says with <c>[FromApplication]</c> where it comes from, the one with the most such
    /// parameters, or else a parameterless one; a generic class closed by exactly one generic mark.
    /// </summary>
    private static Plan? PlanOf(IAssemblySymbol declarer, INamedTypeSymbol definition, Compilation compilation, INamedTypeSymbol? fromApplication, INamedTypeSymbol? contributionInterface, out string? problem)
    {
        problem = null;
        if (definition.TypeKind != TypeKind.Class || definition.IsAbstract || definition.IsStatic)
        {
            problem = "it is not a class that can be made, but " + (definition.IsStatic ? "a static class." : definition.IsAbstract ? "an abstract one." : "a " + definition.TypeKind.ToString().ToLowerInvariant() + ".");
            return null;
        }

        if (!compilation.IsSymbolAccessibleWithin(definition, compilation.Assembly))
        {
            problem = "it is not public, so '" + compilation.AssemblyName + "', which runs the export, cannot make it.";
            return null;
        }

        if (contributionInterface is not null && !definition.AllInterfaces.Any(each => SymbolEqualityComparer.Default.Equals(each, contributionInterface)))
        {
            problem = "it does not implement IRowAccessContribution.";
            return null;
        }

        var candidates = new List<(IMethodSymbol Constructor, List<ParameterSource> Sources)>();
        foreach (var constructor in definition.InstanceConstructors.Where(static each => each.DeclaredAccessibility == Accessibility.Public))
        {
            var sources = new List<ParameterSource>();
            foreach (var parameter in constructor.Parameters)
            {
                var attribute = parameter.GetAttributes().FirstOrDefault(each => fromApplication is not null && SymbolEqualityComparer.Default.Equals(each.AttributeClass, fromApplication));
                if (attribute is not null && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol marker)
                {
                    var every = attribute.NamedArguments.FirstOrDefault(static argument => argument.Key == "Every").Value.Value is true;
                    sources.Add(new ParameterSource(parameter, marker.IsUnboundGenericType ? marker.OriginalDefinition : marker, every));
                }
            }

            if (sources.Count == constructor.Parameters.Length)
            {
                candidates.Add((constructor, sources));
            }
        }

        if (candidates.Count == 0)
        {
            var taking = definition.InstanceConstructors.Where(static each => each.DeclaredAccessibility == Accessibility.Public).SelectMany(static each => each.Parameters).FirstOrDefault();
            problem = taking is null
                ? "it has no public constructor."
                : "its constructor takes '" + taking.Name + "', and does not say with [FromApplication] where that comes from, so the build has nothing to make it with.";
            return null;
        }

        var most = candidates.Max(static each => each.Sources.Count);
        var chosen = candidates.Where(each => each.Sources.Count == most).ToList();
        if (chosen.Count > 1)
        {
            problem = "it has " + chosen.Count + " public constructors of " + most + " parameters that each say with [FromApplication] where they come from, and the build cannot choose between them.";
            return null;
        }

        var (picked, from) = chosen[0];
        var closing = from.Where(static source => source.Closes).ToList();
        if (closing.Any(static source => source.Every))
        {
            problem = "its parameter '" + closing.First(static source => source.Every).Parameter.Name + "' takes every member marked with a generic attribute, which closes the class once for each, not once for all.";
            return null;
        }

        if (definition.IsGenericType && (closing.Count != 1 || closing[0].Marker.Arity != definition.Arity))
        {
            problem = closing.Count > 1
                ? "it is generic, and " + closing.Count + " of its parameters come from a generic mark, which would each close it."
                : "it is generic, and none of its parameters comes from a mark with as many type arguments, which the build would close it with.";
            return null;
        }

        if (!definition.IsGenericType && closing.Count > 0)
        {
            problem = "its parameter '" + closing[0].Parameter.Name + "' comes from a generic mark, which closes a generic class, and the class is not generic.";
            return null;
        }

        foreach (var source in from.Where(static source => source.Every))
        {
            if (ElementOf(source.Parameter.Type) is not { } element
                || !Converts(compilation.CreateArrayTypeSymbol(element), source.Parameter.Type, compilation))
            {
                problem = "its parameter '" + source.Parameter.Name + "' takes every member so marked, and is no sequence an array of them can be handed to.";
                return null;
            }
        }

        return new Plan(declarer, definition, picked, from);
    }

    /// <summary>The element type of a sequence: an array's, or the <c>T</c> of the <c>IEnumerable&lt;T&gt;</c> it is or implements.</summary>
    private static ITypeSymbol? ElementOf(ITypeSymbol sequence)
        => sequence is IArrayTypeSymbol array ? array.ElementType
            : sequence is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T } named ? named.TypeArguments[0]
            : sequence.AllInterfaces.FirstOrDefault(static each => each.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)?.TypeArguments[0];

    /// <summary>Whether a value of <paramref name="source"/> is handed to a parameter of <paramref name="destination"/> as it is: by identity or an implicit conversion of the language's own.</summary>
    private static bool Converts(ITypeSymbol source, ITypeSymbol destination, Compilation compilation)
    {
        var conversion = compilation.ClassifyCommonConversion(source, destination);
        return (conversion.IsIdentity || conversion.IsImplicit) && !conversion.IsUserDefined;
    }

    // ------------------------------------------------------------------ what the application marks

    /// <summary>
    /// Every static property and field marked with an attribute one of the plans asks, in this project and in the
    /// projects it references that reference the attribute's assembly, which is the only way one of them can carry
    /// it. An assembly is searched for each attribute apart: the one that declares an attribute marks nothing of
    /// the application's with that attribute, and is passed over for it alone. A package that declares a mark of
    /// its own may still mark its keys for another package's, a <c>[TenancyPermissions]</c> list, as any module does.
    /// </summary>
    private static List<Marked> MarkedMembers(Compilation compilation, IReadOnlyList<Plan> plans, CancellationToken cancellationToken)
    {
        var markers = plans.SelectMany(static plan => plan.Sources).Select(static source => source.Marker).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToList();
        var found = new List<Marked>();
        if (markers.Count == 0)
        {
            return found;
        }

        // For each assembly, the attributes it can carry: those of the assemblies it references, and not its own.
        var assemblies = new List<(IAssemblySymbol Assembly, List<INamedTypeSymbol> Markers)>();
        foreach (var assembly in new[] { compilation.Assembly }.Concat(compilation.SourceModule.ReferencedAssemblySymbols))
        {
            var referenced = assembly.Modules.SelectMany(static module => module.ReferencedAssemblies).Select(static identity => identity.Name).ToList();
            var carried = markers
                .Where(marker => !SymbolEqualityComparer.Default.Equals(marker.ContainingAssembly, assembly)
                                 && (ReferenceEquals(assembly, compilation.Assembly) || referenced.Contains(marker.ContainingAssembly.Name, StringComparer.Ordinal)))
                .ToList();
            if (carried.Count > 0)
            {
                assemblies.Add((assembly, carried));
            }
        }

        foreach (var (assembly, carried) in assemblies)
        {
            foreach (var type in TypesIn(assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var member in type.GetMembers())
                {
                    if (member is not (IPropertySymbol or IFieldSymbol) || member.IsImplicitlyDeclared)
                    {
                        continue;
                    }

                    foreach (var attribute in member.GetAttributes())
                    {
                        if (attribute.AttributeClass is { } mark && carried.Any(marker => SymbolEqualityComparer.Default.Equals(marker, mark.OriginalDefinition)))
                        {
                            var memberType = member is IPropertySymbol property ? property.Type : ((IFieldSymbol)member).Type;
                            found.Add(new Marked(member, memberType, mark, ExpressionOf(member), ProblemOf(member, compilation)));
                        }
                    }
                }
            }
        }

        // In the order of what reads them, so the code written from them does not change with the order of the references.
        found.Sort(static (left, right) => string.CompareOrdinal(left.Expression, right.Expression));
        return found;
    }

    /// <summary>
    /// Why the code written into the exporting project cannot read <paramref name="member"/> as <c>Type.Member</c>,
    /// as a message says it, or null when it can.
    /// </summary>
    private static string? ProblemOf(ISymbol member, Compilation compilation)
    {
        if (!member.IsStatic)
        {
            return "is not static, and nothing makes an instance of its class to read it from";
        }

        if (member is IPropertySymbol { GetMethod: null })
        {
            return "has no getter";
        }

        if (member.ContainingType.TypeKind == TypeKind.Interface && (member.IsVirtual || member.IsAbstract))
        {
            return "is a static virtual or abstract member of an interface, which is read only through a type parameter";
        }

        for (var container = member.ContainingType; container is not null; container = container.ContainingType)
        {
            if (container.IsGenericType)
            {
                return "is declared in the generic type '" + container.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) + "'";
            }

            if (container.IsFileLocal || !container.CanBeReferencedByName)
            {
                return "is declared in '" + container.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) + "', which code cannot name";
            }
        }

        if (!compilation.IsSymbolAccessibleWithin(member, compilation.Assembly)
            || (member is IPropertySymbol { GetMethod: { } getter } && !compilation.IsSymbolAccessibleWithin(getter, compilation.Assembly)))
        {
            return "cannot be read from '" + compilation.AssemblyName + "', which runs the export: make it public, in public types";
        }

        return null;
    }

    /// <summary>What reads the member from anywhere: <c>global::Shop.ShopCatalogue.Application</c>.</summary>
    private static string ExpressionOf(ISymbol member)
    {
        var name = SyntaxFacts.GetKeywordKind(member.Name) != SyntaxKind.None ? "@" + member.Name : member.Name;
        return member.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + name;
    }

    // ------------------------------------------------------------------ the instances

    /// <summary>
    /// The contributions <paramref name="plan"/> makes: one, or one for every closing the application marks, each
    /// with its arguments. What is missing or cannot be used is reported, and the instance it concerns not made.
    /// </summary>
    private static IEnumerable<(INamedTypeSymbol Type, List<Argument> Arguments)> Instances(
        Plan plan,
        IReadOnlyList<Marked> marked,
        Compilation compilation,
        List<DiagnosticInfo> diagnostics,
        HashSet<string> reported,
        CancellationToken cancellationToken)
    {
        var closing = plan.Sources.FirstOrDefault(static source => source.Closes);
        if (closing is null)
        {
            if (ArgumentsFor(plan, plan.Definition, plan.Constructor, null, marked, compilation, diagnostics, reported) is { } arguments)
            {
                yield return (plan.Definition, arguments);
            }

            yield break;
        }

        var all = marked.Where(each => SymbolEqualityComparer.Default.Equals(each.Mark.OriginalDefinition, closing.Marker)).ToList();
        foreach (var unreadable in all.Where(static each => each.Problem is not null))
        {
            Report(diagnostics, reported, plan, unreadable, "'" + unreadable.Shown + "', marked " + MarkShown(unreadable.Mark) + ", " + unreadable.Problem + ".");
        }

        var readable = all.Where(static each => each.Problem is null).ToList();
        if (all.Count == 0)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.RowAccessContributionNotMarked,
                null,
                plan.Declarer.Name,
                plan.Definition.ToDisplayString(),
                "a static property or field of type '" + Short(closing.Parameter.Type) + "' marked " + MarkShown(closing.Marker) + ", one for each " + string.Join(" and ", plan.Definition.TypeParameters.Select(static parameter => "'" + parameter.Name + "'")) + " it is written for",
                Unbound(plan.Definition)));
            yield break;
        }

        foreach (var group in readable.GroupBy(static each => string.Join(",", each.Mark.TypeArguments.Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var members = group.ToList();
            var typeArguments = members[0].Mark.TypeArguments;
            if (members.Count > 1)
            {
                Report(diagnostics, reported, plan, null, Both(members) + " are marked " + MarkShown(members[0].Mark) + ", and it is made once for each, from one; mark one of them.");
                continue;
            }

            if (Unmet(plan.Definition, typeArguments, compilation, cancellationToken) is { } unmet)
            {
                Report(diagnostics, reported, plan, members[0], "'" + members[0].Shown + "' is marked " + MarkShown(members[0].Mark) + ", " + unmet + ".");
                continue;
            }

            var closed = plan.Definition.Construct([.. typeArguments]);
            var constructor = closed.InstanceConstructors.First(each => SymbolEqualityComparer.Default.Equals(each.OriginalDefinition, plan.Constructor));
            if (ArgumentsFor(plan, closed, constructor, members[0], marked, compilation, diagnostics, reported) is { } arguments)
            {
                yield return (closed, arguments);
            }
        }
    }

    /// <summary>
    /// What <paramref name="type"/> is made with, or null when something it cannot do without is missing or
    /// cannot be used, which is reported.
    /// </summary>
    private static List<Argument>? ArgumentsFor(
        Plan plan,
        INamedTypeSymbol type,
        IMethodSymbol constructor,
        Marked? closedBy,
        IReadOnlyList<Marked> marked,
        Compilation compilation,
        List<DiagnosticInfo> diagnostics,
        HashSet<string> reported)
    {
        var arguments = new List<Argument>();
        var complete = true;
        for (var index = 0; index < constructor.Parameters.Length; index++)
        {
            var parameter = constructor.Parameters[index];
            var source = plan.Sources[index];
            var name = SyntaxFacts.GetKeywordKind(parameter.Name) != SyntaxKind.None ? "@" + parameter.Name : parameter.Name;

            if (source.Closes)
            {
                if (!Converts(closedBy!.Type, parameter.Type, compilation))
                {
                    Report(diagnostics, reported, plan, closedBy, WrongType(closedBy, parameter));
                    complete = false;
                    continue;
                }

                arguments.Add(new Argument(name, closedBy.Expression, "<c>" + Xml(closedBy.Shown) + "</c>, marked <c>" + Xml(MarkShown(closedBy.Mark)) + "</c>."));
                continue;
            }

            var all = marked.Where(each => SymbolEqualityComparer.Default.Equals(each.Mark, source.Marker)).ToList();
            var usable = new List<Marked>();
            foreach (var each in all)
            {
                var problem = each.Problem is not null ? "'" + each.Shown + "', marked " + MarkShown(each.Mark) + ", " + each.Problem + "."
                    : !Converts(each.Type, source.Every ? ElementOf(parameter.Type)! : parameter.Type, compilation) ? WrongType(each, parameter, source.Every)
                    : null;
                if (problem is null)
                {
                    usable.Add(each);
                    continue;
                }

                Report(diagnostics, reported, plan, each, problem);
                complete = false;
            }

            if (usable.Count < all.Count)
            {
                continue;
            }

            if (source.Every)
            {
                var element = ElementOf(parameter.Type)!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                arguments.Add(new Argument(
                    name,
                    usable.Count == 0
                        ? "global::System.Array.Empty<" + element + ">()"
                        : "new " + element + "[]\n{\n" + string.Concat(usable.Select(static each => "    " + each.Expression + ",\n")) + "}",
                    usable.Count == 0
                        ? "every member marked <c>" + Xml(MarkShown(source.Marker)) + "</c>, and none is."
                        : "every member marked <c>" + Xml(MarkShown(source.Marker)) + "</c>: " + string.Join(", ", usable.Select(static each => "<c>" + Xml(each.Shown) + "</c>")) + "."));
                continue;
            }

            if (usable.Count > 1)
            {
                Report(diagnostics, reported, plan, null, Both(usable) + " are marked " + MarkShown(source.Marker) + ", and it takes one for '" + parameter.Name + "'; mark one of them.");
                complete = false;
                continue;
            }

            if (usable.Count == 1)
            {
                arguments.Add(new Argument(name, usable[0].Expression, "<c>" + Xml(usable[0].Shown) + "</c>, marked <c>" + Xml(MarkShown(source.Marker)) + "</c>."));
                continue;
            }

            if (parameter.IsOptional)
            {
                arguments.Add(new Argument(name, null, "nothing is marked <c>" + Xml(MarkShown(source.Marker)) + "</c>, so its default."));
                continue;
            }

            if (reported.Add(type.ToDisplayString() + "|" + parameter.Name))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.RowAccessContributionNotMarked,
                    null,
                    plan.Declarer.Name,
                    type.ToDisplayString(),
                    "a static property or field of type '" + Short(parameter.Type) + "' marked " + MarkShown(source.Marker),
                    Unbound(plan.Definition)));
            }

            complete = false;
        }

        return complete ? arguments : null;
    }

    /// <summary>What does not meet a type parameter's constraint, phrased to follow the member, or null when the type arguments meet them all.</summary>
    private static string? Unmet(INamedTypeSymbol definition, IReadOnlyList<ITypeSymbol> typeArguments, Compilation compilation, CancellationToken cancellationToken)
    {
        var arguments = typeArguments.Cast<ITypeSymbol?>().ToArray();
        for (var index = 0; index < definition.TypeParameters.Length; index++)
        {
            if (DefinitionFactory.UnmetByAnArgument(definition.TypeParameters[index], typeArguments[index], definition.TypeParameters, arguments, compilation, cancellationToken) is { } requirement)
            {
                return "and '" + typeArguments[index].ToDisplayString() + "' does not meet the constraint of its '" + definition.TypeParameters[index].Name + "', which requires " + requirement;
            }
        }

        return null;
    }

    private static string WrongType(Marked member, IParameterSymbol parameter, bool every = false)
        => "'" + member.Shown + "', marked " + MarkShown(member.Mark) + ", is " + Article(Short(member.Type)) + ", and it takes "
           + (every ? "a sequence of " + Short(ElementOf(parameter.Type)!) + " from each member so marked" : Article(Short(parameter.Type))) + " for '" + parameter.Name + "'.";

    private static void Report(List<DiagnosticInfo> diagnostics, HashSet<string> reported, Plan plan, Marked? member, string problem)
    {
        if (reported.Add(plan.Definition.ToDisplayString() + "|" + problem))
        {
            diagnostics.Add(CannotBeMade(plan.Definition, plan.Declarer, problem, member is null ? null : LocationInfo.From(member.Member)));
        }
    }

    private static DiagnosticInfo CannotBeMade(INamedTypeSymbol definition, IAssemblySymbol declarer, string problem, LocationInfo? location = null)
        => DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessContributionCannotBeMade, location, definition.ToDisplayString(), declarer.Name, problem);

    // ------------------------------------------------------------------ names

    /// <summary>
    /// The name of the class written for a contribution: the package's class's own, and for a closing of a
    /// generic one, <c>Of</c> and the names of the types it is closed over:
    /// <c>MembershipRowAccessContributionOfDocumentShare</c>.
    /// </summary>
    private static string ClassNameOf(INamedTypeSymbol type)
        => type.Name + (type.IsGenericType ? "Of" + string.Concat(type.TypeArguments.Select(static argument => argument.Name)) : string.Empty);

    /// <summary>Two packages' classes of one name get a number, in the order of the classes they make.</summary>
    private static void Uniquely(List<Made> classes)
    {
        foreach (var named in classes.GroupBy(static each => each.ClassName, StringComparer.Ordinal).Where(static group => group.Count() > 1).ToList())
        {
            var number = 1;
            foreach (var each in named.OrderBy(static each => each.Contribution, StringComparer.Ordinal).Skip(1))
            {
                var index = classes.IndexOf(each);
                classes[index] = each with { ClassName = each.ClassName + (++number).ToString(System.Globalization.CultureInfo.InvariantCulture) };
            }
        }
    }

    /// <summary>The class as <c>typeof</c> names it to leave it out: open where it is generic, <c>MembershipRowAccessContribution&lt;&gt;</c>.</summary>
    private static string Unbound(INamedTypeSymbol definition)
    {
        var qualified = definition.ContainingType is { } outer ? outer.ToDisplayString() + "." + definition.Name
            : definition.ContainingNamespace is { IsGlobalNamespace: false } space ? space.ToDisplayString() + "." + definition.Name
            : definition.Name;
        return definition.IsGenericType ? qualified + "<" + new string(',', definition.Arity - 1) + ">" : qualified;
    }

    /// <summary>The attribute as an application writes it: <c>[TenancyCatalogue]</c>, <c>[MembershipRules&lt;TMember&gt;]</c>.</summary>
    private static string MarkShown(INamedTypeSymbol mark)
    {
        const string Suffix = "Attribute";
        var name = mark.Name.EndsWith(Suffix, StringComparison.Ordinal) && mark.Name.Length > Suffix.Length ? mark.Name.Substring(0, mark.Name.Length - Suffix.Length) : mark.Name;
        return "[" + name + (mark.IsGenericType ? "<" + string.Join(", ", mark.TypeArguments.Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ">" : string.Empty) + "]";
    }

    private static string Short(ITypeSymbol type) => type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static string Article(string name) => ("AEIOU".IndexOf(char.ToUpperInvariant(name[0])) >= 0 ? "an '" : "a '") + name + "'";

    /// <summary>'A' and 'B', or 'A', 'B' and 'C'.</summary>
    private static string Both(IReadOnlyList<Marked> members)
    {
        var names = members.Select(static each => "'" + each.Shown + "'").ToList();
        return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
    }

    private static string Xml(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ------------------------------------------------------------------ what is written

    /// <summary>
    /// The classes that make the packages' contributions, one for each, in their own file: each holds the package's
    /// class made with what the application marks, and answers for it, except for the contexts it is left out of.
    /// </summary>
    private static string EmitPackages(EquatableArray<Made> made)
    {
        const string Postgres = "global::" + KnownTypes.PostgresNamespace;
        var writer = new StringBuilder();
        writer.Append("// <auto-generated/>\n");
        writer.Append("// The row access contributions the packages this project references write into its Supabase migrations,\n");
        writer.Append("// each declared by its package with [assembly: RowAccessContribution] and made here from what the application\n");
        writer.Append("// marks. Written by the generator of DDDToolkit.EntityFramework.Supabase, because this project turns the export on.\n");
        writer.Append("#nullable enable\n\n");
        writer.Append("namespace ").Append(GeneratedNamespace).Append('\n');
        writer.Append("{\n");

        for (var index = 0; index < made.Count; index++)
        {
            var each = made[index];
            if (index > 0)
            {
                writer.Append('\n');
            }

            writer.Append("    /// <summary>\n");
            writer.Append("    /// The row access contribution <c>").Append(Xml(each.Shown)).Append("</c>, which <c>").Append(Xml(each.Declarer)).Append("</c> writes into\n");
            writer.Append("    /// the migrations of every application that references it")
                .Append(each.Arguments.Count == 0 ? ", made as it is.\n" : ", made with what this application marks.\n");
            writer.Append("    /// </summary>\n");
            writer.Append("    /// <remarks>\n");
            if (each.Arguments.Count > 0)
            {
                writer.Append("    /// <list type=\"bullet\">\n");
                foreach (var argument in each.Arguments)
                {
                    writer.Append("    /// <item><c>").Append(argument.Parameter.TrimStart('@')).Append("</c>: ").Append(argument.Said).Append("</item>\n");
                }

                writer.Append("    /// </list>\n");
            }

            foreach (var context in each.LeftOutOf)
            {
                writer.Append("    /// Left out of the access file of <c>").Append(Xml(context.StartsWith("global::", StringComparison.Ordinal) ? context.Substring("global::".Length) : context)).Append("</c>.\n");
            }

            writer.Append("    /// To write none of it: <c>[assembly: LeaveOutRowAccessContribution(typeof(").Append(Xml(each.LeaveOut)).Append("))]</c>.\n");
            writer.Append("    /// The access files name the package's class and <c>").Append(Xml(each.Declarer)).Append("</c> above what it writes, without a version.\n");
            writer.Append("    /// </remarks>\n");
            writer.Append("    internal sealed class ").Append(each.ClassName).Append(" : ").Append(Postgres).Append(".IPackageRowAccessContribution\n");
            writer.Append("    {\n");

            var given = each.Arguments.Where(static argument => argument.Expression is not null).ToList();
            writer.Append("        private readonly ").Append(Postgres).Append(".IRowAccessContribution _contribution = new ").Append(each.Contribution).Append('(');
            if (given.Count == 0)
            {
                writer.Append(");\n");
            }
            else
            {
                writer.Append('\n');
                for (var position = 0; position < given.Count; position++)
                {
                    var lines = given[position].Expression!.Split('\n');
                    writer.Append("            ").Append(given[position].Parameter).Append(": ").Append(lines[0]);
                    foreach (var line in lines.Skip(1))
                    {
                        writer.Append('\n').Append("            ").Append(line);
                    }

                    writer.Append(position == given.Count - 1 ? ");\n" : ",\n");
                }
            }

            writer.Append('\n');
            writer.Append("        /// <inheritdoc />\n");
            writer.Append("        public ").Append(Postgres).Append(".IRowAccessContribution Contribution => _contribution;\n\n");
            writer.Append("        /// <inheritdoc />\n");
            writer.Append("        public string Owner => _contribution.Owner;\n\n");
            writer.Append("        /// <inheritdoc />\n");
            writer.Append("        public ").Append(Postgres).Append(".RowAccessContributionResult? Contribute(global::Microsoft.EntityFrameworkCore.DbContext context, ").Append(Postgres).Append(".RowAccessExport export)\n");
            writer.Append("            => ");
            if (each.LeftOutOf.Count > 0)
            {
                writer.Append(string.Join(" || ", each.LeftOutOf.Select(static context => "context is " + context))).Append(" ? null : ");
            }

            writer.Append("_contribution.Contribute(context, export);\n");
            writer.Append("    }\n");
        }

        writer.Append("}\n");
        return writer.ToString();
    }
}
