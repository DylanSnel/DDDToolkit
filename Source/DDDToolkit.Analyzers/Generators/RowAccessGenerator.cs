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
/// properties inside it <c>{col:e1:Path}</c>. A rule asks the function about its own row with
/// <c>{call:schema.name}</c>, which the export writes with the row's key, and about a key it holds, from its
/// definition or its contract, with <c>{fn:schema.name}(...)</c>.
/// </para>
/// <para>
/// The translation keeps C#'s meaning rather than SQL's, so the method and the policy answer alike for the
/// same row and caller: <c>==</c> is <c>IS NOT DISTINCT FROM</c>, because in C# a null equals a null, and a
/// comparison is <c>coalesce(..., FALSE)</c>, because a lifted comparison with a null is false in C#, never
/// unknown. What cannot be translated is DDD00039, on that expression.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class RowAccessGenerator : IIncrementalGenerator
{
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

        context.RegisterSourceOutput(rules, static (production, rule) => Produce(production, rule, ".RowAccess"));
        context.RegisterSourceOutput(functions, static (production, function) => Produce(production, function, ".AccessFunction"));
        context.RegisterSourceOutput(contracts, static (production, contract) => Produce(production, contract, ".AccessFunctionContract"));
    }

    private static void Produce(SourceProductionContext production, RowAccessDefinition definition, string suffix)
    {
        definition.Diagnostics.ReportAll(production);
        if (definition.Sql is not null || definition.FunctionName is not null)
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

        var aggregate = attributed.Attributes[0].AttributeClass?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        if (aggregate is null || aggregate.TypeKind == TypeKind.Error)
        {
            return new RowAccessDefinition(type, null, EquatableArray<DiagnosticInfo>.Empty);
        }

        if (!IsAggregateRoot(aggregate))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleNotOnAggregateRoot, LocationInfo.From(syntax.Identifier), symbol.Name, aggregate.Name));
        }

        if (!symbol.IsStatic || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleShape, LocationInfo.From(syntax.Identifier), symbol.Name, "to be a static partial class, so the generator can add its SQL"));
        }

        if (isFunction && !IsQualifiedFunctionName(FunctionNameOf(attributed.Attributes[0])))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.RowAccessRuleShape,
                LocationInfo.From(syntax.Identifier),
                symbol.Name,
                "a function name with its schema, such as \"projects.is_member\": the function runs with an empty search_path, and is created in that schema"));
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
            || method.ReturnType.SpecialType != SpecialType.System_Boolean
            || method.Parameters.Length != 2
            || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type.OriginalDefinition, aggregate.OriginalDefinition)
            || method.Parameters[1].Type.ToDisplayString() != KnownTypes.Caller)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.RowAccessRuleShape,
                LocationInfo.From(declaration?.Identifier ?? syntax.Identifier),
                symbol.Name,
                $"one method 'public static bool Allows({aggregate.Name} {Camel(aggregate.Name)}, Caller caller)' whose body is a single expression"));
            return new RowAccessDefinition(type, null, new EquatableArray<DiagnosticInfo>(diagnostics));
        }

        var model = attributed.SemanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
        var sql = new Translator(model, method.Parameters[0], method.Parameters[1], isFunction, diagnostics).Translate(body);

        if (diagnostics.HasErrorsIn())
        {
            return new RowAccessDefinition(type, null, new EquatableArray<DiagnosticInfo>(diagnostics));
        }

        var key = isFunction ? KeyOf(aggregate) : null;
        return new RowAccessDefinition(
            type,
            sql,
            new EquatableArray<DiagnosticInfo>(diagnostics),
            isFunction ? FunctionNameOf(attributed.Attributes[0]) : null,
            key?.Type,
            key?.Parameter);
    }

    private static bool IsAggregateRoot(INamedTypeSymbol type)
        => type.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.OriginalDefinition is { MetadataName: "AggregateRootAttribute`1" } root
            && root.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace);

    /// <summary>The name an <c>[AccessFunction]</c> gives its function, <c>projects.is_member</c>, or null.</summary>
    private static string? FunctionNameOf(AttributeData attribute)
        => attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name ? name : null;

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

    /// <summary><c>schema.name</c>, both plain identifiers: what a function called with an empty search_path needs.</summary>
    private static bool IsQualifiedFunctionName(string? name)
        => name is not null
            && name.Split('.') is { Length: 2 } parts
            && parts.All(static part => part.Length > 0 && (char.IsLetter(part[0]) || part[0] == '_') && part.All(static c => char.IsLetterOrDigit(c) || c == '_'));

    private static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static string Emit(RowAccessDefinition definition)
    {
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(definition.Type))
        {
            using (writer.Block(definition.Type.PartialHeader))
            {
                if (definition.Sql is not null)
                {
                    writer.Line("/// <summary>");
                    writer.Line("/// <c>Allows</c> as SQL, translated when this compiled. <c>{col:Path}</c> is a property, whose column");
                    writer.Line("/// the export takes from the Entity Framework model, and <c>{caller:...}</c> is the caller, as the");
                    writer.Line("/// database knows them.");
                    writer.Line("/// </summary>");
                    writer.Line("public const string " + KnownTypes.RowAccessSqlField + " = " + SymbolDisplay.FormatLiteral(definition.Sql, quote: true) + ";");
                }

                if (definition.FunctionName is { } name)
                {
                    writer.Line();
                    writer.Line("/// <summary>The function's name in the database, with its schema.</summary>");
                    writer.Line("public const string Name = " + SymbolDisplay.FormatLiteral(name, quote: true) + ";");
                }

                if (definition is { FunctionName: { } function, KeyType: { } key, KeyParameter: { } parameter })
                {
                    writer.Line();
                    writer.Line("/// <summary>");
                    writer.Line("/// This function asked about the aggregate with <paramref name=\"" + parameter + "\"/>, from a row access rule of any");
                    writer.Line("/// aggregate that holds that key: <c>" + function + "(...)</c> in its policy. Only the database can answer it,");
                    writer.Line("/// so called in C# it throws.");
                    writer.Line("/// </summary>");
                    writer.Line("/// <exception cref=\"global::DDDToolkit.Abstractions.Access.DatabaseOnlyException\">Always: the question is the database's.</exception>");
                    writer.Line("public static bool Allows(" + key + " " + parameter + ") => throw new global::DDDToolkit.Abstractions.Access.DatabaseOnlyException(" + SymbolDisplay.FormatLiteral(function + "(" + parameter + ")", quote: true) + ");");
                }
            }
        }

        return writer.ToString();
    }

    /// <summary>
    /// An <c>[AccessFunctionContract&lt;TKey&gt;]</c>: the published side of an access function, whose
    /// <c>Name</c> and <c>Allows(TKey)</c> the generator writes so other modules' rules ask it typed.
    /// </summary>
    private static RowAccessDefinition Declare(GeneratorAttributeSyntaxContext attributed)
    {
        var symbol = (INamedTypeSymbol)attributed.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)attributed.TargetNode;
        var type = DefinitionFactory.CreateTypeInfo(symbol, syntax);
        var diagnostics = new List<DiagnosticInfo>();

        var key = attributed.Attributes[0].AttributeClass?.TypeArguments.FirstOrDefault();
        if (key is null || key.TypeKind == TypeKind.Error)
        {
            return new RowAccessDefinition(type, null, EquatableArray<DiagnosticInfo>.Empty);
        }

        if (!symbol.IsStatic || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleShape, LocationInfo.From(syntax.Identifier), symbol.Name, "to be a static partial class, so the generator can add its Allows method"));
        }

        var name = FunctionNameOf(attributed.Attributes[0]);
        if (!IsQualifiedFunctionName(name))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.RowAccessRuleShape,
                LocationInfo.From(syntax.Identifier),
                symbol.Name,
                "a function name with its schema, such as \"projects.is_member\": the function runs with an empty search_path, and is created in that schema"));
        }

        return diagnostics.HasErrorsIn()
            ? new RowAccessDefinition(type, null, new EquatableArray<DiagnosticInfo>(diagnostics))
            : new RowAccessDefinition(type, null, new EquatableArray<DiagnosticInfo>(diagnostics), name, key.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), Camel(key.Name));
    }

    /// <summary>
    /// The key an access function about <paramref name="aggregate"/> is asked with, as a type and a parameter
    /// name, or null for an aggregate keyed on more than its id, whose function takes every key part.
    /// </summary>
    private static (string Type, string Parameter)? KeyOf(INamedTypeSymbol aggregate)
    {
        if (aggregate.GetMembers().OfType<IPropertySymbol>().Any(static property => property.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == KnownTypes.KeyPartAttribute)))
        {
            return null;
        }

        var root = aggregate.GetAttributes().FirstOrDefault(static attribute =>
            attribute.AttributeClass?.OriginalDefinition is { MetadataName: "AggregateRootAttribute`1" } candidate
            && candidate.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace);
        if (root?.AttributeClass?.TypeArguments.FirstOrDefault() is not { } argument)
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

    /// <summary>C# in, SQL out; anything it does not know is DDD00039 on that expression.</summary>
    private sealed class Translator(SemanticModel model, IParameterSymbol aggregate, IParameterSymbol caller, bool isFunction, List<DiagnosticInfo> diagnostics)
    {
        /// <summary>The entity an <c>Any</c> is looking at, by the lambda parameter that names it: its alias in the SQL.</summary>
        private readonly Dictionary<ISymbol, string> _aliases = new(SymbolEqualityComparer.Default);

        public string Translate(ExpressionSyntax expression) => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => Translate(parenthesized.Expression),
            BinaryExpressionSyntax binary => Binary(binary),
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not => "(NOT " + Translate(not.Operand) + ")",
            LiteralExpressionSyntax literal => Literal(literal),
            InvocationExpressionSyntax invocation => Invocation(invocation),
            _ when ColumnOf(expression) is { } column => "{col:" + column + "}",
            _ when CallerOf(expression) is { } known => known,
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
                    var not = binary.IsKind(SyntaxKind.NotEqualsExpression) ? "NOT " : "";
                    if (IsNull(binary.Left) || IsNull(binary.Right))
                    {
                        return "(" + Translate(IsNull(binary.Right) ? binary.Left : binary.Right) + " IS " + not + "NULL)";
                    }

                    // C#'s equality, in which a null equals a null and differs from everything else.
                    var distinct = binary.IsKind(SyntaxKind.EqualsExpression) ? " IS NOT DISTINCT FROM " : " IS DISTINCT FROM ";
                    return "(" + Operand(binary.Left, binary.Right) + distinct + Operand(binary.Right, binary.Left) + ")";
                case SyntaxKind.LessThanExpression:
                case SyntaxKind.LessThanOrEqualExpression:
                case SyntaxKind.GreaterThanExpression:
                case SyntaxKind.GreaterThanOrEqualExpression:
                    // A lifted comparison with a null is false in C#; in SQL it is unknown until coalesced.
                    return "coalesce(" + Operand(binary.Left, binary.Right) + " " + binary.OperatorToken.Text + " " + Operand(binary.Right, binary.Left) + ", FALSE)";
                default:
                    return Fail(binary);
            }
        }

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
            var called = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (called is { ContainingType: { } sql } method
                && sql.ToDisplayString() == KnownTypes.SqlEscape)
            {
                return method.Name == "Call" ? SqlCall(invocation) : SqlRaw(invocation);
            }

            if (called is { Name: "Any", ContainingType: { } linq }
                && linq.ToDisplayString() == "System.Linq.Enumerable"
                && invocation.Expression is MemberAccessExpressionSyntax any)
            {
                return Any(invocation, any.Expression);
            }

            if (ByKey(invocation, called) is { } asked)
            {
                return asked;
            }

            if (called is { Name: "Allows", IsStatic: true } allows && AccessFunctionOf(allows.ContainingType) is { } function)
            {
                return AccessFunctionCall(invocation, function);
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

        /// <summary>
        /// <c>project.Members.Any(member =&gt; ...)</c>: whether one of the aggregate's entities is so, as
        /// <c>{exists:Members:e1}...{/exists}</c>, which the export writes as an <c>EXISTS</c> on the entities'
        /// table. Only in an <c>[AccessFunction]</c>: a policy on the aggregate's table that read the tables
        /// its entities' policies read back would ask itself (DDD00041).
        /// </summary>
        private string Any(InvocationExpressionSyntax invocation, ExpressionSyntax collection)
        {
            if (_aliases.Count > 0 || ColumnOf(collection) is not { } navigation || navigation.IndexOfAny(['.', ':']) >= 0)
            {
                // An entity of an entity, or a collection that is not the aggregate's own.
                return Fail(invocation);
            }

            if (!isFunction)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.RowAccessRuleReadsEntities, LocationInfo.From(invocation), invocation.ToString(), aggregate.Type.Name));
                return "?";
            }

            const string Alias = "e1";
            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count == 0)
            {
                return "{exists:" + navigation + ":" + Alias + "}TRUE{/exists}";
            }

            if (arguments.Count != 1
                || arguments[0].Expression is not LambdaExpressionSyntax { ExpressionBody: { } body } lambda
                || model.GetSymbolInfo(lambda).Symbol is not IMethodSymbol { Parameters.Length: 1 } signature)
            {
                return Fail(arguments.Count == 1 ? arguments[0].Expression : invocation);
            }

            _aliases[signature.Parameters[0]] = Alias;
            try
            {
                return "{exists:" + navigation + ":" + Alias + "}" + Translate(body) + "{/exists}";
            }
            finally
            {
                _aliases.Remove(signature.Parameters[0]);
            }
        }

        /// <summary>
        /// <c>ProjectMembership.Allows(project, caller)</c>, the question of an <c>[AccessFunction]</c> on the
        /// same aggregate, asked of this row: <c>{call:projects.is_member}</c>, which the export writes as the
        /// function called with the row's key.
        /// </summary>
        private string AccessFunctionCall(InvocationExpressionSyntax invocation, AttributeData function)
        {
            var arguments = invocation.ArgumentList.Arguments;
            return _aliases.Count == 0
                && FunctionNameOf(function) is { } name
                && IsQualifiedFunctionName(name)
                && function.AttributeClass?.TypeArguments.FirstOrDefault() is { } about
                && SymbolEqualityComparer.Default.Equals(about.OriginalDefinition, aggregate.Type.OriginalDefinition)
                && arguments.Count == 2
                && IsParameter(arguments[0].Expression, aggregate)
                && IsParameter(arguments[1].Expression, caller)
                    ? "{call:" + name + "}"
                    : Fail(invocation);
        }

        /// <summary>
        /// <c>ProjectMembership.Allows(task.ProjectId)</c>: an access function asked by key, from its definition or
        /// from its contract, as <c>{fn:projects.is_member}(...)</c>. The method may be one this generator is
        /// writing in this very compilation, which the compiler cannot place yet, so the class is enough.
        /// Null when the call is not one.
        /// </summary>
        private string? ByKey(InvocationExpressionSyntax invocation, IMethodSymbol? called)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Allows" } access
                || invocation.ArgumentList.Arguments.Count != 1
                || called is { IsStatic: false })
            {
                return null;
            }

            var owner = called?.ContainingType ?? model.GetSymbolInfo(access.Expression).Symbol as INamedTypeSymbol;
            if (owner is null || (AccessFunctionOf(owner) ?? AccessFunctionContractOf(owner)) is not { } declared)
            {
                return null;
            }

            return FunctionNameOf(declared) is { } name && IsQualifiedFunctionName(name)
                ? "{fn:" + name + "}(" + Translate(invocation.ArgumentList.Arguments[0].Expression) + ")"
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

            var receiver = model.GetTypeInfo(member.Expression).Type;
            return receiver is null
                || receiver.TypeKind == TypeKind.Error
                || receiver.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                || receiver.GetAttributes().Any(static attribute => attribute.AttributeClass?.OriginalDefinition.MetadataName is "EntityIdAttribute`1" or "SingleValueObjectAttribute`1")
                || receiver.AllInterfaces.Any(static contract => contract.ToDisplayString() == "DDDToolkit.Abstractions.Interfaces.IEntityId");
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
}

/// <summary>One row access rule: the type to add the SQL to, and the SQL, or null when it has errors.</summary>
/// <summary>
/// A row access rule, an access function or an access function's contract: the type to add to, the SQL
/// (null for a contract, or when there are errors), and for a function its name and the key it is asked with.
/// </summary>
internal sealed record RowAccessDefinition(
    TypeDeclarationInfo Type,
    string? Sql,
    EquatableArray<DiagnosticInfo> Diagnostics,
    string? FunctionName = null,
    string? KeyType = null,
    string? KeyParameter = null);

internal static class RowAccessDiagnostics
{
    public static bool HasErrorsIn(this List<DiagnosticInfo> diagnostics) => diagnostics.Any(diagnostic => diagnostic.IsError);
}
