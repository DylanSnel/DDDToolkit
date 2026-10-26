using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.HotChocolate.Analyzers;

/// <summary>
/// The classes of a project that belong to one GraphQL schema, marked <c>[GraphQLSchema(name, operation)]</c>, and
/// what <c>Add{Module}GraphQlRuntimeBindings()</c> writes for them: each class registered for the schema a builder
/// builds, by the builder's name, and for no other.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate's generator registers what it finds in a project in one method that every schema calls: a static
/// method marked <c>[Query]</c>, <c>[Mutation]</c> or <c>[Subscription]</c>, a class marked <c>[QueryType]</c>,
/// <c>[ExtendObjectType]</c> or <c>[ObjectType&lt;T&gt;]</c>, and a class derived from one of its type classes. It
/// finds them by the attribute's full name, so the toolkit's attribute is not among them, and a marked class that
/// carries none of those is invisible to it. DDD00062 holds a marked class to that: otherwise HotChocolate would put
/// its fields into every schema after all.
/// </para>
/// <para>
/// The fields are registered as HotChocolate's generator registers the <c>[Query]</c> methods it finds: a type
/// extension of the operation type, which hands HotChocolate each method to bind by reflection when the schema is
/// built. One extension per schema and operation type, named after neither, since HotChocolate knows an extension
/// by the type it extends. A method is found by its name, as HotChocolate's generator finds a <c>[Query]</c>
/// method; only a name two public static methods share is found by the parameter types as well. A parameter's type
/// may be one another generator writes, such as the interface HotChocolate's generator writes for a data loader,
/// and generators do not see each other's output: by its name alone such a method is found all the same. Two
/// methods that would be one field, two overloads for one, are refused by DDD00062: HotChocolate would keep one of
/// them and drop the other without a word.
/// </para>
/// <para>
/// The registration is written into the bindings every schema already calls, so marking a class asks nothing more
/// of the host, and there is no second method to remember to call. A project without a marked class gets exactly
/// the bindings it had. A project with one gets the bindings method also when it has nothing of its own to bind, and
/// that method calls the module's other bindings, as every project's does.
/// </para>
/// </remarks>
internal static class GraphQLSchemaClasses
{
    /// <summary>The toolkit's attribute, by its metadata name.</summary>
    public const string AttributeMetadataName = "DDDToolkit.HotChocolate.Attributes.GraphQLSchemaAttribute";

    /// <summary>The attributes on a class that HotChocolate's generator registers it for, by name in <c>HotChocolate.Types</c>.</summary>
    private static readonly HashSet<string> RegisteredClassAttributes = new(StringComparer.Ordinal)
    {
        "ExtendObjectTypeAttribute", "ObjectTypeAttribute", "InterfaceTypeAttribute", "UnionTypeAttribute",
        "EnumTypeAttribute", "InputObjectTypeAttribute", "QueryTypeAttribute", "MutationTypeAttribute", "SubscriptionTypeAttribute",
    };

    /// <summary>The base classes HotChocolate's generator registers a class for, by name in <c>HotChocolate.Types</c>.</summary>
    private static readonly HashSet<string> RegisteredBaseClasses = new(StringComparer.Ordinal)
    {
        "ObjectType", "InterfaceType", "UnionType", "InputObjectType", "EnumType", "ScalarType",
        "ObjectTypeExtension", "InterfaceTypeExtension", "UnionTypeExtension", "InputObjectTypeExtension", "EnumTypeExtension",
    };

    /// <summary>The attributes on a static method that HotChocolate's generator makes a field of every schema for, by name in <c>HotChocolate</c>.</summary>
    private static readonly HashSet<string> RegisteredMethodAttributes = new(StringComparer.Ordinal)
    {
        "QueryAttribute", "MutationAttribute", "SubscriptionAttribute",
    };

    /// <summary>The operation types, by the value of HotChocolate's <c>OperationType</c>.</summary>
    private static readonly string[] Operations = ["Query", "Mutation", "Subscription"];

    /// <summary>
    /// How a parameter's type is written into <c>typeof()</c>: fully qualified, without the nullable marks of
    /// reference types, which <c>typeof</c> refuses, and with tuples spelled as the types they are.
    /// </summary>
    private static readonly SymbolDisplayFormat TypeOfFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.ExpandValueTuple);

    /// <summary>
    /// Every marked class of the project, each read once whatever number of attributes it carries. A record is a
    /// class too, which the attribute may mark and HotChocolate's generator reads: it is held to the same rules.
    /// </summary>
    public static IncrementalValuesProvider<SchemaClass> Find(IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                static (syntax, cancellationToken) => Read(syntax, cancellationToken))
            .Where(static found => found is not null)
            .Select(static (found, _) => found!);

    private static SchemaClass? Read(GeneratorAttributeSyntaxContext syntax, CancellationToken cancellationToken)
    {
        if (syntax.TargetSymbol is not INamedTypeSymbol type)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var name = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var problems = new List<DiagnosticInfo>();
        var location = LocationInfo.From(type);

        void Problem(LocationInfo? at, string what)
            => problems.Add(DiagnosticInfo.Create(DiagnosticDescriptors.GraphQLSchemaClassMisdeclared, at ?? location, name, what));

        // Every attribute of the class, not only the ones on this part of it: a partial class is read once per part
        // that carries one, and each reading says the same.
        var schemas = new List<SchemaMembership>();
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeMetadataName)
            {
                continue;
            }

            var at = At(attribute, cancellationToken) ?? location;
            if (attribute.ConstructorArguments.Length != 2)
            {
                continue;
            }

            var schema = attribute.ConstructorArguments[0].Value as string;
            var operation = attribute.ConstructorArguments[1].Value is int value && value >= 0 && value < Operations.Length ? Operations[value] : null;

            if (string.IsNullOrWhiteSpace(schema))
            {
                Problem(at, "names no schema: give it the name the schema is registered under, as in AddGraphQLServer(\"admin\")");
            }
            else if (operation is null)
            {
                Problem(at, "names no operation type: OperationType.Query, OperationType.Mutation or OperationType.Subscription");
            }
            else if (!schemas.Any(known => known.Name == schema && known.Operation == operation))
            {
                schemas.Add(new SchemaMembership(schema!, operation));
            }
        }

        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                Problem(null, "is generic, or inside a generic class: a class of fields is one the generated registration can name");
                break;
            }

            if (current.IsFileLocal)
            {
                Problem(null, "is a file class, or inside one: the generated registration names it from another file, so make it internal");
                break;
            }

            if (current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                Problem(null, "is private or protected inside '" + current.ContainingType?.Name + "': the generated registration names it from outside, so make it internal");
                break;
            }
        }

        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass
                && IsIn(attributeClass, "HotChocolate.Types")
                && RegisteredClassAttributes.Contains(attributeClass.Name))
            {
                Problem(
                    At(attribute, cancellationToken),
                    "is marked [" + Shortened(attributeClass.Name) + "] as well, which HotChocolate's generator registers in every schema: remove it, [GraphQLSchema] says what the class is");
            }
        }

        for (var parent = type.BaseType; parent is not null; parent = parent.BaseType)
        {
            if (IsIn(parent, "HotChocolate.Types") && RegisteredBaseClasses.Contains(parent.Name))
            {
                Problem(null, "derives from " + parent.Name + ", which HotChocolate's generator registers in every schema: a class of one schema derives from nothing");
                break;
            }
        }

        var streams = StreamsOf(type);
        var shared = SharedNames(type);
        var fields = new List<SchemaField>();
        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false } method)
            {
                continue;
            }

            if (!method.IsStatic)
            {
                // Nothing makes an instance of the class, so no schema calls such a method. A [QueryType] class's
                // fields are often instance methods, and without this they would be dropped without a word.
                if (method.DeclaredAccessibility == Accessibility.Public && !method.IsOverride && !IsIgnored(method))
                {
                    Problem(
                        LocationInfo.From(method),
                        "its method '" + method.Name + "' is an instance method, which no schema calls: a field of one schema is a public static method, so make it static");
                }

                continue;
            }

            var registered = method.GetAttributes()
                .Select(attribute => attribute.AttributeClass)
                .FirstOrDefault(attributeClass => attributeClass is not null && IsIn(attributeClass, "HotChocolate") && RegisteredMethodAttributes.Contains(attributeClass.Name));
            if (registered is not null)
            {
                Problem(
                    LocationInfo.From(method),
                    "its method '" + method.Name + "' is marked [" + Shortened(registered.Name) + "], which HotChocolate's generator makes a field of every schema: remove it, the attribute on the class says what its methods are");
                continue;
            }

            // A method that takes a pointer is no field: GraphQL has nothing a pointer could be.
            if (method.DeclaredAccessibility != Accessibility.Public
                || method.IsGenericMethod
                || method.IsExtensionMethod
                || streams.Contains(method.Name)
                || IsIgnored(method)
                || method.Parameters.Any(parameter => parameter.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol))
            {
                continue;
            }

            // A name of its own finds the method, whatever its parameters are. A name it shares with another public
            // static method needs the parameter types too, so each must be one this generator can name: not a type
            // another generator writes, which it does not see.
            var isShared = shared.Contains(method.Name);
            if (isShared && method.Parameters.FirstOrDefault(parameter => IsUnseen(parameter.Type)) is { } unseen)
            {
                Problem(
                    LocationInfo.From(method),
                    "its method '" + method.Name + "' has a name another public static method of the class has, so the registration tells the two apart by their parameters, and the type of '"
                    + unseen.Name + "' is not one it can name, written by another generator: give the method a name of its own");
                continue;
            }

            fields.Add(new SchemaField(
                method.Name,
                FieldName(method),
                method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                isShared,
                isShared ? method.Parameters.Select(TypeOf).ToEquatableArray() : EquatableArray<string>.Empty,
                LocationInfo.From(method)));
        }

        // A class that adds nothing to its schema is a mistake the schema would not show until somebody asked. Where
        // something else is reported already, an instance method or a method marked [Query], that is the reason.
        if (fields.Count == 0 && problems.Count == 0 && schemas.Count > 0)
        {
            Problem(null, "has no field: a field of one schema is a public static method, not marked [GraphQLIgnore], and the class has none");
        }

        return new SchemaClass(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            schemas.ToEquatableArray(),
            fields.ToEquatableArray(),
            problems.ToEquatableArray());
    }

    /// <summary>
    /// What the bindings register of the marked classes, one extension per schema and operation type that has a
    /// field, of the classes without an error; and what is wrong, with one class or between two.
    /// </summary>
    /// <param name="found">The marked classes, a partial class once per part that carries the attribute.</param>
    public static SchemaRegistration Registration(ImmutableArray<SchemaClass> found)
    {
        if (found.IsDefaultOrEmpty)
        {
            return SchemaRegistration.None;
        }

        var classes = found
            .GroupBy(type => type.Type, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(type => type.Type, StringComparer.Ordinal)
            .ToList();

        var diagnostics = classes.SelectMany(type => type.Diagnostics).ToList();
        var refused = new HashSet<string>(classes.Where(type => type.Diagnostics.HasErrors()).Select(type => type.Type), StringComparer.Ordinal);

        // Each schema and operation type, with its fields in the order of their classes' names, then as written.
        var groups = classes
            .Where(type => !refused.Contains(type.Type))
            .SelectMany(type => type.Schemas.Select(schema => (Schema: schema, Type: type)))
            .GroupBy(entry => entry.Schema)
            .OrderBy(group => group.Key.Name, StringComparer.Ordinal)
            .ThenBy(group => Array.IndexOf(Operations, group.Key.Operation))
            .Select(group => (Schema: group.Key, Fields: group.SelectMany(entry => entry.Type.Fields.Select(field => (Class: entry.Type.Type, Field: field))).ToList()))
            .ToList();

        // Two methods that are one field of one operation type: HotChocolate keeps one of them and drops the other.
        foreach (var (schema, fields) in groups)
        {
            foreach (var same in fields.GroupBy(entry => entry.Field.Name, StringComparer.Ordinal).Where(same => same.Count() > 1))
            {
                var first = same.First();
                foreach (var other in same.Skip(1))
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.GraphQLSchemaClassMisdeclared,
                        other.Field.Location,
                        DisplayName(other.Class),
                        "its method '" + other.Field.Display + "' is the field '" + other.Field.Name + "' of the " + schema.Operation + " type of the schema " + Literal(schema.Name)
                        + ", as '" + first.Field.Display + "' is: HotChocolate keeps one of the two and drops the other without a word, so give one of them another name, or another [GraphQLName]"));
                    refused.Add(other.Class);
                    refused.Add(first.Class);
                }
            }
        }

        var extensions = groups
            .Select(group => (group.Schema, Fields: group.Fields.Where(entry => !refused.Contains(entry.Class)).ToList()))
            .Where(group => group.Fields.Count > 0)
            .Select((group, index) => new SchemaExtension(
                group.Schema.Name,
                group.Schema.Operation,
                group.Schema.Operation + "FieldsOf" + Identifier(group.Schema.Name) + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                group.Fields.Select(entry => new RegisteredField(entry.Class, entry.Field.Method, entry.Field.Shared, entry.Field.ParameterTypes)).ToEquatableArray()))
            .ToEquatableArray();

        return new SchemaRegistration(extensions, diagnostics.ToEquatableArray());
    }

    /// <summary>
    /// Writes, inside the bindings method, what registers the marked classes for the schema the builder builds: a
    /// <c>switch</c> over its name. Nothing when no class is registered.
    /// </summary>
    public static void WriteRegistration(CodeWriter writer, EquatableArray<SchemaExtension> extensions)
    {
        if (extensions.Count == 0)
        {
            return;
        }

        writer.Line("// The classes marked [GraphQLSchema], each for the schema of the name it carries and for no other.");
        using (writer.Block("switch (builder.Name)"))
        {
            foreach (var schema in extensions.GroupBy(extension => extension.Schema).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                writer.Line("case " + Literal(schema.Key) + ":");
                foreach (var extension in schema)
                {
                    writer.Line("    builder.AddTypeExtension<" + extension.Class + ">();");
                    writer.Line("    builder.ConfigureSchema(static schema => schema.TryAddRootType(");
                    writer.Line("        static () => new global::HotChocolate.Types.ObjectType(static type => type.Name(global::HotChocolate.Types.OperationTypeNames." + extension.Operation + ")),");
                    writer.Line("        global::HotChocolate.Language.OperationType." + extension.Operation + "));");
                }

                writer.Line("    break;");
            }
        }

        writer.Line();
    }

    /// <summary>Writes the extensions <see cref="WriteRegistration"/> adds, as private classes beside the bindings method.</summary>
    public static void WriteExtensions(CodeWriter writer, EquatableArray<SchemaExtension> extensions)
    {
        foreach (var extension in extensions)
        {
            writer.Line();
            writer.Line("/// <summary>The " + Escape(extension.Operation.ToLowerInvariant()) + " fields of the schema " + Escape(Literal(extension.Schema)) + ".</summary>");
            using (writer.Block("private sealed class " + extension.Class + " : global::HotChocolate.Types.ObjectTypeExtension"))
            {
                using (writer.Block("protected override void Configure(global::HotChocolate.Types.IObjectTypeDescriptor descriptor)"))
                {
                    writer.Line("const global::System.Reflection.BindingFlags Flags = global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static;");
                    writer.Line("descriptor.Name(global::HotChocolate.Types.OperationTypeNames." + extension.Operation + ");");
                    foreach (var field in extension.Fields)
                    {
                        var types = !field.Shared
                            ? string.Empty
                            : field.ParameterTypes.Count == 0
                                ? ", global::System.Type.EmptyTypes"
                                : ", new global::System.Type[] { " + string.Join(", ", field.ParameterTypes) + " }";
                        writer.Line("descriptor.Field(typeof(" + field.Type + ").GetMethod(" + Literal(field.Method) + ", Flags" + types + ")!);");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The names two or more public static methods of a class have: <c>GetMethod(name, Flags)</c> would find more
    /// than one by such a name, ignored ones and generic ones included, so the registration names the parameter
    /// types as well.
    /// </summary>
    private static HashSet<string> SharedNames(INamedTypeSymbol type)
        => new(
            type.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method => method is { MethodKind: MethodKind.Ordinary, IsStatic: true, DeclaredAccessibility: Accessibility.Public })
                .GroupBy(method => method.Name, StringComparer.Ordinal)
                .Where(same => same.Count() > 1)
                .Select(same => same.Key),
            StringComparer.Ordinal);

    /// <summary>
    /// The methods of a class that a subscription field names with <c>[Subscribe(With = ...)]</c>: the stream it
    /// subscribes to. Such a method is no field, as it is none of a class HotChocolate's generator reads.
    /// </summary>
    private static HashSet<string> StreamsOf(INamedTypeSymbol type)
    {
        var streams = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attribute in method.GetAttributes())
            {
                if (attribute.AttributeClass is { Name: "SubscribeAttribute" } subscribe && IsIn(subscribe, "HotChocolate.Types"))
                {
                    foreach (var argument in attribute.NamedArguments)
                    {
                        if (argument.Key == "With" && argument.Value.Value is string with)
                        {
                            streams.Add(with);
                        }
                    }
                }
            }
        }

        return streams;
    }

    /// <summary>
    /// The name HotChocolate gives a method's field: what <c>[GraphQLName]</c> says, or the method's name without
    /// <c>Get</c> in front and, for a method that answers later, <c>Async</c> behind, with its leading capitals in
    /// lower case. HotChocolate's own rule, so that two methods it would make one field are told.
    /// </summary>
    private static string FieldName(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "GraphQLNameAttribute" } named
                && IsIn(named, "HotChocolate")
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string name)
            {
                return name;
            }
        }

        var text = method.Name;
        if (text.StartsWith("Get", StringComparison.Ordinal) && text.Length > "Get".Length)
        {
            text = text.Substring("Get".Length);
        }

        if (AnswersLater(method.ReturnType) && text.Length > "Async".Length && text.EndsWith("Async", StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - "Async".Length);
        }

        if (char.IsLower(text[0]))
        {
            return text;
        }

        // The leading capitals in lower case, but the last of several where a word follows them: URLPath is urlPath.
        var characters = text.ToCharArray();
        var index = 0;
        for (; index < characters.Length && char.IsLetter(characters[index]) && char.IsUpper(characters[index]); index++)
        {
            characters[index] = char.ToLowerInvariant(characters[index]);
        }

        if (index < characters.Length && index > 1 && char.IsLetter(characters[index]))
        {
            characters[index - 1] = char.ToUpperInvariant(characters[index - 1]);
        }

        return new string(characters);
    }

    /// <summary>Whether a method answers later, as HotChocolate reads it: a task, a value task or a stream.</summary>
    private static bool AnswersLater(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.ToDisplayString() is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask"
                or "System.Threading.Tasks.ValueTask<TResult>" or "System.Collections.Generic.IAsyncEnumerable<T>")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What a parameter's type is in <c>typeof()</c>, by reference where the parameter is one.</summary>
    private static string TypeOf(IParameterSymbol parameter)
    {
        var type = parameter.Type;
        var written = type.TypeKind == TypeKind.Dynamic
            ? "typeof(object)"
            : "typeof(" + type.ToDisplayString(TypeOfFormat) + ")";

        return parameter.RefKind == RefKind.None ? written : written + ".MakeByRefType()";
    }

    /// <summary>
    /// Whether a type is, or holds, one this generator does not know: written by another generator, whose output
    /// it does not see, or by nobody, which the compiler reports itself.
    /// </summary>
    private static bool IsUnseen(ITypeSymbol type) => type switch
    {
        IErrorTypeSymbol => true,
        IArrayTypeSymbol array => IsUnseen(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(IsUnseen),
        _ => false,
    };

    /// <summary>Where an attribute is written, or null for one that came from metadata.</summary>
    private static LocationInfo? At(AttributeData attribute, CancellationToken cancellationToken)
        => attribute.ApplicationSyntaxReference is { } reference ? LocationInfo.From(reference.GetSyntax(cancellationToken)) : null;

    /// <summary>Whether a method's attribute makes it no field: HotChocolate's <c>[GraphQLIgnore]</c>, or a data loader's <c>[DataLoader]</c>.</summary>
    private static bool IsIgnored(IMethodSymbol method)
        => method.GetAttributes().Any(attribute => attribute.AttributeClass is { } type
            && ((type.Name == "GraphQLIgnoreAttribute" && IsIn(type, "HotChocolate"))
                || (type.Name == "DataLoaderAttribute" && IsIn(type, "GreenDonut"))));

    private static bool IsIn(INamedTypeSymbol type, string ns)
        => type.ContainingNamespace?.ToDisplayString() == ns;

    /// <summary>An attribute's name as it is written on a class: without <c>Attribute</c>.</summary>
    private static string Shortened(string attribute)
        => attribute.EndsWith("Attribute", StringComparison.Ordinal) ? attribute.Substring(0, attribute.Length - "Attribute".Length) : attribute;

    /// <summary>A fully qualified class as a diagnostic names it: without <c>global::</c>.</summary>
    private static string DisplayName(string type)
        => type.StartsWith("global::", StringComparison.Ordinal) ? type.Substring("global::".Length) : type;

    /// <summary>A schema's name as part of a class name: its letters and digits.</summary>
    private static string Identifier(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(builder.Length == 0 ? char.ToUpperInvariant(character) : character);
            }
        }

        return builder.Length == 0 ? "Schema" : builder.ToString();
    }

    /// <summary>A string as a C# literal.</summary>
    private static string Literal(string value)
        => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>A text as XML documentation may hold it.</summary>
    private static string Escape(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>A class marked <c>[GraphQLSchema]</c>, as the generated registration needs it.</summary>
/// <param name="Type">The class, fully qualified.</param>
/// <param name="Schemas">The schemas it belongs to, each with the operation type its methods are fields of.</param>
/// <param name="Fields">Its public static methods that are fields.</param>
/// <param name="Diagnostics">What is wrong with it: a class with an error is not registered.</param>
internal sealed record SchemaClass(
    string Type,
    EquatableArray<SchemaMembership> Schemas,
    EquatableArray<SchemaField> Fields,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>One schema a class belongs to.</summary>
/// <param name="Name">The schema's name.</param>
/// <param name="Operation">The operation type its methods are fields of: <c>Query</c>, <c>Mutation</c> or <c>Subscription</c>.</param>
internal sealed record SchemaMembership(string Name, string Operation);

/// <summary>A method that is a field.</summary>
/// <param name="Method">Its name.</param>
/// <param name="Name">The field's name, as HotChocolate gives it.</param>
/// <param name="Display">The method as a diagnostic names it, with its parameters.</param>
/// <param name="Shared">Whether another public static method of the class has its name, so it is found by its parameter types too.</param>
/// <param name="ParameterTypes">Where <paramref name="Shared"/>, each parameter's type as <c>typeof()</c> writes it, so an overload is told apart.</param>
/// <param name="Location">Where the method is declared.</param>
internal sealed record SchemaField(string Method, string Name, string Display, bool Shared, EquatableArray<string> ParameterTypes, LocationInfo? Location);

/// <summary>What the bindings register of a project's marked classes, and what is wrong with them.</summary>
/// <param name="Extensions">One extension per schema and operation type that has a field.</param>
/// <param name="Diagnostics">What is wrong, with one class or between two.</param>
internal sealed record SchemaRegistration(EquatableArray<SchemaExtension> Extensions, EquatableArray<DiagnosticInfo> Diagnostics)
{
    /// <summary>A project without a marked class.</summary>
    public static SchemaRegistration None { get; } = new(EquatableArray<SchemaExtension>.Empty, EquatableArray<DiagnosticInfo>.Empty);
}

/// <summary>The fields of one operation type of one schema, as one extension of that type.</summary>
/// <param name="Schema">The schema's name.</param>
/// <param name="Operation">The operation type: <c>Query</c>, <c>Mutation</c> or <c>Subscription</c>.</param>
/// <param name="Class">The extension's class, private beside the bindings method.</param>
/// <param name="Fields">Its fields, in the order of their classes' names, then as written.</param>
internal sealed record SchemaExtension(string Schema, string Operation, string Class, EquatableArray<RegisteredField> Fields);

/// <summary>A method an extension makes a field.</summary>
/// <param name="Type">Its class, fully qualified.</param>
/// <param name="Method">Its name.</param>
/// <param name="Shared">Whether it is found by its parameter types as well as its name.</param>
/// <param name="ParameterTypes">Where <paramref name="Shared"/>, each parameter's type as <c>typeof()</c> writes it.</param>
internal sealed record RegisteredField(string Type, string Method, bool Shared, EquatableArray<string> ParameterTypes);
