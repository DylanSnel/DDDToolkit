using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DTS.DDD.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DTS.DDD.Analyzers;

/// <summary>
/// Turns the nested <c>Validator</c> of a value object into code. The author writes the rules in the constructor:
/// <code>
/// partial class Validator
/// {
///     public Validator() =&gt; RuleFor(x =&gt; x.Value).NotEmpty().EmailAddress();
/// }
/// </code>
/// The generator gives <c>Validator</c> the base class <c>DTS.DDD.Validation.Rules&lt;T&gt;</c> and a static
/// <c>Check</c> method with one <c>if</c> statement per rule. The value object gets overrides of both
/// <c>Validate</c> methods that call <c>Check</c>. With no list of failures to fill, <c>Check</c> returns at the
/// first failure and allocates nothing.
/// <para>
/// The generator reads the rules from the syntax, because the base class and the <c>Value</c> property of a single
/// value object are generated and the semantic model of a generator does not see generated code. It copies each
/// lambda body and replaces the lambda parameters with the value object and the value.
/// </para>
/// <para>
/// A project that references <c>DTS.DDD.FluentValidation</c> keeps that package's generator, and this generator
/// writes nothing.
/// </para>
/// <list type="bullet">
///   <item><description>DDD00115: a call that is no rule.</description></item>
///   <item><description>DDD00116: a lambda without an expression body, or a <c>RuleFor</c> that reads no property.</description></item>
///   <item><description>DDD00117: <c>WithError</c> creates no <c>[ErrorCode]</c> record of <c>InvalidError</c>.</description></item>
///   <item><description>DDD00118: a statement in the constructor that is no rule.</description></item>
///   <item><description>DDD00119: a rule uses a local, a parameter or an instance member of the <c>Validator</c>.</description></item>
/// </list>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ValidatorGenerator : IIncrementalGenerator
{
    private const string FluentValidationBridge = "DTS.DDD.FluentValidation.ValueObjectRules";
    private const string Validation = "global::DTS.DDD.Validation";
    private const string Checks = Validation + ".RuleChecks";
    private const string Failures = Validation + ".RuleFailures";
    private const string Instance = "instance";
    private const string Errors = "errors";

    private static readonly HashSet<string> ValueObjectAttributes = new(StringComparer.Ordinal)
    {
        KnownTypes.ValueObjectAttribute,
        KnownTypes.SingleValueObjectAttribute,
        KnownTypes.EntityIdAttribute,
    };

    /// <summary>True when <paramref name="type"/> carries <c>[ValueObject]</c>, <c>[SingleValueObject&lt;T&gt;]</c> or <c>[EntityId&lt;T&gt;]</c>.</summary>
    private static bool IsValueObject(ITypeSymbol type)
        => type.GetAttributes().Any(static attribute => attribute.AttributeClass is { } attributeType
                                                        && ValueObjectAttributes.Contains(attributeType.ContainingNamespace.ToDisplayString() + "." + attributeType.MetadataName));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var usesFluentValidation = context.CompilationProvider
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName(FluentValidationBridge) is not null);

        var validators = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { Identifier.ValueText: "Validator", Parent: TypeDeclarationSyntax } declaration
                                               && declaration.Members.OfType<ConstructorDeclarationSyntax>().Any(static constructor => constructor.ParameterList.Parameters.Count == 0),
                transform: static (syntaxContext, cancellationToken) => Describe(syntaxContext, cancellationToken))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .Combine(usesFluentValidation);

        context.RegisterSourceOutput(validators, static (production, pair) =>
        {
            var (model, fluentValidation) = pair;
            if (fluentValidation)
            {
                return;
            }

            model.Diagnostics.ReportAll(production);
            if (!model.Diagnostics.HasErrors())
            {
                production.AddSource(model.HintName, SourceText.From(model.Source, Encoding.UTF8));
            }
        });
    }

    private static ValidatorModel? Describe(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var validator = (ClassDeclarationSyntax)context.Node;
        var containerSyntax = (TypeDeclarationSyntax)validator.Parent!;
        if (context.SemanticModel.GetDeclaredSymbol(containerSyntax, cancellationToken) is not INamedTypeSymbol container
            || !container.IsRecord
            || container.IsValueType
            || !IsValueObject(container))
        {
            return null;
        }

        var validatorSymbol = context.SemanticModel.GetDeclaredSymbol(validator, cancellationToken) as INamedTypeSymbol;
        var type = DefinitionFactory.CreateTypeInfo(container, containerSyntax);
        var reader = new RuleReader(context.SemanticModel, container, validatorSymbol, type, cancellationToken);

        var constructor = validator.Members.OfType<ConstructorDeclarationSyntax>().First(static c => c.ParameterList.Parameters.Count == 0);
        if (constructor.ExpressionBody is { } arrow)
        {
            reader.ReadStatement(arrow.Expression, conditions: []);
        }
        else if (constructor.Body is { } body)
        {
            foreach (var statement in body.Statements)
            {
                reader.ReadStatement(statement, conditions: []);
            }
        }

        reader.FoldNestedValueObjects();

        var source = Write(type, Usings(containerSyntax), reader);
        return new ValidatorModel(type.HintName(".Validator"), source, reader.Diagnostics.ToEquatableArray());
    }

    /// <summary>The using directives of the file, so that the copied lambda bodies resolve the same names.</summary>
    private static IEnumerable<string> Usings(SyntaxNode node)
    {
        var usings = new List<string>();
        foreach (var ancestor in node.Ancestors())
        {
            var directives = ancestor switch
            {
                CompilationUnitSyntax unit => unit.Usings,
                BaseNamespaceDeclarationSyntax space => space.Usings,
                _ => default,
            };

            foreach (var directive in directives)
            {
                if (directive.GlobalKeyword.IsKind(SyntaxKind.None))
                {
                    usings.Add(directive.WithoutTrivia().ToString());
                }
            }
        }

        return usings.Distinct(StringComparer.Ordinal);
    }

    private static string Write(TypeDeclarationInfo type, IEnumerable<string> usings, RuleReader reader)
    {
        var writer = new CodeWriter().Header();
        foreach (var directive in usings)
        {
            writer.Line(directive);
        }

        writer.Line();
        using (writer.TypeScope(type))
        {
            using (writer.Block(type.PartialHeader))
            {
                writer.Line("/// <inheritdoc />");
                writer.Line("protected override bool CollectsFailuresOnlyWhenInvalid => true;");
                writer.Line();
                writer.Line("/// <summary>Runs the rules of <see cref=\"Validator\"/> and stops at the first failure.</summary>");
                writer.Line("protected override bool Validate() => Validator.Check(this, null);");
                writer.Line();
                writer.Line("/// <summary>Runs the rules of <see cref=\"Validator\"/> and adds every failure.</summary>");
                writer.Line("protected override void Validate(" + Validation + ".ValidationErrorBuilder errors) => Validator.Check(this, errors);");
                writer.Line();
                using (writer.Block("partial class Validator : " + Validation + ".Rules<" + type.FullyQualifiedName + ">"))
                {
                    foreach (var field in reader.Fields)
                    {
                        writer.Line(field);
                    }

                    if (reader.Fields.Count > 0)
                    {
                        writer.Line();
                    }

                    writer.Line("/// <summary>");
                    writer.Line("/// The rules of the constructor as code. Without <paramref name=\"errors\"/> it returns at the first failure.");
                    writer.Line("/// With it, it adds every failure and returns whether there were none.");
                    writer.Line("/// </summary>");
                    using (writer.Block("internal static bool Check(" + type.FullyQualifiedName + " " + Instance + ", " + Validation + ".ValidationErrorBuilder? " + Errors + ")"))
                    {
                        writer.Line("var valid = true;");
                        foreach (var line in reader.Body)
                        {
                            writer.Line(line);
                        }

                        writer.Line("return valid;");
                    }
                }
            }
        }

        return writer.ToString();
    }

    /// <summary>What the generator writes for one value object. Only strings, so the pipeline can cache it.</summary>
    private sealed record ValidatorModel(string HintName, string Source, EquatableArray<DiagnosticInfo> Diagnostics);

    /// <summary>One rule of a chain, with the conditions that apply to it.</summary>
    private sealed class Rule(string name, IReadOnlyList<ExpressionSyntax> arguments, Location location)
    {
        public string Name { get; } = name;

        public IReadOnlyList<ExpressionSyntax> Arguments { get; } = arguments;

        public Location Location { get; } = location;

        public List<string> Conditions { get; } = [];

        public string? Error { get; set; }
    }

    /// <summary>Reads the statements of the constructor and writes the body of <c>Check</c>.</summary>
    private sealed class RuleReader(
        SemanticModel model,
        INamedTypeSymbol container,
        INamedTypeSymbol? validator,
        TypeDeclarationInfo type,
        CancellationToken cancellationToken)
    {
        private static readonly HashSet<string> RuleNames = new(StringComparer.Ordinal)
        {
            "NotNull", "Null", "NotEmpty", "Empty", "Length", "MinimumLength", "MaximumLength", "Matches", "EmailAddress",
            "Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqualTo", "LessThan", "LessThanOrEqualTo",
            "InclusiveBetween", "ExclusiveBetween", "IsInEnum", "Must",
        };

        private int _counter;

        public List<string> Body { get; } = [];

        public List<string> Fields { get; } = [];

        public List<DiagnosticInfo> Diagnostics { get; } = [];

        public void ReadStatement(SyntaxNode statement, IReadOnlyList<string> conditions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expression = statement switch
            {
                ExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
                ExpressionSyntax plain => plain,
                _ => null,
            };

            if (expression is not InvocationExpressionSyntax invocation)
            {
                Report(DiagnosticDescriptors.RuleStatement, statement.GetLocation(), type.Name);
                return;
            }

            var calls = Unwind(invocation);
            if (calls is null)
            {
                Report(DiagnosticDescriptors.RuleStatement, statement.GetLocation(), type.Name);
                return;
            }

            var root = calls[0];
            switch (root.Name)
            {
                case "When" or "Unless" when calls.Count == 1:
                    ReadGroup(root, conditions, negate: root.Name == "Unless");
                    break;
                case "RuleFor" or "RuleForEach":
                    ReadChain(root, calls.Skip(1).ToList(), conditions, each: root.Name == "RuleForEach");
                    break;
                default:
                    Report(DiagnosticDescriptors.RuleUnknown, root.Location, root.Name);
                    break;
            }
        }

        /// <summary>Adds the failures of each property that holds another value object, under the name of the property.</summary>
        public void FoldNestedValueObjects()
        {
            foreach (var property in container.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.GetMethod is null || property.IsIndexer || !IsValidatedValueObject(property.Type))
                {
                    continue;
                }

                var name = property.Name;
                var local = Next("nested");
                Body.Add("if (" + Instance + "." + name + " is { IsValid: false } " + local + ")");
                Body.Add("{");
                Body.Add("    valid = false;");
                Body.Add("    if (" + Errors + " is null) return false;");
                Body.Add("    foreach (var failure in " + local + ".ValidationErrors)");
                Body.Add("    {");
                Body.Add("        " + Errors + ".Add(failure with { PropertyName = " + Failures + ".Nested(\"" + name + "\", failure.PropertyName) });");
                Body.Add("    }");
                Body.Add("}");
            }
        }

        private static bool IsValidatedValueObject(ITypeSymbol type)
            => type is INamedTypeSymbol { IsRecord: true, IsValueType: false } named && IsValueObject(named);

        private void ReadGroup(Call call, IReadOnlyList<string> conditions, bool negate)
        {
            if (call.Arguments.Count != 2
                || Condition(call.Arguments[0], negate) is not { } condition
                || call.Arguments[1] is not ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 0 } group)
            {
                Report(DiagnosticDescriptors.RuleLambdaShape, call.Location, "When and Unless take a condition, such as x => x.IsBusiness, and a lambda with the rules, such as () => { RuleFor(...); }.");
                return;
            }

            var inner = conditions.Concat([condition]).ToList();
            if (group.Block is { } block)
            {
                foreach (var statement in block.Statements)
                {
                    ReadStatement(statement, inner);
                }
            }
            else if (group.ExpressionBody is { } expression)
            {
                ReadStatement(expression, inner);
            }
        }

        private void ReadChain(Call root, IReadOnlyList<Call> calls, IReadOnlyList<string> conditions, bool each)
        {
            if (root.Arguments.Count != 1 || Property(root.Arguments[0]) is not { } property)
            {
                Report(DiagnosticDescriptors.RuleLambdaShape, root.Location, root.Name + " reads a property of the value object, such as x => x.Street.");
                return;
            }

            var rules = new List<Rule>();
            foreach (var call in calls)
            {
                switch (call.Name)
                {
                    case "When" or "Unless":
                        if (call.Arguments.Count == 1 && Condition(call.Arguments[0], negate: call.Name == "Unless") is { } condition)
                        {
                            foreach (var rule in rules)
                            {
                                rule.Conditions.Add(condition);
                            }
                        }
                        else
                        {
                            Report(DiagnosticDescriptors.RuleLambdaShape, call.Location, call.Name + " takes a lambda over the value object with an expression body, such as x => x.Country == \"NL\".");
                        }

                        break;
                    case "WithError":
                        if (rules.Count == 0)
                        {
                            Report(DiagnosticDescriptors.RuleUnknown, call.Location, "WithError before a rule");
                        }
                        else if (call.Arguments.Count == 1 && ErrorOf(call.Arguments[0]) is { } error)
                        {
                            rules[rules.Count - 1].Error = error;
                        }

                        break;
                    case var name when RuleNames.Contains(name):
                        rules.Add(new Rule(name, call.Arguments, call.Location));
                        break;
                    default:
                        Report(DiagnosticDescriptors.RuleUnknown, call.Location, call.Name);
                        break;
                }
            }

            var value = Next("value");
            var indent = "";
            string propertyName;
            if (each)
            {
                var items = Next("items");
                var index = Next("index");
                Body.Add("if (" + property.Access + " is { } " + items + ")");
                Body.Add("{");
                Body.Add("    var " + index + " = 0;");
                Body.Add("    foreach (var " + value + " in " + items + ")");
                Body.Add("    {");
                indent = "        ";
                propertyName = Failures + ".Item(\"" + property.Name + "\", " + index + ")";
                WriteRules(rules, conditions, value, propertyName, indent);
                Body.Add(indent + index + "++;");
                Body.Add("    }");
                Body.Add("}");
            }
            else
            {
                Body.Add("{");
                Body.Add("    var " + value + " = " + property.Access + ";");
                propertyName = "\"" + property.Name + "\"";
                WriteRules(rules, conditions, value, propertyName, "    ");
                Body.Add("}");
            }
        }

        private void WriteRules(IReadOnlyList<Rule> rules, IReadOnlyList<string> conditions, string value, string propertyName, string indent)
        {
            foreach (var rule in rules)
            {
                if (Translate(rule, value, propertyName) is not { } translated)
                {
                    continue;
                }

                var test = string.Join(" && ", conditions.Concat(rule.Conditions).Concat(["!(" + translated.Check + ")"]));
                var failure = rule.Error is { } error
                    ? Failures + ".From(" + error + ", " + propertyName + ", " + value + ")"
                    : translated.Failure;

                Body.Add(indent + "if (" + test + ")");
                Body.Add(indent + "{");
                Body.Add(indent + "    valid = false;");
                Body.Add(indent + "    if (" + Errors + " is null) return false;");
                Body.Add(indent + "    " + Errors + ".Add(" + failure + ");");
                Body.Add(indent + "}");
            }
        }

        private (string Check, string Failure)? Translate(Rule rule, string value, string propertyName)
        {
            var arguments = new List<string>();
            foreach (var argument in rule.Arguments)
            {
                if (rule.Name == "Must")
                {
                    break;
                }

                if (Captures(argument) is { } captured)
                {
                    Report(DiagnosticDescriptors.RuleCapture, argument.GetLocation(), captured);
                    return null;
                }

                arguments.Add(argument.WithoutTrivia().ToString());
            }

            string Argument(int index) => index < arguments.Count ? arguments[index] : "default";

            switch (rule.Name)
            {
                case "NotNull":
                    return (Checks + ".IsNotNull(" + value + ")", Failures + ".NotNull(" + propertyName + ")");
                case "Null":
                    return ("!" + Checks + ".IsNotNull(" + value + ")", Failures + ".Null(" + propertyName + ", " + value + ")");
                case "NotEmpty":
                    return (Checks + ".IsNotEmpty(" + value + ")", Failures + ".NotEmpty(" + propertyName + ", " + value + ")");
                case "Empty":
                    return ("!" + Checks + ".IsNotEmpty(" + value + ")", Failures + ".Empty(" + propertyName + ", " + value + ")");
                case "Length" when arguments.Count == 2:
                    return (Checks + ".HasLength(" + value + ", " + Argument(0) + ", " + Argument(1) + ")",
                        Failures + ".Length(" + propertyName + ", " + value + "!, " + Argument(0) + ", " + Argument(1) + ")");
                case "Length":
                    return (Checks + ".HasLength(" + value + ", " + Argument(0) + ", " + Argument(0) + ")",
                        Failures + ".ExactLength(" + propertyName + ", " + value + "!, " + Argument(0) + ")");
                case "MinimumLength":
                    return (Checks + ".HasLength(" + value + ", " + Argument(0) + ", int.MaxValue)",
                        Failures + ".MinimumLength(" + propertyName + ", " + value + "!, " + Argument(0) + ")");
                case "MaximumLength":
                    return (Checks + ".HasLength(" + value + ", 0, " + Argument(0) + ")",
                        Failures + ".MaximumLength(" + propertyName + ", " + value + "!, " + Argument(0) + ")");
                case "Matches":
                    var regex = Regex(rule.Arguments.Count > 0 ? rule.Arguments[0] : null, Argument(0));
                    return (Checks + ".Matches(" + value + ", " + regex + ")",
                        Failures + ".Matches(" + propertyName + ", " + value + "!, " + regex + ".ToString())");
                case "EmailAddress":
                    return (Checks + ".IsEmailAddress(" + value + ")", Failures + ".EmailAddress(" + propertyName + ", " + value + "!)");
                case "Equal":
                    return (Checks + ".IsEqual(" + value + ", " + Argument(0) + ")", Failures + ".Equal(" + propertyName + ", " + value + ", " + Argument(0) + ")");
                case "NotEqual":
                    return ("!" + Checks + ".IsEqual(" + value + ", " + Argument(0) + ")", Failures + ".NotEqual(" + propertyName + ", " + value + ", " + Argument(0) + ")");
                case "GreaterThan" or "GreaterThanOrEqualTo" or "LessThan" or "LessThanOrEqualTo":
                    return (Checks + ".Is" + rule.Name + "(" + value + ", " + Argument(0) + ")",
                        Failures + "." + rule.Name + "(" + propertyName + ", " + value + ", " + Argument(0) + ")");
                case "InclusiveBetween" or "ExclusiveBetween":
                    return (Checks + ".Is" + rule.Name + "(" + value + ", " + Argument(0) + ", " + Argument(1) + ")",
                        Failures + "." + rule.Name + "(" + propertyName + ", " + value + ", " + Argument(0) + ", " + Argument(1) + ")");
                case "IsInEnum":
                    return (Checks + ".IsInEnum(" + value + ")", Failures + ".IsInEnum(" + propertyName + ", " + value + ")");
                case "Must":
                    if (rule.Arguments.Count == 1 && Predicate(rule.Arguments[0], value) is { } predicate)
                    {
                        return (predicate, Failures + ".Must(" + propertyName + ", " + value + ")");
                    }

                    Report(DiagnosticDescriptors.RuleLambdaShape, rule.Location, "Must takes a lambda with an expression body over the value, such as v => v.StartsWith(\"NL\"), or over the value object and the value, such as (x, v) => v != x.Other.");
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>A constant pattern becomes a static field, compiled once. Any other argument is a <c>Regex</c> already.</summary>
        private string Regex(ExpressionSyntax? argument, string text)
        {
            if (argument is null || model.GetTypeInfo(argument, cancellationToken).Type?.SpecialType != SpecialType.System_String)
            {
                return text;
            }

            var field = Next("Pattern");
            Fields.Add("private static readonly global::System.Text.RegularExpressions.Regex " + field
                       + " = new(" + text + ", global::System.Text.RegularExpressions.RegexOptions.CultureInvariant);");
            return field;
        }

        private string? Condition(ExpressionSyntax argument, bool negate)
            => Lambda(argument, [Instance]) is { } body ? (negate ? "!(" + body + ")" : "(" + body + ")") : null;

        private string? Predicate(ExpressionSyntax argument, string value)
            => argument switch
            {
                SimpleLambdaExpressionSyntax => Lambda(argument, [value]) is { } body ? "(" + body + ")" : null,
                ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } => Lambda(argument, [value]) is { } body ? "(" + body + ")" : null,
                ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 } => Lambda(argument, [Instance, value]) is { } body ? "(" + body + ")" : null,
                _ => null,
            };

        private string? ErrorOf(ExpressionSyntax argument)
        {
            if (Lambda(argument, [Instance]) is not { } body)
            {
                Report(DiagnosticDescriptors.RuleLambdaShape, argument.GetLocation(), "WithError takes a lambda over the value object that creates the error, such as x => new PostalCodeRequired(x.Country).");
                return null;
            }

            var created = ((LambdaExpressionSyntax)argument).ExpressionBody;
            if (created is BaseObjectCreationExpressionSyntax creation
                && model.GetTypeInfo(creation, cancellationToken).Type is INamedTypeSymbol errorType
                && errorType.TypeKind != TypeKind.Error
                && !IsErrorRecord(errorType))
            {
                Report(DiagnosticDescriptors.RuleErrorType, creation.GetLocation(), errorType.Name);
                return null;
            }

            return body;
        }

        private static bool IsErrorRecord(INamedTypeSymbol type)
        {
            var hasCode = type.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == "DTS.DDD.Abstractions.Attributes.ErrorCodeAttribute");
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (current.ToDisplayString() == "DTS.DDD.Results.InvalidError")
                {
                    return hasCode && type.IsRecord;
                }
            }

            return false;
        }

        /// <summary>
        /// The body of an expression lambda with its parameters replaced by <paramref name="replacements"/>, or null
        /// after a diagnostic.
        /// </summary>
        private string? Lambda(ExpressionSyntax argument, IReadOnlyList<string> replacements)
        {
            if (argument is not LambdaExpressionSyntax { ExpressionBody: { } body } lambda)
            {
                return null;
            }

            var parameters = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => [simple.Parameter.Identifier.ValueText],
                ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.Select(static p => p.Identifier.ValueText).ToArray(),
                _ => Array.Empty<string>(),
            };

            if (parameters.Length != replacements.Count)
            {
                return null;
            }

            if (Captures(body) is { } captured)
            {
                Report(DiagnosticDescriptors.RuleCapture, body.GetLocation(), captured);
                return null;
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < parameters.Length; i++)
            {
                map[parameters[i]] = replacements[i];
            }

            return new ParameterRewriter(map).Visit(body)!.WithoutTrivia().ToFullString();
        }

        /// <summary>The property a <c>RuleFor</c> lambda reads: its name for the failure and its access on the instance.</summary>
        private (string Name, string Access)? Property(ExpressionSyntax argument)
        {
            if (argument is not SimpleLambdaExpressionSyntax { ExpressionBody: { } body } lambda)
            {
                return null;
            }

            var parameter = lambda.Parameter.Identifier.ValueText;
            var names = new List<string>();
            var current = body;
            while (current is MemberAccessExpressionSyntax access && access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
            {
                names.Insert(0, access.Name.Identifier.ValueText);
                current = access.Expression;
            }

            if (current is not IdentifierNameSyntax identifier || identifier.Identifier.ValueText != parameter || names.Count == 0)
            {
                return null;
            }

            return (string.Join(".", names), Instance + "." + string.Join("?.", names));
        }

        /// <summary>The first name in <paramref name="node"/> that the copied code cannot use, or null.</summary>
        private string? Captures(SyntaxNode node)
        {
            foreach (var candidate in node.DescendantNodesAndSelf())
            {
                if (candidate is ThisExpressionSyntax or BaseExpressionSyntax)
                {
                    return candidate.ToString();
                }

                if (candidate is not IdentifierNameSyntax identifier
                    || (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier))
                {
                    continue;
                }

                var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
                switch (symbol)
                {
                    case ILocalSymbol or IParameterSymbol when !DeclaredInside(symbol, node):
                        return identifier.Identifier.ValueText;
                    case IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol
                        when !symbol.IsStatic
                             && symbol is not IMethodSymbol { MethodKind: MethodKind.LocalFunction }
                             && validator is not null
                             && SymbolEqualityComparer.Default.Equals(symbol.ContainingType, validator):
                        return identifier.Identifier.ValueText;
                }
            }

            return null;
        }

        private static bool DeclaredInside(ISymbol symbol, SyntaxNode node)
            => symbol.DeclaringSyntaxReferences.Any(reference => node.Parent is { } parent && parent.Span.Contains(reference.Span));

        private string Next(string name) => name + _counter++;

        private void Report(DiagnosticDescriptor descriptor, Location location, params string[] arguments)
            => Diagnostics.Add(DiagnosticInfo.Create(descriptor, LocationInfo.From(location), arguments));

        /// <summary>A call of the chain, from the first one on.</summary>
        private sealed record Call(string Name, IReadOnlyList<ExpressionSyntax> Arguments, Location Location);

        /// <summary>
        /// The calls of <c>RuleFor(...).NotEmpty().When(...)</c> from left to right, or null when the chain does not
        /// start with a plain method call.
        /// </summary>
        private static List<Call>? Unwind(InvocationExpressionSyntax invocation)
        {
            var calls = new List<Call>();
            ExpressionSyntax? current = invocation;
            while (current is InvocationExpressionSyntax call)
            {
                var arguments = call.ArgumentList.Arguments.Select(static argument => argument.Expression).ToList();
                switch (call.Expression)
                {
                    case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } thisAccess:
                        calls.Insert(0, new Call(thisAccess.Name.Identifier.ValueText, arguments, thisAccess.Name.GetLocation()));
                        current = null;
                        break;
                    case MemberAccessExpressionSyntax access:
                        calls.Insert(0, new Call(access.Name.Identifier.ValueText, arguments, access.Name.GetLocation()));
                        current = access.Expression;
                        break;
                    case SimpleNameSyntax name:
                        calls.Insert(0, new Call(name.Identifier.ValueText, arguments, name.GetLocation()));
                        current = null;
                        break;
                    default:
                        return null;
                }
            }

            return current is null ? calls : null;
        }
    }

    /// <summary>Replaces the parameters of a lambda with the names the generated code uses.</summary>
    private sealed class ParameterRewriter(IReadOnlyDictionary<string, string> map) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var isMemberName = node.Parent is MemberAccessExpressionSyntax access && access.Name == node
                               || node.Parent is MemberBindingExpressionSyntax
                               || node.Parent is NameColonSyntax
                               || node.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assignment && assignment.Left == node;

            return !isMemberName && map.TryGetValue(node.Identifier.ValueText, out var replacement)
                ? SyntaxFactory.IdentifierName(replacement).WithTriviaFrom(node)
                : base.VisitIdentifierName(node);
        }
    }
}
