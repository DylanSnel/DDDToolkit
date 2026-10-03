using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Translates each <c>[RowAccess&lt;TAggregate&gt;]</c> rule's <c>Allows</c> method into SQL, and writes it
/// into the rule as the constant <c>RowAccessSql</c>, where the Supabase export finds it.
/// <code>
/// public static bool Allows(Order order, Caller caller)
///     =&gt; order.PlacedBy == null || order.PlacedBy?.Value == caller.UserId;
/// // (({col:PlacedBy} IS NULL) OR ({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid}))
/// </code>
/// </summary>
/// <remarks>
/// The SQL is a template, because two things in it are not the rule's to know. Which column a property is
/// stored in is Entity Framework's to say, so a property is <c>{col:Path}</c>, and an enum constant compared
/// with one is <c>{val:Path:Number}</c>, to be put through the property's converter. And what the caller is
/// depends on the database, so it is <c>{caller:uid}</c>, <c>{caller:signedin}</c>, <c>{caller:role}</c> or
/// <c>{caller:claim:path}</c>. Braces in a string constant are doubled.
/// <para>
/// An <c>[AccessFunction]</c> is translated the same way, and may also ask the aggregate's entities:
/// <c>project.Members.Any(member =&gt; ...)</c> is <c>{exists:Members:e1}...{/exists}</c>, the entity's
/// properties inside it <c>{col:e1:Path}</c>, the entities of that entity
/// <c>{exists:e1:Duties:e2}...{/exists}</c>, and its own parameters after the caller <c>{arg:1}</c>,
/// <c>{arg:2}</c>. A rule asks the function about its own row with <c>{call:name}</c>, which the export
/// writes with the row's key, or <c>{fn:name}({key}, ...)</c> when it passes more; and about a key it holds,
/// from its definition or its contract, with <c>{fn:name}(...)</c>. A name is <c>schema.name</c>, or a
/// logical <c>owner/name</c> the export resolves to the schema of the context that defines it.
/// </para>
/// <para>
/// A question only the database answers, an <c>[AccessSet]</c> or <c>[AccessScalar]</c> method of an
/// <c>[AccessFunctions]</c> class, or the <c>Ids</c> of a set-shaped access function, is asked once per
/// statement: <c>set.Contains(e)</c> is <c>(e = ANY (ARRAY(SELECT {fn:name}(...))))</c>, and a scalar
/// question <c>(SELECT {fn:name}(...))</c>.
/// </para>
/// <para>
/// The translation keeps C#'s meaning rather than SQL's, so the method and the policy answer alike for the
/// same row and caller: <c>==</c> between values that cannot be null is <c>=</c>, and otherwise
/// <c>IS NOT DISTINCT FROM</c>, because in C# a null equals a null; a comparison is
/// <c>coalesce(..., FALSE)</c>, because a lifted comparison with a null is false in C#, never unknown. A
/// scalar question has no answer in C# to keep, and answers null for a caller it does not know, so a
/// comparison that asks one is SQL's own, <c>a = b</c>, <c>a &lt;&gt; b</c>, <c>a &lt; b</c>: null for such a
/// caller, which a policy counts as no and which stays null under <c>NOT</c>, so such a caller never passes
/// it, negated or not. What cannot be translated is DDD00039, on that expression.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class RowAccessGenerator : IIncrementalGenerator
{
    private const string DatabaseOnlyException = "global::DDDToolkit.Abstractions.Access.DatabaseOnlyException";

    private const string AccessSetType = "global::DDDToolkit.Abstractions.Access.AccessSet";

    private const string ShapeType = "global::" + KnownTypes.AttributesNamespace + ".AccessFunctionShape";

    /// <summary>How the generated members name a type: fully qualified, with its nullable annotations.</summary>
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var rules = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.RowAccessAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => Translate(syntaxContext, isFunction: false, cancellationToken));

        var functions = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.AccessFunctionAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => Translate(syntaxContext, isFunction: true, cancellationToken));

        var contracts = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.AccessFunctionContractAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (syntaxContext, _) => Declare(syntaxContext));

        var questions = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.AccessFunctionsAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (syntaxContext, _) => DeclareQuestions(syntaxContext));

        var setsOutside = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.AccessSetAttribute,
            predicate: static (node, _) => node is MethodDeclarationSyntax,
            transform: static (syntaxContext, _) => Outside(syntaxContext));

        var scalarsOutside = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.AccessScalarAttribute,
            predicate: static (node, _) => node is MethodDeclarationSyntax,
            transform: static (syntaxContext, _) => Outside(syntaxContext));

        context.RegisterSourceOutput(rules, static (production, rule) => Produce(production, rule, ".RowAccess"));
        context.RegisterSourceOutput(functions, static (production, function) => Produce(production, function, ".AccessFunction"));
        context.RegisterSourceOutput(contracts, static (production, contract) => Produce(production, contract, ".AccessFunctionContract"));
        context.RegisterSourceOutput(questions, static (production, declared) => Produce(production, declared, ".AccessFunctions"));
        context.RegisterSourceOutput(setsOutside, static (production, diagnostics) => diagnostics.ReportAll(production));
        context.RegisterSourceOutput(scalarsOutside, static (production, diagnostics) => diagnostics.ReportAll(production));
    }

    private static void Produce(SourceProductionContext production, RowAccessDefinition definition, string suffix)
    {
        definition.Diagnostics.ReportAll(production);
        if (definition.Members.Count > 0)
        {
            production.AddSource(definition.Type.HintName(suffix), SourceText.From(Emit(definition), Encoding.UTF8));
        }
    }

    private static RowAccessDefinition Translate(GeneratorAttributeSyntaxContext attributed, bool isFunction, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)attributed.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)attributed.TargetNode;
        var type = DefinitionFactory.CreateTypeInfo(symbol, syntax);
        var diagnostics = new List<DiagnosticInfo>();

        var attribute = attributed.Attributes[0];
        var aggregate = attribute.AttributeClass?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        if (aggregate is null || aggregate.TypeKind == TypeKind.Error)
        {
            return RowAccessDefinition.Nothing(type);
        }

        if (!IsAggregateRoot(aggregate))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleNotOnAggregateRoot, LocationInfo.From(syntax.Identifier), symbol.Name, aggregate.Name));
        }

        if (!symbol.IsStatic || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleShape, LocationInfo.From(syntax.Identifier), symbol.Name, "to be a static partial class, so the generator can add its SQL"));
        }

        FunctionName? name = null;
        var shape = isFunction ? ShapeOf(attribute) : Shape.Row;
        if (isFunction)
        {
            var written = NameOf(FunctionNameOf(attribute), OwnerOf(symbol));
            ReportName(written, symbol.Name, LocationInfo.From(syntax.Identifier), diagnostics);
            name = written;
        }

        var allows = symbol.GetMembers("Allows").OfType<IMethodSymbol>().ToList();
        var method = allows.Count == 1 ? allows[0] : null;
        var declaration = method?.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken)).OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var body = declaration?.ExpressionBody?.Expression
            ?? (declaration?.Body is { Statements.Count: 1 } block && block.Statements[0] is ReturnStatementSyntax { Expression: { } returned } ? returned : null);

        if (method is null
            || declaration is null
            || body is null
            || !method.IsStatic
            || method.IsGenericMethod
            || method.ReturnType.SpecialType != SpecialType.System_Boolean
            || (isFunction ? method.Parameters.Length < 2 : method.Parameters.Length != 2)
            || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type.OriginalDefinition, aggregate.OriginalDefinition)
            || method.Parameters[1].Type.ToDisplayString() != KnownTypes.Caller)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.RowAccessRuleShape,
                LocationInfo.From(declaration?.Identifier ?? syntax.Identifier),
                symbol.Name,
                $"one method 'public static bool Allows({aggregate.Name} {Camel(aggregate.Name)}, Caller caller)' whose body is a single expression"
                + (isFunction ? ", and may take strings, numbers, flags, Guids and ids after the caller" : "")));
            return RowAccessDefinition.Failed(type, diagnostics);
        }

        // An access function's parameters after the caller: the SQL function's own, after the key.
        var extras = method.Parameters.Skip(2).ToList();
        var sqlTypes = new List<string>(extras.Count);
        foreach (var extra in extras)
        {
            if (extra.RefKind != RefKind.None || extra.IsParams || SqlTypeOf(extra.Type) is not { } sqlType)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.RowAccessRuleShape,
                    LocationInfo.From(extra),
                    symbol.Name,
                    $"parameters after the caller that the SQL function can take: '{extra.Name}' is {extra.Type.ToDisplayString()}, and a parameter is a string, bool, int, long, Guid or an id"));
                continue;
            }

            sqlTypes.Add(sqlType);
        }

        var key = isFunction ? KeyOf(aggregate) : null;
        if (isFunction && shape == Shape.Set && key is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.RowAccessRuleShape,
                LocationInfo.From(syntax.Identifier),
                symbol.Name,
                $"an aggregate whose key is one column, because a set-shaped function answers with the keys of the rows it allows, and {aggregate.Name} is keyed on more than its id"));
        }

        if (diagnostics.HasErrorsIn())
        {
            return RowAccessDefinition.Failed(type, diagnostics);
        }

        var model = attributed.SemanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
        var sql = new Translator(model, method.Parameters[0], method.Parameters[1], extras, isFunction, diagnostics).Translate(body);

        if (diagnostics.HasErrorsIn())
        {
            return RowAccessDefinition.Failed(type, diagnostics);
        }

        List<string> members = [SqlConstant(sql)];
        if (isFunction && name is { Logical: { } logical } resolved)
        {
            members.Add(NameConstant(logical, contract: false));
            members.Add(FunctionConstants(resolved.Owner, string.Join(", ", sqlTypes), shape));

            // The C# parameters the generated question takes after the key: those of Allows after the caller.
            var parameters = extras.Select(extra => extra.Type.ToDisplayString(TypeFormat) + " " + Identifier(extra.Name)).ToList();
            var names = extras.Select(extra => extra.Name).ToList();
            if (shape == Shape.Set)
            {
                members.Add(IdsMethod(logical, key!.Value.Type, parameters, names, "public", partial: false));
            }
            else if (key is { } byKey)
            {
                var keyName = names.Contains(byKey.Parameter) ? "key" : byKey.Parameter;
                members.Add(AllowsMethod(logical, byKey.Type, keyName, parameters, names, "public", partial: false));
            }
        }

        return new RowAccessDefinition(type, new EquatableArray<DiagnosticInfo>(diagnostics), new EquatableArray<string>(members));
    }

    /// <summary>
    /// Whether the rule is about an aggregate root the export can find a table for. A package's parent is
    /// declared a root, but it is abstract and never mapped: the export only knows the application's classes,
    /// and a rule about the parent would be left out of it without a word. The rule belongs on the class
    /// declared with the parent's template.
    /// </summary>
    private static bool IsAggregateRoot(INamedTypeSymbol type)
        => EntityDeclarations.IsAggregateRoot(type) && !EntityDeclarations.IsBase(type);

    /// <summary>The name an access function, a contract or a question gives its function, as written, or null.</summary>
    private static string? FunctionNameOf(AttributeData attribute)
        => attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name ? name : null;

    /// <summary>What an access function or a contract answers: about one row, or with a set of keys.</summary>
    private static Shape ShapeOf(AttributeData attribute)
        => attribute.NamedArguments.FirstOrDefault(static argument => argument.Key == "Shape").Value.Value is int value && value == (int)Shape.Set
            ? Shape.Set
            : Shape.Row;

    /// <summary>Whether the attribute says its shape itself, rather than leaving it at the default.</summary>
    private static bool SaysItsShape(AttributeData attribute)
        => attribute.NamedArguments.Any(static argument => argument.Key == "Shape");

    /// <summary>The <c>[AccessFunction&lt;T&gt;]</c> on <paramref name="type"/>, or null.</summary>
    private static AttributeData? AccessFunctionOf(ITypeSymbol type)
        => type.GetAttributes().FirstOrDefault(static attribute =>
            attribute.AttributeClass?.OriginalDefinition is { MetadataName: "AccessFunctionAttribute`1" } function
            && function.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace);

    /// <summary>The <c>[AccessFunctionContract&lt;TKey&gt;]</c> on <paramref name="type"/>, or null.</summary>
    private static AttributeData? AccessFunctionContractOf(ITypeSymbol type)
        => type.GetAttributes().FirstOrDefault(static attribute =>
            attribute.AttributeClass?.OriginalDefinition is { MetadataName: "AccessFunctionContractAttribute`1" } contract
            && contract.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace);

    /// <summary>The attribute named <paramref name="metadataName"/>, of the toolkit's attributes, on <paramref name="symbol"/>, or null.</summary>
    private static AttributeData? AttributeOf(ISymbol symbol, string metadataName)
        => symbol.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass is { } attributeClass && attributeClass.ToDisplayString() == metadataName);

    /// <summary>
    /// The owner of the functions <paramref name="type"/> names without a schema: the <c>Owner</c> of its
    /// <c>[AccessFunctions]</c>, or else the module its assembly declares, as a module's name is written in a
    /// migration's file name; null when there is neither.
    /// </summary>
    private static string? OwnerOf(INamedTypeSymbol type)
    {
        if (AttributeOf(type, KnownTypes.AccessFunctionsAttribute) is { } declared
            && declared.NamedArguments.FirstOrDefault(static argument => argument.Key == "Owner").Value.Value is string owner
            && !string.IsNullOrWhiteSpace(owner))
        {
            return NormalizedOwner(owner);
        }

        return ModuleBoundary.ModuleOf(type.ContainingAssembly) is { } module ? NormalizedOwner(module) : null;
    }

    /// <summary>
    /// An owner as the export writes a module's name: lower case, with anything but ASCII letters and digits
    /// a dash, and no dash at either end. The same as <c>SupabaseMigrations.NormalizeModuleName</c>.
    /// </summary>
    private static string NormalizedOwner(string name)
    {
        var normalized = new StringBuilder(name.Length);
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            normalized.Append(character is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? character : '-');
        }

        var result = normalized.ToString().Trim('-');
        return result.Length > 0 ? result : "module";
    }

    /// <summary>
    /// A function's name, <paramref name="written"/>, as the export resolves it: <c>schema.name</c> and
    /// <c>owner/name</c> as they are, and a bare name relative to <paramref name="owner"/>.
    /// </summary>
    private static FunctionName NameOf(string? written, string? owner)
    {
        if (written is null || written.Length == 0)
        {
            return FunctionName.Invalid(written ?? "");
        }

        var slash = written.Split('/');
        if (slash.Length == 2)
        {
            return IsOwnerName(slash[0]) && IsIdentifier(slash[1]) ? new FunctionName(written, written, slash[0], Qualified: false) : FunctionName.Invalid(written);
        }

        if (slash.Length > 2)
        {
            return FunctionName.Invalid(written);
        }

        var dot = written.Split('.');
        if (dot.Length == 2)
        {
            return dot.All(IsIdentifier) ? new FunctionName(written, written, owner, Qualified: true) : FunctionName.Invalid(written);
        }

        if (dot.Length > 2 || !IsIdentifier(written))
        {
            return FunctionName.Invalid(written);
        }

        return owner is null ? new FunctionName(written, null, null, Qualified: false) : new FunctionName(written, owner + "/" + written, owner, Qualified: false);
    }

    /// <summary>
    /// Reports what is wrong with a function's name: DDD00052 for a relative one with no owner, DDD00038 for
    /// one that is no name at all.
    /// </summary>
    private static void ReportName(FunctionName name, string what, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        if (name.IsInvalid)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.RowAccessRuleShape,
                location,
                what,
                "a function name such as \"projects.is_member\", \"projects/is_member\" or \"is_member\", relative to its module: each part letters, digits and underscores, and the owner lower case letters, digits and dashes"));
        }
        else if (name.Logical is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.FunctionNameWithoutOwner, location, name.Written));
        }
    }

    /// <summary>A plain SQL identifier: a letter or <c>_</c>, then letters, digits and <c>_</c>.</summary>
    private static bool IsIdentifier(string part)
        => part.Length > 0 && (char.IsLetter(part[0]) || part[0] == '_') && part.All(static c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>An owner as it is written in a logical name: lower case letters, digits and dashes, no dash at either end.</summary>
    private static bool IsOwnerName(string owner)
        => owner.Length > 0 && owner[0] != '-' && owner[owner.Length - 1] != '-' && owner.All(static c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>
    /// The SQL type a parameter of a function is, for the C# types a question may take: <c>string</c>,
    /// <c>bool</c>, <c>int</c>, <c>long</c>, <c>Guid</c>, and an id, which is the type of its value. An empty
    /// string for a type parameter that is an id, when <paramref name="typeParameters"/> allows one, since a
    /// question over the host's ids has no SQL of its own; null for anything else.
    /// </summary>
    private static string? SqlTypeOf(ITypeSymbol type, bool typeParameters = false)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_String:
                return "text";
            case SpecialType.System_Boolean:
                return "boolean";
            case SpecialType.System_Int32:
                return "integer";
            case SpecialType.System_Int64:
                return "bigint";
        }

        if (type.ToDisplayString() == "System.Guid")
        {
            return "uuid";
        }

        if (type is ITypeParameterSymbol parameter)
        {
            return typeParameters && parameter.ConstraintTypes.Any(IsEntityIdInterface) ? "" : null;
        }

        // An id: [EntityId<T>] where it is declared, IEntityId<T> once its generated part is compiled.
        var value = type.OriginalDefinition.GetAttributes()
            .Where(static attribute => attribute.AttributeClass?.OriginalDefinition.MetadataName == "EntityIdAttribute`1")
            .Select(static attribute => attribute.AttributeClass!.TypeArguments.FirstOrDefault())
            .FirstOrDefault()
            ?? type.AllInterfaces
                .Where(static contract => contract.OriginalDefinition.ToDisplayString() == "DDDToolkit.Abstractions.Interfaces.IEntityId<out TValue>"
                    || contract.OriginalDefinition.MetadataName == "IEntityId`1")
                .Select(static contract => contract.TypeArguments.FirstOrDefault())
                .FirstOrDefault();

        return value is null or ITypeParameterSymbol || value.TypeKind == TypeKind.Error || SymbolEqualityComparer.Default.Equals(value, type)
            ? null
            : SqlTypeOf(value);
    }

    private static bool IsEntityIdInterface(ITypeSymbol type)
        => type.ToDisplayString() == KnownTypes.EntityIdInterface || type.OriginalDefinition.MetadataName == "IEntityId`1";

    /// <summary>Whether <paramref name="type"/> is <c>AccessSet&lt;T&gt;</c>.</summary>
    private static bool IsAccessSet(ITypeSymbol type)
        => type is INamedTypeSymbol { IsGenericType: true } named
           && named.OriginalDefinition.ContainingNamespace.ToDisplayString() + "." + named.OriginalDefinition.MetadataName == KnownTypes.AccessSet;

    private static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    /// <summary>A C# identifier as code writes it, with <c>@</c> in front of a keyword.</summary>
    private static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static string Accessibility(IMethodSymbol method) => method.DeclaredAccessibility switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => "public",
        Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
        Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
        Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal",
        Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
        _ => "private",
    };

    private static string Emit(RowAccessDefinition definition)
    {
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(definition.Type))
        {
            using (writer.Block(definition.Type.PartialHeader))
            {
                for (var i = 0; i < definition.Members.Count; i++)
                {
                    if (i > 0)
                    {
                        writer.Line();
                    }

                    writer.Lines(definition.Members[i]);
                }
            }
        }

        return writer.ToString();
    }

    private static string SqlConstant(string sql)
        => "/// <summary>\n"
           + "/// <c>Allows</c> as SQL, translated when this compiled. <c>{col:Path}</c> is a property, whose column\n"
           + "/// the export takes from the Entity Framework model, and <c>{caller:...}</c> is the caller, as the\n"
           + "/// database knows them.\n"
           + "/// </summary>\n"
           + "public const string " + KnownTypes.RowAccessSqlField + " = " + SymbolDisplay.FormatLiteral(sql, quote: true) + ";";

    private static string NameConstant(string logical, bool contract)
        => "/// <summary>\n"
           + "/// The function's name as " + (contract ? "the rules of other modules and its definition ask it" : "rules ask it") + ": with its schema, or\n"
           + "/// <c>owner/name</c>, which the export writes in the schema of the context that defines it.\n"
           + "/// </summary>\n"
           + "public const string Name = " + SymbolDisplay.FormatLiteral(logical, quote: true) + ";";

    private static string FunctionConstants(string? owner, string parameters, Shape shape)
        => "/// <summary>The owner a name without a schema is relative to: the module, or the class's <c>[AccessFunctions]</c> owner.</summary>\n"
           + "public const string? RowAccessOwner = " + (owner is null ? "null" : SymbolDisplay.FormatLiteral(owner, quote: true)) + ";\n"
           + "\n"
           + "/// <summary>The SQL types of the function's parameters after the key, from those of <c>Allows</c> after the caller.</summary>\n"
           + "public const string RowAccessParameters = " + SymbolDisplay.FormatLiteral(parameters, quote: true) + ";\n"
           + "\n"
           + "/// <summary>Whether the function answers about one row, or with the keys of every row it allows.</summary>\n"
           + "public const " + ShapeType + " RowAccessShape = " + ShapeType + "." + shape + ";";

    /// <summary>
    /// <c>Allows(key, ...)</c>: the function asked by a key a rule holds, which only the database answers.
    /// A contract declares it, and gets it implemented with <paramref name="partial"/>.
    /// </summary>
    private static string AllowsMethod(string function, string keyType, string keyName, IReadOnlyList<string> parameters, IReadOnlyList<string> names, string accessibility, bool partial)
    {
        List<string> all = [keyType + " " + Identifier(keyName), .. parameters];
        List<string> asked = [keyName, .. names];
        return "/// <summary>\n"
               + "/// This function asked about the aggregate with <paramref name=\"" + keyName + "\"/>, from a row access rule of any\n"
               + "/// aggregate that holds that key: <c>" + function + "(...)</c> in its policy. Only the database can answer it,\n"
               + "/// so called in C# it throws.\n"
               + "/// </summary>\n"
               + "/// <exception cref=\"" + DatabaseOnlyException + "\">Always: the question is the database's.</exception>\n"
               + accessibility + " static " + (partial ? "partial " : "") + "bool Allows(" + string.Join(", ", all) + ") => throw new " + DatabaseOnlyException
               + "(" + SymbolDisplay.FormatLiteral(function + "(" + string.Join(", ", asked) + ")", quote: true) + ");";
    }

    /// <summary><c>Ids(...)</c>: the keys of every aggregate the function allows, which only the database answers.</summary>
    private static string IdsMethod(string function, string keyType, IReadOnlyList<string> parameters, IReadOnlyList<string> names, string accessibility, bool partial)
        => "/// <summary>\n"
           + "/// The keys of every aggregate this function allows, which a row access rule asks with <c>Contains</c>:\n"
           + "/// <c>(key = ANY (ARRAY(SELECT " + function + "(...))))</c> in its policy, once per statement. Only the\n"
           + "/// database can answer it, so called in C# it throws.\n"
           + "/// </summary>\n"
           + "/// <exception cref=\"" + DatabaseOnlyException + "\">Always: the question is the database's.</exception>\n"
           + accessibility + " static " + (partial ? "partial " : "") + AccessSetType + "<" + keyType + "> Ids(" + string.Join(", ", parameters) + ") => throw new " + DatabaseOnlyException
           + "(" + SymbolDisplay.FormatLiteral(function + "(" + string.Join(", ", names) + ")", quote: true) + ");";

    /// <summary>
    /// An <c>[AccessFunctionContract&lt;TKey&gt;]</c>: the published side of an access function, whose
    /// <c>Name</c> and <c>Allows(TKey)</c>, or <c>Ids()</c>, the generator writes so other modules' rules ask
    /// it typed. A contract whose function takes more than the key declares its question, and the generator
    /// implements it.
    /// </summary>
    private static RowAccessDefinition Declare(GeneratorAttributeSyntaxContext attributed)
    {
        var symbol = (INamedTypeSymbol)attributed.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)attributed.TargetNode;
        var type = DefinitionFactory.CreateTypeInfo(symbol, syntax);
        var diagnostics = new List<DiagnosticInfo>();

        var attribute = attributed.Attributes[0];
        var key = attribute.AttributeClass?.TypeArguments.FirstOrDefault();
        if (key is null || key.TypeKind == TypeKind.Error)
        {
            return RowAccessDefinition.Nothing(type);
        }

        if (!symbol.IsStatic || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleShape, LocationInfo.From(syntax.Identifier), symbol.Name, "to be a static partial class, so the generator can add its Allows method"));
        }

        var name = NameOf(FunctionNameOf(attribute), OwnerOf(symbol));
        ReportName(name, symbol.Name, LocationInfo.From(syntax.Identifier), diagnostics);

        var keyType = key.ToDisplayString(TypeFormat);
        var declared = symbol.GetMembers().OfType<IMethodSymbol>().Where(static method => method.Name is "Allows" or "Ids").ToList();
        var shape = ShapeOf(attribute);

        string? question = null;
        if (declared.Count > 1)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleShape, LocationInfo.From(syntax.Identifier), symbol.Name, "one question at most: a static partial Allows or a static partial Ids, which the generator implements"));
        }
        else if (declared.Count == 1 && name.Logical is { } asked)
        {
            question = DeclaredQuestion(symbol, declared[0], key, keyType, asked, attribute, ref shape, diagnostics);
        }

        if (diagnostics.HasErrorsIn() || name.Logical is not { } logical)
        {
            return RowAccessDefinition.Failed(type, diagnostics);
        }

        List<string> members = [NameConstant(logical, contract: true)];
        members.Add(question
            ?? (shape == Shape.Set
                ? IdsMethod(logical, keyType, [], [], "public", partial: false)
                : AllowsMethod(logical, keyType, Camel(key.Name), [], [], "public", partial: false)));

        return new RowAccessDefinition(type, new EquatableArray<DiagnosticInfo>(diagnostics), new EquatableArray<string>(members));
    }

    /// <summary>
    /// The implementation of the question a contract declares, <c>static partial bool Allows(TKey key, ...)</c>
    /// or <c>static partial AccessSet&lt;TKey&gt; Ids(...)</c>, or null with a diagnostic when it has another shape.
    /// </summary>
    private static string? DeclaredQuestion(
        INamedTypeSymbol contract,
        IMethodSymbol method,
        ITypeSymbol key,
        string keyType,
        string function,
        AttributeData attribute,
        ref Shape shape,
        List<DiagnosticInfo> diagnostics)
    {
        var isSet = method.Name == "Ids";
        var expected = isSet
            ? $"'static partial AccessSet<{key.Name}> Ids(...)' or 'static partial bool Allows({key.Name} key, ...)', declared without a body for the generator to implement, taking strings, numbers, flags, Guids and ids"
            : $"'static partial bool Allows({key.Name} key, ...)' or 'static partial AccessSet<{key.Name}> Ids(...)', declared without a body for the generator to implement, taking strings, numbers, flags, Guids and ids";

        var location = LocationInfo.From(method);
        var parameters = isSet ? method.Parameters : method.Parameters.Skip(1).ToImmutableArrayOrEmpty();
        if (!method.IsStatic
            || !method.IsPartialDefinition
            || method.PartialImplementationPart is not null
            || method.IsGenericMethod
            || (isSet
                ? !IsAccessSet(method.ReturnType) || !SymbolEqualityComparer.Default.Equals(((INamedTypeSymbol)method.ReturnType).TypeArguments[0], key)
                : method.ReturnType.SpecialType != SpecialType.System_Boolean
                  || method.Parameters.Length == 0
                  || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, key))
            || parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsParams || SqlTypeOf(parameter.Type) is null))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleShape, location, contract.Name, expected));
            return null;
        }

        var declaredShape = isSet ? Shape.Set : Shape.Row;
        if (SaysItsShape(attribute) && ShapeOf(attribute) != declaredShape)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.RowAccessRuleShape,
                location,
                contract.Name,
                isSet ? "Shape = AccessFunctionShape.Set to declare Ids, or Allows in its place" : "Shape = AccessFunctionShape.Row, the default, to declare Allows, or Ids in its place"));
            return null;
        }

        shape = declaredShape;
        var typed = parameters.Select(parameter => parameter.Type.ToDisplayString(TypeFormat) + " " + Identifier(parameter.Name)).ToList();
        var names = parameters.Select(parameter => parameter.Name).ToList();
        return isSet
            ? IdsMethod(function, keyType, typed, names, Accessibility(method), partial: true)
            : AllowsMethod(function, keyType, method.Parameters[0].Name, typed, names, Accessibility(method), partial: true);
    }

    /// <summary>
    /// An <c>[AccessFunctions]</c> class: each of its <c>[AccessSet]</c> and <c>[AccessScalar]</c> methods
    /// gets a body that throws, because only the SQL function of its name answers it.
    /// </summary>
    private static RowAccessDefinition DeclareQuestions(GeneratorAttributeSyntaxContext attributed)
    {
        var symbol = (INamedTypeSymbol)attributed.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)attributed.TargetNode;
        var type = DefinitionFactory.CreateTypeInfo(symbol, syntax);
        var diagnostics = new List<DiagnosticInfo>();
        var members = new List<string>();

        var questions = symbol.GetMembers().OfType<IMethodSymbol>()
            .Select(method => (Method: method, Set: AttributeOf(method, KnownTypes.AccessSetAttribute), Scalar: AttributeOf(method, KnownTypes.AccessScalarAttribute)))
            .Where(static question => question.Set is not null || question.Scalar is not null)
            .ToList();

        if (questions.Count > 0 && (!symbol.IsStatic || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleShape, LocationInfo.From(syntax.Identifier), symbol.Name, "to be a static partial class, so the generator can write its questions' bodies"));
            return RowAccessDefinition.Failed(type, diagnostics);
        }

        var owner = OwnerOf(symbol);
        foreach (var (method, set, scalar) in questions)
        {
            var location = LocationInfo.From(method);
            if (set is not null && scalar is not null)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleShape, location, method.Name, "one of [AccessSet] and [AccessScalar], not both"));
                continue;
            }

            var isSet = set is not null;
            var name = NameOf(FunctionNameOf((set ?? scalar)!), owner);
            ReportName(name, method.Name, location, diagnostics);

            var reason = !method.IsStatic || !method.IsPartialDefinition || method.PartialImplementationPart is not null
                ? "to be a static partial method without a body, which the generator writes"
                : isSet && !IsAccessSet(method.ReturnType)
                    ? "to return AccessSet<T>, the set its function answers with"
                    : !isSet && (method.ReturnsVoid || IsAccessSet(method.ReturnType))
                        ? "to return the one value its function answers with; a set-shaped question is an [AccessSet]"
                        : method.Parameters.FirstOrDefault(parameter => parameter.RefKind != RefKind.None || parameter.IsParams || SqlTypeOf(parameter.Type, typeParameters: true) is null) is { } refused
                            ? $"parameters its function can take: '{refused.Name}' is {refused.Type.ToDisplayString()}, and a parameter is a string, bool, int, long, Guid, an id, or a type parameter constrained to IEntityId"
                            : null;

            if (reason is not null)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleShape, location, method.Name, reason));
                continue;
            }

            if (name.Logical is { } logical)
            {
                members.Add(QuestionBody(method, logical, isSet));
            }
        }

        return diagnostics.HasErrorsIn()
            ? RowAccessDefinition.Failed(type, diagnostics)
            : new RowAccessDefinition(type, new EquatableArray<DiagnosticInfo>(diagnostics), new EquatableArray<string>(members));
    }

    /// <summary>The body of a question, which throws: the implementing part of its partial method.</summary>
    private static string QuestionBody(IMethodSymbol method, string function, bool isSet)
    {
        var typeParameters = method.TypeParameters.Length == 0 ? "" : "<" + string.Join(", ", method.TypeParameters.Select(static parameter => parameter.Name)) + ">";
        var parameters = string.Join(", ", method.Parameters.Select(parameter => parameter.Type.ToDisplayString(TypeFormat) + " " + Identifier(parameter.Name)));

        return "/// <summary>\n"
               + "/// Answered by the SQL function <c>" + function + "</c>, "
               + (isSet ? "a set a row access rule asks with <c>Contains</c>,\n/// once per statement" : "one value a row access rule compares,\n/// once per statement when no argument reads the row")
               + ". Only the database can answer it, so called in C# it throws.\n"
               + "/// </summary>\n"
               + "/// <exception cref=\"" + DatabaseOnlyException + "\">Always: the question is the database's.</exception>\n"
               + Accessibility(method) + " static partial " + method.ReturnType.ToDisplayString(TypeFormat) + " " + Identifier(method.Name) + typeParameters + "(" + parameters + ")"
               + ConstraintsOf(method)
               + " => throw new " + DatabaseOnlyException + "(" + SymbolDisplay.FormatLiteral(function, quote: true) + ");";
    }

    /// <summary>The <c>where</c> clauses of a generic method, which the implementing part of a partial method repeats.</summary>
    private static string ConstraintsOf(IMethodSymbol method)
    {
        var clauses = new StringBuilder();
        foreach (var parameter in method.TypeParameters)
        {
            var constraints = new List<string>();
            if (parameter.HasReferenceTypeConstraint)
            {
                constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }
            else if (parameter.HasUnmanagedTypeConstraint)
            {
                constraints.Add("unmanaged");
            }
            else if (parameter.HasValueTypeConstraint)
            {
                constraints.Add("struct");
            }
            else if (parameter.HasNotNullConstraint)
            {
                constraints.Add("notnull");
            }

            constraints.AddRange(parameter.ConstraintTypes.Select(constraint => constraint.ToDisplayString(TypeFormat)));

            if (parameter.HasConstructorConstraint && !parameter.HasValueTypeConstraint)
            {
                constraints.Add("new()");
            }

            if (constraints.Count > 0)
            {
                clauses.Append(" where ").Append(parameter.Name).Append(" : ").Append(string.Join(", ", constraints));
            }
        }

        return clauses.ToString();
    }

    /// <summary>
    /// An <c>[AccessSet]</c> or <c>[AccessScalar]</c> method outside an <c>[AccessFunctions]</c> class, which
    /// nothing writes a body for: DDD00038 on it.
    /// </summary>
    private static EquatableArray<DiagnosticInfo> Outside(GeneratorAttributeSyntaxContext attributed)
    {
        var method = (IMethodSymbol)attributed.TargetSymbol;
        return AttributeOf(method.ContainingType, KnownTypes.AccessFunctionsAttribute) is not null
            ? EquatableArray<DiagnosticInfo>.Empty
            : new EquatableArray<DiagnosticInfo>([DiagnosticInfo.Create(
                DiagnosticDescriptors.RowAccessRuleShape,
                LocationInfo.From(method),
                method.Name,
                "to be declared in a static partial class marked [AccessFunctions], whose questions the generator writes the bodies of")]);
    }

    /// <summary>
    /// The key an access function about <paramref name="aggregate"/> is asked with, as a type and a parameter
    /// name, or null for an aggregate keyed on more than its id, whose function takes every key part.
    /// </summary>
    private static (string Type, string Parameter)? KeyOf(INamedTypeSymbol aggregate)
    {
        // A template class's key parts may come from its parent, which this compilation cannot show as its base.
        if (DefinitionFactory.HasKeyParts(aggregate) || (EntityDeclarations.TemplateParentOf(aggregate) is { } parent && DefinitionFactory.HasKeyParts(parent)))
        {
            return null;
        }

        if (EntityDeclarations.IdArgumentOf(aggregate) is not { } argument)
        {
            return null;
        }

        // [AggregateRoot<Guid>] names the raw value, and the generator writes the id beside the aggregate.
        var isIdentifier = argument.GetAttributes().Any(static attribute => attribute.AttributeClass?.OriginalDefinition.MetadataName == "EntityIdAttribute`1")
            || argument.AllInterfaces.Any(static contract => contract.ToDisplayString() == KnownTypes.EntityIdInterface);
        var type = isIdentifier
            ? argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : "global::" + (aggregate.ContainingNamespace.IsGlobalNamespace ? "" : aggregate.ContainingNamespace.ToDisplayString() + ".") + Identifiers.IdNameFor(aggregate.Name);

        return (type, Camel(Identifiers.IdNameFor(aggregate.Name)));
    }

    /// <summary>What an access function answers, as the attribute's <c>AccessFunctionShape</c> numbers it.</summary>
    private enum Shape
    {
        Row = 0,
        Set = 1,
    }

    /// <summary>
    /// A function's name as written, and as rules ask it: <c>schema.name</c> or <c>owner/name</c>. No logical
    /// name for a relative one without an owner; <see cref="IsInvalid"/> for one that is no name at all.
    /// </summary>
    private readonly record struct FunctionName(string Written, string? Logical, string? Owner, bool Qualified, bool IsInvalid = false)
    {
        public static FunctionName Invalid(string written) => new(written, null, null, Qualified: false, IsInvalid: true);
    }

    /// <summary>C# in, SQL out; anything it does not know is DDD00039 on that expression.</summary>
    private sealed class Translator(
        SemanticModel model,
        IParameterSymbol aggregate,
        IParameterSymbol caller,
        IReadOnlyList<IParameterSymbol> extras,
        bool isFunction,
        List<DiagnosticInfo> diagnostics)
    {
        /// <summary>The entity an <c>Any</c> is looking at, by the lambda parameter that names it: its alias in the SQL.</summary>
        private readonly Dictionary<ISymbol, string> _aliases = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// The lambda parameters whose type the compiler cannot give, as for the entities of a collection a
        /// template class inherits: what <c>.Value</c> on one of their members means cannot be known, so a rule
        /// that asks it is refused rather than translated by a guess.
        /// </summary>
        private readonly HashSet<ISymbol> _unbound = new(SymbolEqualityComparer.Default);

        public string Translate(ExpressionSyntax expression) => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => Translate(parenthesized.Expression),
            BinaryExpressionSyntax binary => Binary(binary),
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not => "(NOT " + Translate(not.Operand) + ")",
            LiteralExpressionSyntax literal => Literal(literal),
            InvocationExpressionSyntax invocation => Invocation(invocation),
            _ when IsNow(expression) => "now()",
            _ when ColumnOf(expression) is { } column => "{col:" + column + "}",
            _ when CallerOf(expression) is { } known => known,
            _ when ArgumentOf(expression) is { } argument => argument,
            _ when Unwrapped(expression) is InvocationExpressionSyntax wrapped && !ReferenceEquals(wrapped, expression) && ScalarOf(wrapped) is not null => Invocation(wrapped),
            _ when ConstantOf(expression) is { } constant => constant,
            _ => Fail(expression),
        };

        private string Binary(BinaryExpressionSyntax binary)
        {
            switch (binary.Kind())
            {
                case SyntaxKind.LogicalAndExpression:
                    return "(" + Translate(binary.Left) + " AND " + Translate(binary.Right) + ")";
                case SyntaxKind.LogicalOrExpression:
                    return "(" + Translate(binary.Left) + " OR " + Translate(binary.Right) + ")";
                case SyntaxKind.EqualsExpression:
                case SyntaxKind.NotEqualsExpression:
                    var equals = binary.IsKind(SyntaxKind.EqualsExpression);
                    if (IsNull(binary.Left) || IsNull(binary.Right))
                    {
                        return "(" + Translate(IsNull(binary.Right) ? binary.Left : binary.Right) + " IS " + (equals ? "" : "NOT ") + "NULL)";
                    }

                    var left = Operand(binary.Left, binary.Right);
                    var right = Operand(binary.Right, binary.Left);

                    // A scalar question answers null for a caller it does not know. SQL's own comparison is
                    // then null too, which a policy counts as no, and which stays null under NOT, AND with
                    // anything but false and OR with anything but true: such a caller passes neither == nor
                    // !=, nor either one negated. There is no C# answer to keep: in C# the question throws.
                    if (AsksAScalar(binary.Left) || AsksAScalar(binary.Right))
                    {
                        return "(" + left + (equals ? " = " : " <> ") + right + ")";
                    }

                    // Values that cannot be null compare as SQL does, which an index can answer.
                    if (CannotBeNull(binary.Left) && CannotBeNull(binary.Right))
                    {
                        return "(" + left + (equals ? " = " : " <> ") + right + ")";
                    }

                    // C#'s equality, in which a null equals a null and differs from everything else.
                    return "(" + left + (equals ? " IS NOT DISTINCT FROM " : " IS DISTINCT FROM ") + right + ")";
                case SyntaxKind.LessThanExpression:
                case SyntaxKind.LessThanOrEqualExpression:
                case SyntaxKind.GreaterThanExpression:
                case SyntaxKind.GreaterThanOrEqualExpression:
                    var compared = Operand(binary.Left, binary.Right) + " " + binary.OperatorToken.Text + " " + Operand(binary.Right, binary.Left);

                    // A lifted comparison with a null is false in C#; in SQL it is unknown until coalesced. With
                    // a scalar question it stays unknown, as == does, so NOT cannot turn it into a yes.
                    return AsksAScalar(binary.Left) || AsksAScalar(binary.Right) ? "(" + compared + ")" : "coalesce(" + compared + ", FALSE)";
                default:
                    return Fail(binary);
            }
        }

        /// <summary>
        /// Whether <paramref name="expression"/> asks an <c>[AccessScalar]</c> question, itself or anywhere
        /// inside it, whose answer is null for a caller the question does not know.
        /// </summary>
        private bool AsksAScalar(ExpressionSyntax expression)
            => expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(invocation => ScalarOf(invocation) is not null);

        /// <summary>
        /// Whether the C# type of <paramref name="expression"/> is a value type that is not nullable, so its
        /// SQL is never null either and <c>=</c> means what <c>==</c> means.
        /// </summary>
        private bool CannotBeNull(ExpressionSyntax expression)
            => TypeOf(expression) is { IsValueType: true, TypeKind: not TypeKind.Error } type
               && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

        /// <summary>
        /// One side of a comparison. An enum constant compared with a column is written as the column
        /// stores it, which only the export knows: a number by default, text with a converter.
        /// </summary>
        private string Operand(ExpressionSyntax side, ExpressionSyntax other)
            => EnumValueOf(side) is { } number && ColumnOf(other) is { } column
                ? "{val:" + column + ":" + number + "}"
                : Translate(side);

        private string Invocation(InvocationExpressionSyntax invocation)
        {
            var called = MethodOf(invocation);
            if (called is { ContainingType: { } sql } method
                && sql.ToDisplayString() == KnownTypes.SqlEscape)
            {
                return method.Name == "Call" ? SqlCall(invocation) : SqlRaw(invocation);
            }

            if (invocation.Expression is MemberAccessExpressionSyntax any
                && ((called is { Name: "Any", ContainingType: { } linq } && linq.ToDisplayString() == "System.Linq.Enumerable")
                    || (called is null && any.Name.Identifier.ValueText == "Any" && IsInheritedCollection(any.Expression))))
            {
                return Any(invocation, any.Expression);
            }

            if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Contains" } contains
                && Unwrapped(contains.Expression) is InvocationExpressionSyntax receiver
                && SetOf(receiver) is { } set)
            {
                return invocation.ArgumentList.Arguments.Count == 1
                    ? "(" + Translate(invocation.ArgumentList.Arguments[0].Expression) + " = ANY (ARRAY(SELECT " + Asked(set, receiver.ArgumentList.Arguments, [], perRow: false) + ")))"
                    : Fail(invocation);
            }

            if (SetOf(invocation) is not null)
            {
                // A set is asked whether it holds a value, and nothing else.
                return Fail(invocation);
            }

            if (ScalarOf(invocation) is { } scalar)
            {
                var arguments = invocation.ArgumentList.Arguments.Select(argument => Translate(argument.Expression)).ToList();
                var call = Call(scalar, arguments);
                return arguments.Any(ReadsTheRow) ? call : "(SELECT " + call + ")";
            }

            if (ByKey(invocation, called) is { } asked)
            {
                return asked;
            }

            if (called is { Name: "Allows", IsStatic: true } allows && AccessFunctionOf(allows.ContainingType) is { } function)
            {
                return AccessFunctionCall(invocation, allows.ContainingType, function);
            }

            if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Claim" } claim
                && IsParameter(claim.Expression, caller)
                && invocation.ArgumentList.Arguments.Count == 1
                && invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } path
                && path.Token.ValueText.Length > 0
                && path.Token.ValueText.IndexOfAny(['{', '}', ':']) < 0)
            {
                return "{caller:claim:" + path.Token.ValueText + "}";
            }

            return Fail(invocation);
        }

        /// <summary>The method an invocation calls, or its one candidate when the call does not bind yet.</summary>
        private IMethodSymbol? MethodOf(InvocationExpressionSyntax invocation)
        {
            var info = model.GetSymbolInfo(invocation);
            return info.Symbol as IMethodSymbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] as IMethodSymbol : null);
        }

        /// <summary>
        /// The name a set-shaped question is asked by, or null when the invocation is none: an
        /// <c>[AccessSet]</c> method of an <c>[AccessFunctions]</c> class, or the <c>Ids</c> of a set-shaped
        /// access function or its contract. <c>Ids</c> may be one this generator is writing in this very
        /// compilation, which the compiler cannot place yet, so the class is enough.
        /// </summary>
        private Question? SetOf(InvocationExpressionSyntax invocation)
        {
            if (MethodOf(invocation) is { } method && AttributeOf(method.OriginalDefinition, KnownTypes.AccessSetAttribute) is { } set)
            {
                return QuestionOf(method, set, invocation);
            }

            if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Ids" } access)
            {
                return null;
            }

            var owner = MethodOf(invocation)?.ContainingType ?? model.GetSymbolInfo(access.Expression).Symbol as INamedTypeSymbol;
            if (owner is null || (AccessFunctionOf(owner) ?? AccessFunctionContractOf(owner)) is not { } declared)
            {
                return null;
            }

            return NameOf(FunctionNameOf(declared), OwnerOf(owner)).Logical is { } logical
                ? new Question(logical, access.Expression + ".Ids", Plain: false)
                : null;
        }

        /// <summary>The name an <c>[AccessScalar]</c> question is asked by, or null when the invocation is none.</summary>
        private Question? ScalarOf(ExpressionSyntax? expression)
            => expression is InvocationExpressionSyntax invocation
               && MethodOf(invocation) is { } method
               && AttributeOf(method.OriginalDefinition, KnownTypes.AccessScalarAttribute) is { } scalar
                ? QuestionOf(method, scalar, invocation)
                : null;

        /// <summary>
        /// A question of an <c>[AccessFunctions]</c> class. One named with its schema is a function the host
        /// creates itself, called as it is, like <c>Sql.Call</c>; one named relative to its owner is asked by its
        /// logical name, which the export resolves to the function an access function or a contribution writes.
        /// </summary>
        private static Question? QuestionOf(IMethodSymbol method, AttributeData attribute, InvocationExpressionSyntax invocation)
        {
            var name = NameOf(FunctionNameOf(attribute), OwnerOf(method.ContainingType));
            return name.Logical is { } logical
                ? new Question(logical, invocation.Expression.ToString(), Plain: name.Qualified)
                : null;
        }

        /// <summary><c>{fn:owner/name}(arguments)</c>, or <c>schema.name(arguments)</c> for a function of the host's own.</summary>
        private static string Call(Question question, IEnumerable<string> arguments)
            => (question.Plain ? question.Name : "{fn:" + question.Name + "}") + "(" + string.Join(", ", arguments) + ")";

        /// <summary>
        /// A set-shaped question's call, its arguments translated. An argument that reads the row would make the
        /// database ask it once per row, which is DDD00051 on that argument.
        /// </summary>
        private string Asked(Question question, IEnumerable<ArgumentSyntax> arguments, IReadOnlyList<string> translated, bool perRow)
        {
            var all = new List<string>(translated);
            foreach (var argument in arguments)
            {
                var sql = Translate(argument.Expression);
                if (!perRow && ReadsTheRow(sql))
                {
                    diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.SetQuestionArgumentReadsTheRow, LocationInfo.From(argument), question.Display));
                }

                all.Add(sql);
            }

            return Call(question, all);
        }

        /// <summary>Whether translated SQL reads the row: a column of it, its entities, or a question asked about it.</summary>
        private static bool ReadsTheRow(string sql)
        {
            for (var i = 0; i < sql.Length; i++)
            {
                if (sql[i] is '{' or '}' && i + 1 < sql.Length && sql[i + 1] == sql[i])
                {
                    i++;
                    continue;
                }

                if (sql[i] != '{')
                {
                    continue;
                }

                var end = sql.IndexOf('}', i);
                if (end < 0)
                {
                    return false;
                }

                var token = sql.Substring(i + 1, end - i - 1);
                if (token.StartsWith("col:", System.StringComparison.Ordinal)
                    || token.StartsWith("exists:", System.StringComparison.Ordinal)
                    || token.StartsWith("call:", System.StringComparison.Ordinal)
                    || token == "key")
                {
                    return true;
                }

                i = end;
            }

            return false;
        }

        /// <summary>
        /// <c>project.Members.Any(member =&gt; ...)</c>: whether one of the aggregate's entities is so, as
        /// <c>{exists:Members:e1}...{/exists}</c>, which the export writes as an <c>EXISTS</c> on the entities'
        /// table. Only in an <c>[AccessFunction]</c>: a policy on the aggregate's table that read the tables
        /// its entities' policies read back would ask itself (DDD00041).
        /// </summary>
        /// <remarks>
        /// Inside such an <c>Any</c>, the entities of the entity it is looking at are asked the same way:
        /// <c>member.Duties.Any(duty =&gt; ...)</c> is <c>{exists:e1:Duties:e2}...{/exists}</c>, which names the
        /// alias whose collection it is. An alias is the depth of its <c>Any</c>, so one inside another never
        /// shares one. The aggregate's own collections are not asked from inside an <c>Any</c>: that question
        /// is about the row, not about the entity, and belongs next to the outer <c>Any</c>.
        /// </remarks>
        private string Any(InvocationExpressionSyntax invocation, ExpressionSyntax collection)
        {
            if (ColumnOf(collection) is not { } navigation)
            {
                return Fail(invocation);
            }

            // "Members" for the aggregate's own entities; "e1:Duties" for those of the entity an Any is looking at.
            string? owner = null;
            if (navigation.IndexOf(':') is var colon and >= 0)
            {
                owner = navigation.Substring(0, colon);
                navigation = navigation.Substring(colon + 1);
            }

            if (navigation.IndexOf('.') >= 0 || (owner is null && _aliases.Count > 0))
            {
                // A collection behind a value object, or the aggregate's own asked from inside an Any.
                return Fail(invocation);
            }

            if (!isFunction)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleReadsEntities, LocationInfo.From(invocation), invocation.ToString(), aggregate.Type.Name));
                return "?";
            }

            var alias = "e" + (_aliases.Count + 1).ToString(CultureInfo.InvariantCulture);
            var opening = "{exists:" + (owner is null ? "" : owner + ":") + navigation + ":" + alias + "}";
            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count == 0)
            {
                return opening + "TRUE{/exists}";
            }

            if (arguments.Count != 1
                || arguments[0].Expression is not LambdaExpressionSyntax { ExpressionBody: { } body } lambda
                || LambdaParameterOf(lambda) is not { } entity)
            {
                return Fail(arguments.Count == 1 ? arguments[0].Expression : invocation);
            }

            _aliases[entity] = alias;
            var unbound = entity.Type is null or { TypeKind: TypeKind.Error } && _unbound.Add(entity);
            try
            {
                return opening + Translate(body) + "{/exists}";
            }
            finally
            {
                _aliases.Remove(entity);
                if (unbound)
                {
                    _unbound.Remove(entity);
                }
            }
        }

        /// <summary>
        /// <c>ProjectMembership.Allows(project, caller)</c>, the question of an <c>[AccessFunction]</c> on the
        /// same aggregate, asked of this row: <c>{call:projects.is_member}</c>, which the export writes as the
        /// function called with the row's key; <c>{fn:projects.is_member}({key}, ...)</c> when the rule passes
        /// more; and, for a set-shaped function, whether the row's key is in its set.
        /// </summary>
        private string AccessFunctionCall(InvocationExpressionSyntax invocation, INamedTypeSymbol declaring, AttributeData function)
        {
            var arguments = invocation.ArgumentList.Arguments;
            if (_aliases.Count > 0
                || NameOf(FunctionNameOf(function), OwnerOf(declaring)).Logical is not { } name
                || function.AttributeClass?.TypeArguments.FirstOrDefault() is not { } about
                || !SymbolEqualityComparer.Default.Equals(about.OriginalDefinition, aggregate.Type.OriginalDefinition)
                || arguments.Count < 2
                || !IsParameter(arguments[0].Expression, aggregate)
                || !IsParameter(arguments[1].Expression, caller))
            {
                return Fail(invocation);
            }

            var more = arguments.Skip(2).ToList();
            var question = new Question(name, invocation.Expression.ToString(), Plain: false);
            if (ShapeOf(function) == Shape.Set)
            {
                return "({key} = ANY (ARRAY(SELECT " + Asked(question, more, [], perRow: false) + ")))";
            }

            return more.Count == 0 ? "{call:" + name + "}" : Asked(question, more, ["{key}"], perRow: true);
        }

        /// <summary>
        /// <c>ProjectMembership.Allows(task.ProjectId)</c>: an access function asked by key, from its definition or
        /// from its contract, as <c>{fn:projects.is_member}(...)</c>, with anything the rule passes after the key.
        /// The method may be one this generator is writing in this very compilation, which the compiler cannot
        /// place yet, so the class is enough. Null when the call is not one.
        /// </summary>
        private string? ByKey(InvocationExpressionSyntax invocation, IMethodSymbol? called)
        {
            var arguments = invocation.ArgumentList.Arguments;
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Allows" } access
                || arguments.Count == 0
                || called is { IsStatic: false }
                || IsParameter(arguments[0].Expression, aggregate))
            {
                return null;
            }

            var owner = called?.ContainingType ?? model.GetSymbolInfo(access.Expression).Symbol as INamedTypeSymbol;
            if (owner is null || (AccessFunctionOf(owner) ?? AccessFunctionContractOf(owner)) is not { } declared)
            {
                return null;
            }

            return NameOf(FunctionNameOf(declared), OwnerOf(owner)).Logical is { } name
                ? "{fn:" + name + "}(" + string.Join(", ", arguments.Select(argument => Translate(argument.Expression))) + ")"
                : Fail(invocation);
        }

        /// <summary><c>Sql.Call&lt;T&gt;("schema.name", ...)</c>: the function, with its arguments translated like the rest of the rule.</summary>
        private string SqlCall(InvocationExpressionSyntax invocation)
        {
            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count == 0 || StringConstant(arguments[0].Expression) is not { } function || !IsFunctionName(function))
            {
                return Fail(arguments.Count == 0 ? invocation : arguments[0].Expression);
            }

            var translated = new List<string>();
            for (var i = 1; i < arguments.Count; i++)
            {
                translated.Add(Translate(arguments[i].Expression));
            }

            return function + "(" + string.Join(", ", translated) + ")";
        }

        /// <summary><c>Sql.Raw&lt;T&gt;("...")</c>: the SQL as it is, in parentheses, its braces doubled so nothing is filled into them.</summary>
        private string SqlRaw(InvocationExpressionSyntax invocation)
        {
            var arguments = invocation.ArgumentList.Arguments;
            return arguments.Count == 1 && StringConstant(arguments[0].Expression) is { Length: > 0 } sql
                ? "(" + sql.Replace("{", "{{").Replace("}", "}}") + ")"
                : Fail(arguments.Count == 1 ? arguments[0].Expression : invocation);
        }

        /// <summary>A string the rule states: a literal, or a constant.</summary>
        private string? StringConstant(ExpressionSyntax expression)
            => model.GetConstantValue(expression) is { HasValue: true, Value: string text } ? text : null;

        private static bool IsFunctionName(string name)
            => name.Length > 0
                && name.Split('.').Length <= 2
                && name.Split('.').All(static part => part.Length > 0 && (char.IsLetter(part[0]) || part[0] == '_') && part.All(static c => char.IsLetterOrDigit(c) || c == '_'));

        /// <summary><c>DateTimeOffset.UtcNow</c> and <c>DateTime.UtcNow</c>: the database's <c>now()</c>.</summary>
        private bool IsNow(ExpressionSyntax expression)
            => model.GetSymbolInfo(expression).Symbol is IPropertySymbol { Name: "UtcNow", IsStatic: true } now
               && now.ContainingType.ToDisplayString() is "System.DateTimeOffset" or "System.DateTime";

        private string? CallerOf(ExpressionSyntax expression)
        {
            if (expression is not MemberAccessExpressionSyntax member || !IsParameter(member.Expression, caller))
            {
                return null;
            }

            return member.Name.Identifier.ValueText switch
            {
                "UserId" => "{caller:uid}",
                "IsSignedIn" => "{caller:signedin}",
                "Role" => "{caller:role}",
                _ => null,
            };
        }

        /// <summary>
        /// A parameter of the access function after the caller, <c>{arg:1}</c> for the first: the SQL function's
        /// own parameter, after the key. Its <c>.Value</c>, when it is an id, is the parameter too.
        /// </summary>
        private string? ArgumentOf(ExpressionSyntax expression)
        {
            if (extras.Count == 0 || Unwrapped(expression) is not IdentifierNameSyntax identifier || model.GetSymbolInfo(identifier).Symbol is not IParameterSymbol parameter)
            {
                return null;
            }

            for (var i = 0; i < extras.Count; i++)
            {
                if (SymbolEqualityComparer.Default.Equals(extras[i], parameter))
                {
                    return "{arg:" + (i + 1).ToString(CultureInfo.InvariantCulture) + "}";
                }
            }

            return null;
        }

        /// <summary>
        /// <paramref name="expression"/> without what does not change its SQL: parentheses, <c>!</c>, and the
        /// <c>.Value</c> of an id or a nullable, whose value is the thing itself.
        /// </summary>
        private ExpressionSyntax Unwrapped(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        continue;
                    case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed:
                        expression = suppressed.Operand;
                        continue;
                    case MemberAccessExpressionSyntax member when IsWrappedValue(member):
                        expression = member.Expression;
                        continue;
                    case ConditionalAccessExpressionSyntax { WhenNotNull: MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Value" } } conditional:
                        expression = conditional.Expression;
                        continue;
                }

                return expression;
            }
        }

        /// <summary>
        /// The property path an expression reads from the aggregate: <c>order.Status</c> is <c>Status</c>,
        /// <c>payment.Amount.Amount</c> is <c>Amount.Amount</c>, and the <c>Value</c> of a typed id or a
        /// single value object, <c>order.PlacedBy?.Value</c>, is the property itself, because it is
        /// stored in the same column.
        /// </summary>
        private string? ColumnOf(ExpressionSyntax expression)
        {
            var path = new List<string>();
            var current = expression;

            while (true)
            {
                switch (current)
                {
                    case MemberAccessExpressionSyntax member:
                        if (!IsWrappedValue(member))
                        {
                            path.Insert(0, member.Name.Identifier.ValueText);
                        }
                        else if (_unbound.Count > 0 && TypeOf(member.Expression) is null && IsUnboundElement(member.Expression))
                        {
                            return null;
                        }

                        current = member.Expression;
                        continue;
                    case ConditionalAccessExpressionSyntax { WhenNotNull: MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Value" } } conditional:
                        current = conditional.Expression;
                        continue;
                    case ParenthesizedExpressionSyntax parenthesized:
                        current = parenthesized.Expression;
                        continue;
                    case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed:
                        current = suppressed.Operand;
                        continue;
                }

                break;
            }

            if (path.Count == 0)
            {
                return null;
            }

            if (IsParameter(current, aggregate))
            {
                return string.Join(".", path);
            }

            // A property of the entity an Any is looking at: its alias, then its path.
            return current is IdentifierNameSyntax && model.GetSymbolInfo(current).Symbol is { } entity && _aliases.TryGetValue(entity, out var alias)
                ? alias + ":" + string.Join(".", path)
                : null;
        }

        /// <summary>
        /// <c>.Value</c> on something whose value lives in the same column: a nullable, a typed id or a
        /// single value object. The type may be one another generator writes, which this one cannot see,
        /// so a <c>.Value</c> the compiler cannot place yet counts as well.
        /// </summary>
        private bool IsWrappedValue(MemberAccessExpressionSyntax member)
        {
            if (member.Name.Identifier.ValueText != "Value")
            {
                return false;
            }

            // A type parameter is what a parent's id is, TTenantId, and an id keeps its value in its column.
            var receiver = TypeOf(member.Expression);
            return receiver is null or ITypeParameterSymbol
                || receiver.TypeKind == TypeKind.Error
                || receiver.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                || receiver.GetAttributes().Any(static attribute => attribute.AttributeClass?.OriginalDefinition.MetadataName is "EntityIdAttribute`1" or "SingleValueObjectAttribute`1")
                || receiver.AllInterfaces.Any(static contract => contract.ToDisplayString() == "DDDToolkit.Abstractions.Interfaces.IEntityId");
        }

        /// <summary>Whether an expression reads a member of an entity whose type the compiler cannot give.</summary>
        private bool IsUnboundElement(ExpressionSyntax expression)
        {
            while (expression is MemberAccessExpressionSyntax access)
            {
                expression = access.Expression;
            }

            return expression is IdentifierNameSyntax && model.GetSymbolInfo(expression).Symbol is { } symbol && _unbound.Contains(symbol);
        }

        /// <summary>
        /// The one parameter of an <c>Any</c>'s lambda. Bound through the lambda when the call binds, and
        /// through its declaration when it does not, as for a collection a template class inherits.
        /// </summary>
        private IParameterSymbol? LambdaParameterOf(LambdaExpressionSyntax lambda)
        {
            if (model.GetSymbolInfo(lambda).Symbol is IMethodSymbol { Parameters.Length: 1 } signature)
            {
                return signature.Parameters[0];
            }

            var declared = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => simple.Parameter,
                ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
                _ => null,
            };

            return declared is null ? null : model.GetDeclaredSymbol(declared);
        }

        /// <summary>
        /// Whether an expression the compiler cannot bind is a collection a template class inherits from its
        /// parent, <c>organization.Units</c>. Its base class is written by a generator, so in this compilation
        /// the member is not there, and <c>Any</c> over it does not bind either.
        /// </summary>
        private bool IsInheritedCollection(ExpressionSyntax expression)
            => model.GetTypeInfo(expression).Type is null or { TypeKind: TypeKind.Error }
               && TypeOf(expression) is INamedTypeSymbol collection
               && (collection.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                   || collection.AllInterfaces.Any(static contract => contract.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T));

        /// <summary>
        /// The type of an expression, reading a member a template class inherits from its parent where the
        /// compiler cannot: the class has no base class yet in this compilation, because a generator writes it.
        /// </summary>
        private ITypeSymbol? TypeOf(ExpressionSyntax expression)
        {
            if (model.GetTypeInfo(expression).Type is { TypeKind: not TypeKind.Error } bound)
            {
                return bound;
            }

            if (expression is not MemberAccessExpressionSyntax access
                || TypeOf(access.Expression) is not INamedTypeSymbol owner)
            {
                return null;
            }

            // The id an aggregate or an entity inherits from the base class the generator writes for it.
            if (access.Name.Identifier.ValueText == "Id" && EntityDeclarations.IdArgumentOf(owner) is { TypeKind: not TypeKind.Error } id)
            {
                return id;
            }

            if (EntityDeclarations.TemplateParentOf(owner) is not { } parent)
            {
                return null;
            }

            for (var type = (INamedTypeSymbol?)parent; type is not null; type = type.BaseType)
            {
                foreach (var member in type.GetMembers(access.Name.Identifier.ValueText))
                {
                    switch (member)
                    {
                        case IPropertySymbol property:
                            return property.Type;
                        case IFieldSymbol field:
                            return field.Type;
                    }
                }
            }

            return null;
        }

        private string? EnumValueOf(ExpressionSyntax expression)
            => model.GetSymbolInfo(expression).Symbol is IFieldSymbol { HasConstantValue: true, ContainingType.TypeKind: TypeKind.Enum } field
                ? System.Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture)
                : null;

        private string? ConstantOf(ExpressionSyntax expression)
            => model.GetSymbolInfo(expression).Symbol is IFieldSymbol { HasConstantValue: true } field ? Constant(field.ConstantValue) : null;

        private string Literal(LiteralExpressionSyntax literal) => literal.Kind() switch
        {
            SyntaxKind.TrueLiteralExpression => "TRUE",
            SyntaxKind.FalseLiteralExpression => "FALSE",
            SyntaxKind.NullLiteralExpression => "NULL",
            SyntaxKind.StringLiteralExpression => Constant(literal.Token.ValueText),
            SyntaxKind.NumericLiteralExpression => Constant(literal.Token.Value),
            _ => Fail(literal),
        };

        /// <summary>A constant as SQL. Braces are doubled, because the template uses them for what it fills in.</summary>
        private static string Constant(object? value) => value switch
        {
            null => "NULL",
            string text => "'" + text.Replace("'", "''").Replace("{", "{{").Replace("}", "}}") + "'",
            bool flag => flag ? "TRUE" : "FALSE",
            char character => "'" + (character == '\'' ? "''" : character.ToString()) + "'",
            System.IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()!,
        };

        private bool IsParameter(ExpressionSyntax expression, IParameterSymbol parameter)
            => expression is IdentifierNameSyntax && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(expression).Symbol, parameter);

        private static bool IsNull(ExpressionSyntax expression) => expression.IsKind(SyntaxKind.NullLiteralExpression);

        private string Fail(SyntaxNode node)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleUntranslatable, LocationInfo.From(node), node.ToString()));
            return "?";
        }
    }

    /// <summary>
    /// A question only the database answers, as a rule asks it: the function's name, logical or with its
    /// schema; how the rule names it, for a diagnostic; and whether it is called as it is, a function of the
    /// host's own, rather than by a name the export resolves.
    /// </summary>
    private sealed record Question(string Name, string Display, bool Plain);
}

/// <summary>
/// A row access rule, an access function, an access function's contract or a class of database questions:
/// the type to add to, what to report, and the members to add to it, none when there are errors.
/// </summary>
internal sealed record RowAccessDefinition(
    TypeDeclarationInfo Type,
    EquatableArray<DiagnosticInfo> Diagnostics,
    EquatableArray<string> Members)
{
    /// <summary>Nothing to add and nothing to report: the attribute's type argument does not bind yet.</summary>
    public static RowAccessDefinition Nothing(TypeDeclarationInfo type)
        => new(type, EquatableArray<DiagnosticInfo>.Empty, EquatableArray<string>.Empty);

    /// <summary>Only the diagnostics: nothing is added to a class that has errors.</summary>
    public static RowAccessDefinition Failed(TypeDeclarationInfo type, List<DiagnosticInfo> diagnostics)
        => new(type, new EquatableArray<DiagnosticInfo>(diagnostics), EquatableArray<string>.Empty);
}

internal static class RowAccessDiagnostics
{
    public static bool HasErrorsIn(this List<DiagnosticInfo> diagnostics) => diagnostics.Any(diagnostic => diagnostic.IsError);

    /// <summary>The parameters as an immutable array, for a filter that may leave none.</summary>
    public static System.Collections.Immutable.ImmutableArray<IParameterSymbol> ToImmutableArrayOrEmpty(this IEnumerable<IParameterSymbol> parameters)
        => System.Collections.Immutable.ImmutableArray.CreateRange(parameters);
}
