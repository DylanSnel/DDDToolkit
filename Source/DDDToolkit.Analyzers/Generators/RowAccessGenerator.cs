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
            transform: static (syntaxContext, cancellationToken) => Translate(syntaxContext, cancellationToken));

        context.RegisterSourceOutput(rules, static (production, rule) =>
        {
            rule.Diagnostics.ReportAll(production);
            if (rule.Sql is not null)
            {
                production.AddSource(rule.Type.HintName(".RowAccess"), SourceText.From(Emit(rule), Encoding.UTF8));
            }
        });
    }

    private static RowAccessDefinition Translate(GeneratorAttributeSyntaxContext attributed, CancellationToken cancellationToken)
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
        var sql = new Translator(model, method.Parameters[0], method.Parameters[1], diagnostics).Translate(body);

        return new RowAccessDefinition(
            type,
            diagnostics.HasErrorsIn() ? null : sql,
            new EquatableArray<DiagnosticInfo>(diagnostics));
    }

    private static bool IsAggregateRoot(INamedTypeSymbol type)
        => type.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.OriginalDefinition is { MetadataName: "AggregateRootAttribute`1" } root
            && root.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace);

    private static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static string Emit(RowAccessDefinition rule)
    {
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(rule.Type))
        {
            using (writer.Block(rule.Type.PartialHeader))
            {
                writer.Line("/// <summary>");
                writer.Line("/// <c>Allows</c> as SQL, translated when this compiled. <c>{col:Path}</c> is a property, whose column");
                writer.Line("/// the export takes from the Entity Framework model, and <c>{caller:...}</c> is the caller, as the");
                writer.Line("/// database knows them.");
                writer.Line("/// </summary>");
                writer.Line("public const string " + KnownTypes.RowAccessSqlField + " = " + SymbolDisplay.FormatLiteral(rule.Sql!, quote: true) + ";");
            }
        }

        return writer.ToString();
    }

    /// <summary>C# in, SQL out; anything it does not know is DDD00039 on that expression.</summary>
    private sealed class Translator(SemanticModel model, IParameterSymbol aggregate, IParameterSymbol caller, List<DiagnosticInfo> diagnostics)
    {
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
            if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType: { } sql } method
                && sql.ToDisplayString() == KnownTypes.SqlEscape)
            {
                return method.Name == "Call" ? SqlCall(invocation) : SqlRaw(invocation);
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

            return path.Count > 0 && IsParameter(current, aggregate) ? string.Join(".", path) : null;
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
internal sealed record RowAccessDefinition(TypeDeclarationInfo Type, string? Sql, EquatableArray<DiagnosticInfo> Diagnostics);

internal static class RowAccessDiagnostics
{
    public static bool HasErrorsIn(this List<DiagnosticInfo> diagnostics) => diagnostics.Any(diagnostic => diagnostic.IsError);
}
