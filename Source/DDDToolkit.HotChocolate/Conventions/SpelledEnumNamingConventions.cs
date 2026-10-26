using System.Collections.Concurrent;
using System.Reflection;
using HotChocolate;
using HotChocolate.Types.Descriptors;
using Microsoft.Extensions.ObjectPool;

namespace DDDToolkit.HotChocolate.Conventions;

/// <summary>
/// HotChocolate's naming conventions with the values of the application's enums spelled the host's way.
/// Everything else is named as HotChocolate names it, its own enums included, and described from the XML
/// documentation as it would be without this class.
/// </summary>
/// <remarks>
/// A schema has one set of naming conventions, so registering this replaces the default one. The default reads
/// descriptions from XML documentation files according to the schema's options, which are not known when a
/// convention is constructed; <see cref="Initialize"/> reads them, and until then nothing is described.
/// </remarks>
/// <param name="spelling">How the schema's enum values are spelled.</param>
internal sealed class SpelledEnumNamingConventions(EnumValueSpelling spelling) : DefaultNamingConventions(new Documentation())
{
    private static readonly ConcurrentDictionary<Assembly, bool> OwnedByHotChocolate = new();

    /// <inheritdoc />
    protected override void Initialize(IConventionContext context)
    {
        base.Initialize(context);

        // What the default conventions are built with: the files the options name, or none when the schema
        // turned XML documentation off.
        var options = context.DescriptorContext.Options;
        if (options.UseXmlDocumentation)
        {
            ((Documentation)DocumentationProvider).Source = new XmlDocumentationProvider(
                new XmlDocumentationFileResolver(options.ResolveXmlDocumentationFileName),
                new DefaultObjectPoolProvider().CreateStringBuilderPool());
        }
    }

    /// <inheritdoc />
    public override string GetEnumValueName(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (spelling == EnumValueSpelling.UpperSnakeCase || value.GetType() is not { IsEnum: true } type || IsHotChocolates(type))
        {
            return base.GetEnumValueName(value);
        }

        var name = value.ToString()!;

        // A member that was given a name keeps it: [GraphQLName] is a decision about that one value.
        if (type.GetField(name)?.IsDefined(typeof(GraphQLNameAttribute)) == true)
        {
            return base.GetEnumValueName(value);
        }

        var spelled = EnumValueSpellings.Spell(name, spelling);

        // GraphQL reads these three as literals wherever a value is written, so an enum cannot have them: the
        // schema would print, and no document could name the value. In capitals they are ordinary names, which
        // is why only the lower spelling can run into it.
        if (spelled is "true" or "false" or "null")
        {
            throw new InvalidOperationException(
                $"The enum value {type.Name}.{name} would be spelled '{spelled}', which GraphQL reads as a literal and not as an enum value. "
                + "Give the member a name of its own with [GraphQLName].");
        }

        return spelled;
    }

    /// <summary>
    /// Whether an enum is one of HotChocolate's own, such as the one its <c>@serializeAs</c> directive takes.
    /// Those keep HotChocolate's spelling: its directives, the Fusion composer and its tools know their values
    /// by it, and a source schema that spelled them otherwise would not compose.
    /// </summary>
    private static bool IsHotChocolates(Type type)
        => OwnedByHotChocolate.GetOrAdd(type.Assembly, static assembly =>
            assembly.GetName().Name is { } name
            && (name.Equals("HotChocolate", StringComparison.Ordinal)
                || name.StartsWith("HotChocolate.", StringComparison.Ordinal)
                || name.StartsWith("GreenDonut", StringComparison.Ordinal)));

    /// <summary>Stands in for the documentation provider until the schema's options say which one it is.</summary>
    private sealed class Documentation : IDocumentationProvider
    {
        public IDocumentationProvider? Source { get; set; }

        public string? GetDescription(Type type) => Source?.GetDescription(type);

        public string? GetDescription(MemberInfo member) => Source?.GetDescription(member);

        public string? GetDescription(ParameterInfo parameter) => Source?.GetDescription(parameter);
    }
}
