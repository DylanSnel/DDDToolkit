using System.Collections.Generic;
using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Generates the base class for <c>[Entity&lt;TId&gt;]</c> and <c>[AggregateRoot&lt;TId&gt;]</c> classes and
/// implements their get-only partial collection properties:
/// <code>
/// public partial IReadOnlyList&lt;Order&gt; Orders { get; }
/// </code>
/// becomes a private <c>List&lt;Order&gt; _orders</c> backing field plus a read-only view, annotated with
/// EF Core's <c>[BackingField]</c> when the project references Entity Framework. The entity mutates the
/// collection through the field; the outside world only ever sees the read-only interface.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EntityGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.Entities(), static (productionContext, definition) => Execute(productionContext, definition));
        context.RegisterSourceOutput(context.AggregateRoots(), static (productionContext, definition) => Execute(productionContext, definition));
    }

    private static void Execute(SourceProductionContext context, EntityDefinition definition)
    {
        definition.Diagnostics.ReportAll(context);
        if (!definition.CanGenerate)
        {
            return;
        }

        var type = definition.Type;
        var baseType = definition.IsAggregateRoot ? "AggregateRoot" : "Entity";
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            using (writer.Block(type.PartialHeader + " : " + KnownTypes.BaseTypesNamespace + "." + baseType + "<" + definition.IdType + ">"))
            {
                writer.Line("/// <summary>Parameterless constructor for persistence frameworks and serializers.</summary>");
                using (writer.Block("protected " + type.Name + "()"))
                {
                }

                WriteInvariantSeam(writer, definition);

                foreach (var collection in definition.Collections)
                {
                    writer.Line();
                    writer.Line("private readonly " + collection.BackingType + " " + collection.FieldName + " = new();");

                    // AsReadOnly() and ReadOnlySet<T> have no cache of their own: each call is a new
                    // wrapper around the same collection, so an expression bodied property built one
                    // per read and threw it away. Holding it costs one reference and is safe because
                    // the field above is readonly, so the collection it wraps can never be swapped out
                    // from under it. Two threads racing here both win, building equivalent wrappers
                    // over the same collection. When the view IS the field there is nothing to hold.
                    var view = View(collection, definition.ReadOnlySetAvailable);
                    var cached = view != collection.FieldName;
                    var cache = "__" + collection.FieldName.TrimStart('_') + "View";

                    if (cached)
                    {
                        writer.Line();
                        writer.Line("private " + collection.InterfaceType + "? " + cache + ";");
                    }

                    writer.Line();
                    writer.Line("/// <summary>Read-only view over <see cref=\"" + collection.FieldName + "\"/>. Mutate the collection through the field.</summary>");
                    if (definition.EfBackingFieldAttributeAvailable)
                    {
                        writer.Line("[" + KnownTypes.EfBackingFieldAttributeUsage + "(nameof(" + collection.FieldName + "))]");
                    }

                    var body = cached ? cache + " ??= " + view : view;
                    writer.Line(collection.Modifiers + " " + collection.InterfaceType + " " + collection.Name + " => " + body + ";");
                }
            }
        }

        context.AddSource(type.HintName(), SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// Writes everything that runs this type's invariants: the <c>partial void CheckInvariants()</c>
    /// seam the author implements in their own part of the class, the array of nested
    /// <c>IInvariant&lt;T&gt;</c> rules, the routine that collects what this object alone has broken, the
    /// walk over its child entities, and the four public methods built on those two.
    /// <para>
    /// The four are two questions asked of two subjects. <c>GetInvariantViolations</c> and
    /// <c>EnsureInvariants</c> answer for the aggregate this object holds, because the root is the
    /// consistency boundary and answering for the boundary means answering for what is inside it;
    /// <c>GetOwnInvariantViolations</c> and <c>EnsureOwnInvariants</c> answer for this object alone, for
    /// a caller that is already walking the graph and would otherwise hear about a child twice. The save
    /// is that caller.
    /// </para>
    /// <para>
    /// Returning violations is the primary mechanism and throwing happens only at the boundary, so
    /// nothing in the toolkit uses exceptions as control flow. An <c>IInvariant&lt;T&gt;</c> never throws
    /// at all; the only throw comes from the author's own seam, and that exception is kept as the inner
    /// exception of the single one the <c>Ensure</c> pair raises, so no stack trace is lost. All four go
    /// through <c>CollectInvariantViolations</c> for the same reason: two paths through the seam could
    /// disagree about what counts as a violation, and the stage that asks would then pass something the
    /// stage that saves refuses.
    /// </para>
    /// <para>
    /// The seam is a <c>partial void</c> on purpose. A partial method with no implementing
    /// declaration is erased by the compiler along with every call to it, so a type that states no
    /// invariants and holds no children keeps an <c>EnsureInvariants</c> whose body is a single
    /// <c>ret</c>. A shape that returns something (a list of violations, a result object) cannot do
    /// that: C# requires an implementing declaration for any partial method that is not <c>void</c>,
    /// which would force every entity in the solution to write an empty method. Reporting several
    /// violations at once is handled instead by <c>InvariantViolation(IEnumerable&lt;string&gt;)</c>,
    /// which costs nothing when it is never called.
    /// </para>
    /// </summary>
    private static void WriteInvariantSeam(CodeWriter writer, EntityDefinition definition)
    {
        var what = definition.IsAggregateRoot ? "aggregate" : "entity";

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// Implement this in your own part of the class to state what must be true of this");
        writer.Line("/// " + what + " after every change, and throw (for example with <c>InvariantViolation(...)</c>)");
        writer.Line("/// when it is not. Write it without an accessibility modifier:");
        writer.Line("/// <code>partial void CheckInvariants() { ... }</code>");
        writer.Line("/// Leave it out and nothing runs: the compiler removes an unimplemented partial method");
        writer.Line("/// and every call to it. A rule worth a name of its own is better written as a nested");
        writer.Line("/// <c>IInvariant&lt;" + definition.Type.Name + "&gt;</c>, which this " + what + " runs as well.");
        writer.Line("/// </summary>");
        writer.Line("partial void CheckInvariants();");

        WriteInvariantRules(writer, definition);
        WriteCollectInvariantViolations(writer, definition);
        WriteCollectChildInvariantViolations(writer, definition, what);
        WriteThrowInvariantViolations(writer, definition, what);
        WriteGetOwnInvariantViolations(writer, definition, what);
        WriteEnsureOwnInvariants(writer, definition, what);
        WriteGetInvariantViolations(writer, definition, what);
        WriteEnsureInvariants(writer, definition, what);
    }

    /// <summary>The child entity collections this type holds, which are the ones an aggregate answers for.</summary>
    private static List<CollectionPropertyInfo> Children(EntityDefinition definition)
    {
        var children = new List<CollectionPropertyInfo>();
        foreach (var collection in definition.Collections)
        {
            if (collection.ElementIsEntity)
            {
                children.Add(collection);
            }
        }

        return children;
    }

    /// <summary>
    /// Whether either <c>Ensure</c> method has something to collect before it throws. With no rules and
    /// no children there is nothing to merge, and both stay the bare call to the seam that the compiler
    /// can erase outright.
    /// </summary>
    private static bool Collects(EntityDefinition definition)
        => definition.Invariants.Count > 0 || Children(definition).Count > 0;

    /// <summary>What the self-only methods say they run, which is the seam alone until rules are stated.</summary>
    private static string Runs(EntityDefinition definition, string what)
        => definition.Invariants.Count == 0
            ? "this " + what + "'s <c>CheckInvariants()</c> seam"
            : "this " + what + "'s rules and its <c>CheckInvariants()</c> seam";

    /// <summary>
    /// The rules this type states, one instance of each. Static and created once because an
    /// <c>IInvariant&lt;T&gt;</c> is stateless by contract: the entity it judges is the argument, never a
    /// field. Nothing is written when the type states none, so an entity that has only a seam (or
    /// nothing at all) pays for no array and no loop.
    /// </summary>
    private static void WriteInvariantRules(CodeWriter writer, EntityDefinition definition)
    {
        if (definition.Invariants.Count == 0)
        {
            return;
        }

        writer.Line();
        writer.Line("/// <summary>The rules declared inside this type, created once and reused for every check.</summary>");
        writer.Line("private static readonly " + InvariantOf(definition) + "[] __invariants =");
        writer.Line("[");
        foreach (var invariant in definition.Invariants)
        {
            writer.Line("    new " + invariant + "(),");
        }

        writer.Line("];");
    }

    /// <summary>
    /// The one routine that decides what this object alone has broken. All four public methods are built
    /// on it, which is what keeps the stage that asks and the stage that insists from ever disagreeing.
    /// <para>
    /// It appends to a list the caller owns rather than returning one, so the walk over child entities
    /// can add to the same list without a second allocation, and so the consistent case still allocates
    /// nothing at all: the list stays null until something is actually wrong.
    /// </para>
    /// <para>
    /// Every violation it builds names this type and this id. A caller handed a list by an aggregate has
    /// no other way to tell which child of it is the problem, and reading that out of a message is not a
    /// thing anyone should have to do.
    /// </para>
    /// </summary>
    private static void WriteCollectInvariantViolations(CodeWriter writer, EntityDefinition definition)
    {
        var violation = KnownTypes.InvariantViolation;
        var list = "global::System.Collections.Generic.List<" + violation + ">";
        var reporter = ", typeof(" + definition.Type.FullyQualifiedName + "), Id)";

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// Runs every rule and then the seam, and adds what is broken to <paramref name=\"violations\"/>.");
        writer.Line("/// Never throws for a broken rule: throwing is the boundary's job, and it is done in exactly");
        writer.Line("/// one place.");
        writer.Line("/// </summary>");
        writer.Line("/// <param name=\"violations\">");
        writer.Line("/// The list being built, created on the first failure and left <see langword=\"null\"/> while");
        writer.Line("/// there is none. This runs for every changed entity on every save, so the consistent case is");
        writer.Line("/// the one that has to cost nothing.");
        writer.Line("/// </param>");
        writer.Line("/// <param name=\"seamFailure\">");
        writer.Line("/// What <c>CheckInvariants()</c> threw, so the caller that throws can keep it as the inner");
        writer.Line("/// exception and lose no stack trace, or <see langword=\"null\"/> when the seam was happy.");
        writer.Line("/// </param>");
        writer.Line("private void CollectInvariantViolations(");
        writer.Line("    ref " + list + "? violations,");
        using (writer.Block("    out " + KnownTypes.InvariantViolationException + "? seamFailure)"))
        {
            if (definition.Invariants.Count > 0)
            {
                using (writer.Block("foreach (var invariant in __invariants)"))
                {
                    writer.Line("// A null message means the rule holds, which is why it is a string and not an object.");
                    writer.Line("var message = invariant.Check(this);");
                    using (writer.Block("if (message is not null)"))
                    {
                        writer.Line("violations ??= new " + list + "();");
                        writer.Line("violations.Add(new " + violation + "(invariant.Code, message" + reporter + ");");
                    }
                }

                writer.Line();
            }

            writer.Line("seamFailure = null;");
            writer.Line();
            using (writer.Block("try"))
            {
                writer.Line("CheckInvariants();");
            }

            using (writer.Block("catch (" + KnownTypes.InvariantViolationException + " failure)"))
            {
                writer.Line("// The seam reports by throwing, because that is the shape that lets it be erased when");
                writer.Line("// nobody implements it. Catching it here is what lets the asking stage see what it");
                writer.Line("// found without every caller having to catch.");
                writer.Line("seamFailure = failure;");
                writer.Line("violations ??= new " + list + "();");
                writer.Line();
                writer.Line("var reported = failure.Violations;");
                using (writer.Block("if (reported.Count == 0)"))
                {
                    writer.Line("// Thrown with a message of its own rather than through InvariantViolation(...), so");
                    writer.Line("// the message is all there is to report.");
                    writer.Line("violations.Add(new " + violation + "(" + violation + ".SeamCode, failure.Message" + reporter + ");");
                }

                using (writer.Block("else"))
                {
                    using (writer.Block("for (var index = 0; index < reported.Count; index++)"))
                    {
                        writer.Line("violations.Add(new " + violation + "(" + violation + ".SeamCode, reported[index]" + reporter + ");");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The walk that makes an aggregate answer for what it holds. Written only for a type that holds
    /// child entities, so nothing else pays for it.
    /// <para>
    /// It reads the generated backing field rather than the property, because the property hands out a
    /// read-only wrapper and this would allocate one per call, on a path whose whole point is that the
    /// consistent case allocates nothing.
    /// </para>
    /// </summary>
    private static void WriteCollectChildInvariantViolations(CodeWriter writer, EntityDefinition definition, string what)
    {
        var children = Children(definition);
        if (children.Count == 0)
        {
            return;
        }

        var violation = KnownTypes.InvariantViolation;
        var list = "global::System.Collections.Generic.List<" + violation + ">";

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// Asks every child entity this " + what + " holds what it has broken, and adds the answers to");
        writer.Line("/// <paramref name=\"violations\"/>. A child answers the same way, so a grandchild is reached");
        writer.Line("/// without this method having to know it exists.");
        writer.Line("/// <para>");
        writer.Line("/// Collections only, and never a single reference to another entity: a child that points back");
        writer.Line("/// at its parent would recurse forever, and the visited set that would stop it costs an");
        writer.Line("/// allocation on every call, including the consistent one that has to stay free. Owning its");
        writer.Line("/// children in collections is the shape an aggregate actually has, so ruling cycles out by");
        writer.Line("/// construction misses nothing real.");
        writer.Line("/// </para>");
        writer.Line("/// </summary>");
        using (writer.Block("private void CollectChildInvariantViolations(ref " + list + "? violations)"))
        {
            for (var child = 0; child < children.Count; child++)
            {
                if (child > 0)
                {
                    writer.Line();
                }

                using (writer.Block("foreach (var child in " + children[child].FieldName + ")"))
                {
                    using (writer.Block("if (child is null)"))
                    {
                        writer.Line("continue;");
                    }

                    writer.Line();
                    writer.Line("var broken = child.GetInvariantViolations();");
                    using (writer.Block("if (broken.Count == 0)"))
                    {
                        writer.Line("continue;");
                    }

                    writer.Line();
                    writer.Line("violations ??= new " + list + "();");
                    using (writer.Block("for (var index = 0; index < broken.Count; index++)"))
                    {
                        writer.Line("violations.Add(broken[index]);");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The one place either <c>Ensure</c> method throws from. Not written for a type with nothing to
    /// collect, whose two <c>Ensure</c> methods are the bare call to the seam instead.
    /// </summary>
    private static void WriteThrowInvariantViolations(CodeWriter writer, EntityDefinition definition, string what)
    {
        if (!Collects(definition))
        {
            return;
        }

        var violation = KnownTypes.InvariantViolation;
        var list = "global::System.Collections.Generic.List<" + violation + ">";

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// Throws the single exception both stages raise, naming this " + what + ", its id and every");
        writer.Line("/// rule found broken.");
        writer.Line("/// </summary>");
        writer.Line("/// <param name=\"violations\">Everything that was found, this " + what + "'s own first.</param>");
        writer.Line("/// <param name=\"own\">");
        writer.Line("/// How many of them are this " + what + "'s own. A violation past that point was reported by a");
        writer.Line("/// child, and is phrased with the child's own type and id, because the exception itself names");
        writer.Line("/// only the boundary that was asked. The ones before it need no such prefix: the exception");
        writer.Line("/// already says whose they are.");
        writer.Line("/// </param>");
        writer.Line("/// <param name=\"seamFailure\">What the seam threw, kept as the inner exception so no stack trace is lost.</param>");
        writer.Line("private void ThrowInvariantViolations(");
        writer.Line("    " + list + " violations,");
        writer.Line("    int own,");
        using (writer.Block("    " + KnownTypes.InvariantViolationException + "? seamFailure)"))
        {
            writer.Line("// Built by hand rather than with LINQ: nothing is imported into a generated file, and an");
            writer.Line("// array of the messages is what the exception already knows how to phrase.");
            writer.Line("var messages = new string[violations.Count];");
            using (writer.Block("for (var index = 0; index < violations.Count; index++)"))
            {
                writer.Line("messages[index] = index < own ? violations[index].Message : violations[index].ToString();");
            }

            writer.Line();
            writer.Line("throw new " + KnownTypes.InvariantViolationException
                + "(typeof(" + definition.Type.FullyQualifiedName + "), Id, messages, seamFailure);");
        }
    }

    /// <summary>The stage that asks, about this object alone. What a caller walking the graph itself wants.</summary>
    private static void WriteGetOwnInvariantViolations(CodeWriter writer, EntityDefinition definition, string what)
    {
        var violation = KnownTypes.InvariantViolation;

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// Runs " + Runs(definition, what) + " and returns every rule that is");
        writer.Line("/// broken, without throwing and without asking the children this " + what + " holds. Empty means");
        writer.Line("/// this object is consistent and says nothing about what is inside it.");
        writer.Line("/// <para>");
        writer.Line("/// For a caller that already walks the graph and asks each object in it separately, which is");
        writer.Line("/// what the save does from the change tracker. Anything else wants");
        writer.Line("/// <see cref=\"GetInvariantViolations\"/>.");
        writer.Line("/// </para>");
        writer.Line("/// </summary>");
        using (writer.Block("public override " + ViolationList + " GetOwnInvariantViolations()"))
        {
            writer.Line("global::System.Collections.Generic.List<" + violation + ">? violations = null;");
            writer.Line("CollectInvariantViolations(ref violations, out _);");
            writer.Line();
            using (writer.Block("if (violations is null)"))
            {
                writer.Line("return global::System.Array.Empty<" + violation + ">();");
            }

            writer.Line();
            writer.Line("return violations;");
        }
    }

    /// <summary>
    /// The stage that insists, about this object alone, in the two shapes it has. With something to
    /// collect it collects and then throws once, naming this type and its id and keeping whatever the
    /// seam threw as the inner exception. With nothing to collect it stays the call to the seam it has
    /// always been: the compiler erases that call when nobody implements the seam, leaving a method with
    /// an empty body, and the exception the seam threw itself reaches the caller unwrapped, which is
    /// worth more than a wrapper that would only repeat what it already says.
    /// </summary>
    private static void WriteEnsureOwnInvariants(CodeWriter writer, EntityDefinition definition, string what)
    {
        var violation = KnownTypes.InvariantViolation;

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// Runs " + Runs(definition, what) + " and throws when it finds something");
        writer.Line("/// broken, without asking the children this " + what + " holds. Called by");
        writer.Line("/// <c>DDDToolkit.EntityFramework</c> on every object a save writes, which is why it must not");
        writer.Line("/// walk: the save reaches the children itself, from the change tracker, and would otherwise");
        writer.Line("/// be told about each of them twice.");
        writer.Line("/// <para>");
        if (definition.Invariants.Count == 0)
        {
            writer.Line("/// Whatever the seam throws reaches the caller as it was thrown. At the save, broken is no");
            writer.Line("/// longer an answer.");
        }
        else
        {
            writer.Line("/// Throws one <c>InvariantViolationException</c> naming this type, its id and every rule it");
            writer.Line("/// found broken, with whatever the seam threw as its inner exception. At the save, broken is");
            writer.Line("/// no longer an answer.");
        }

        writer.Line("/// </para>");
        writer.Line("/// </summary>");
        using (writer.Block("public override void EnsureOwnInvariants()"))
        {
            if (definition.Invariants.Count == 0)
            {
                writer.Line("CheckInvariants();");
                return;
            }

            writer.Line("global::System.Collections.Generic.List<" + violation + ">? violations = null;");
            writer.Line("CollectInvariantViolations(ref violations, out var seamFailure);");
            writer.Line();
            using (writer.Block("if (violations is null)"))
            {
                writer.Line("return;");
            }

            writer.Line();
            writer.Line("ThrowInvariantViolations(violations, violations.Count, seamFailure);");
        }
    }

    /// <summary>
    /// The stage that asks, about the whole aggregate. Delegates when this type holds no child entities:
    /// with nothing inside it, the two questions have one answer, and one method having it keeps them
    /// from drifting.
    /// </summary>
    private static void WriteGetInvariantViolations(CodeWriter writer, EntityDefinition definition, string what)
    {
        var violation = KnownTypes.InvariantViolation;
        var children = Children(definition);

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// Runs " + Runs(definition, what) + ", and then asks every child entity this");
        writer.Line("/// " + what + " holds, and returns every rule that is broken, without throwing. Empty means the");
        writer.Line("/// whole " + what + " is consistent. Ask this before the save, while \"not yet\" is still an answer");
        writer.Line("/// you want to handle.");
        writer.Line("/// <para>");
        writer.Line("/// Each violation names the entity that reported it, so a caller can tell which child is the");
        writer.Line("/// problem without reading a message. This asks every child this " + what + " holds, where the");
        writer.Line("/// save asks only the ones it is about to write, so an empty answer here is never contradicted");
        writer.Line("/// by the save.");
        writer.Line("/// </para>");
        writer.Line("/// </summary>");

        if (children.Count == 0)
        {
            writer.Line("public override " + ViolationList + " GetInvariantViolations() => GetOwnInvariantViolations();");
            return;
        }

        using (writer.Block("public override " + ViolationList + " GetInvariantViolations()"))
        {
            writer.Line("global::System.Collections.Generic.List<" + violation + ">? violations = null;");
            writer.Line("CollectInvariantViolations(ref violations, out _);");
            writer.Line("CollectChildInvariantViolations(ref violations);");
            writer.Line();
            using (writer.Block("if (violations is null)"))
            {
                writer.Line("return global::System.Array.Empty<" + violation + ">();");
            }

            writer.Line();
            writer.Line("return violations;");
        }
    }

    /// <summary>
    /// The stage that insists, about the whole aggregate. With no children to walk it is the self-only
    /// method, either delegated to or, when there is nothing to collect either, written out again as the
    /// bare call to the seam so that the compiler can erase this body too.
    /// </summary>
    private static void WriteEnsureInvariants(CodeWriter writer, EntityDefinition definition, string what)
    {
        var violation = KnownTypes.InvariantViolation;
        var children = Children(definition);

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// Runs " + Runs(definition, what) + " and the invariants of every child entity");
        writer.Line("/// this " + what + " holds, and throws when it finds something broken. This is the question a");
        writer.Line("/// command handler asks after it has acted: the " + what + " is the consistency boundary, so");
        writer.Line("/// answering for it means answering for what is inside it. No database is involved.");
        writer.Line("/// <para>");
        if (!Collects(definition))
        {
            writer.Line("/// Whatever the seam throws reaches the caller as it was thrown. At the save, broken is no");
            writer.Line("/// longer an answer.");
        }
        else
        {
            writer.Line("/// Throws one <c>InvariantViolationException</c> naming this type, its id and every rule it");
            writer.Line("/// found broken, a child's phrased with that child's own type and id, and with whatever the");
            writer.Line("/// seam threw as its inner exception. At the save, broken is no longer an answer.");
        }

        writer.Line("/// </para>");
        writer.Line("/// </summary>");

        if (children.Count == 0)
        {
            if (!Collects(definition))
            {
                // Written out rather than delegated: a call to EnsureOwnInvariants is a call the compiler
                // cannot erase, and an entity that states nothing must keep an empty method body.
                using (writer.Block("public override void EnsureInvariants()"))
                {
                    writer.Line("CheckInvariants();");
                }

                return;
            }

            writer.Line("public override void EnsureInvariants() => EnsureOwnInvariants();");
            return;
        }

        using (writer.Block("public override void EnsureInvariants()"))
        {
            writer.Line("global::System.Collections.Generic.List<" + violation + ">? violations = null;");
            writer.Line("CollectInvariantViolations(ref violations, out var seamFailure);");
            writer.Line();
            writer.Line("// Where this " + what + "'s own violations end and its children's begin, so that the one");
            writer.Line("// exception can name the child a violation came from without repeating this " + what + " for");
            writer.Line("// the ones that are its own.");
            writer.Line("var own = violations is null ? 0 : violations.Count;");
            writer.Line();
            writer.Line("CollectChildInvariantViolations(ref violations);");
            writer.Line();
            using (writer.Block("if (violations is null)"))
            {
                writer.Line("return;");
            }

            writer.Line();
            writer.Line("ThrowInvariantViolations(violations, own, seamFailure);");
        }
    }

    /// <summary>The rule interface closed over the type being generated.</summary>
    private static string InvariantOf(EntityDefinition definition)
        => KnownTypes.InvariantInterface + "<" + definition.Type.FullyQualifiedName + ">";

    /// <summary>What both stages hand back: the violations, in the order they were found.</summary>
    private static string ViolationList
        => "global::System.Collections.Generic.IReadOnlyList<" + KnownTypes.InvariantViolation + ">";

    private static string View(CollectionPropertyInfo collection, bool readOnlySetAvailable) => collection.Backing switch
    {
        CollectionBacking.HashSet when readOnlySetAvailable => "new global::System.Collections.ObjectModel.ReadOnlySet<" + collection.ElementType + ">(" + collection.FieldName + ")",
        CollectionBacking.HashSet => collection.FieldName,
        _ => collection.FieldName + ".AsReadOnly()",
    };
}
