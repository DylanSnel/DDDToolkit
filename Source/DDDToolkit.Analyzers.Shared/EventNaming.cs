using System.Linq;
using System.Text;
using DDDToolkit.BaseTypes;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// The names and versions of events, read off symbols the way the runtime reads them off types. The rule
/// itself is <see cref="EventNameConvention"/>, the same source file the runtime compiles; this class only
/// finds the attributes and the module to hand it.
/// <list type="bullet">
///   <item><description>A domain event is stored under <c>[DomainEventName]</c>, otherwise the conventional name
///   (<c>DomainEventName.Of</c>).</description></item>
///   <item><description>A contract is published under <c>[IntegrationEvent("name")]</c>, otherwise
///   <c>[DomainEventName]</c>, otherwise the conventional name (<c>IntegrationEventContract.NameOf</c>).</description></item>
///   <item><description>Either one's version is its class name's <c>V</c> suffix, otherwise
///   <c>[IntegrationEvent(Version = n)]</c>, otherwise 1 (<c>IntegrationEventContract.VersionOf</c>).</description></item>
/// </list>
/// </summary>
internal static class EventNaming
{
    public const string DomainEventNameAttribute = KnownTypes.AttributesNamespace + ".DomainEventNameAttribute";

    /// <summary>The name and version the outbox stores a domain event under.</summary>
    public static (string Name, int Version) DomainEventOf(ITypeSymbol type)
        => (PinnedDomainEventName(type) ?? ConventionalNameOf(type), VersionOf(type));

    /// <summary>The name and version a contract is published under.</summary>
    public static (string Name, int Version) ContractOf(ITypeSymbol type)
        => (PinnedContractName(type) ?? PinnedDomainEventName(type) ?? ConventionalNameOf(type), VersionOf(type));

    /// <summary>The module and class name in kebab case, whatever the attributes say.</summary>
    public static string ConventionalNameOf(ITypeSymbol type)
        => EventNameConvention.NameFor(type.MetadataName, ModuleBoundary.ModuleOf(type.ContainingAssembly));

    /// <summary>The version: the class name's suffix, otherwise <c>[IntegrationEvent(Version = n)]</c>, otherwise 1.</summary>
    public static int VersionOf(ITypeSymbol type)
        => EventNameConvention.Split(type.MetadataName).Version ?? ExplicitVersion(IntegrationEventAttributeOf(type)) ?? 1;

    /// <summary>The name <c>[DomainEventName("...")]</c> pins, or null.</summary>
    public static string? PinnedDomainEventName(ITypeSymbol type) => StringArgument(Find(type, DomainEventNameAttribute));

    /// <summary>The name <c>[IntegrationEvent("...")]</c> pins, or null when it has none or is absent.</summary>
    public static string? PinnedContractName(ITypeSymbol type) => StringArgument(IntegrationEventAttributeOf(type));

    /// <summary>The type's <c>[IntegrationEvent]</c>, or null.</summary>
    public static AttributeData? IntegrationEventAttributeOf(ITypeSymbol type) => Find(type, KnownTypes.IntegrationEventAttribute);

    /// <summary>The <c>Version = n</c> an attribute states, or null when it states none.</summary>
    public static int? ExplicitVersion(AttributeData? attribute)
    {
        if (attribute is null)
        {
            return null;
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Version" && argument.Value.Value is int version)
            {
                return version;
            }
        }

        return null;
    }

    /// <summary>Whether the type is a domain event: it implements <c>DDDToolkit.Interfaces.IDomainEvent</c>.</summary>
    public static bool IsDomainEvent(INamedTypeSymbol type)
        => type.AllInterfaces.Any(static candidate =>
            candidate.Name == "IDomainEvent"
            && candidate.Arity == 0
            && candidate.ContainingNamespace.ToDisplayString() == "DDDToolkit.Interfaces");

    /// <summary>
    /// The identifier of the constant that holds <paramref name="name"/> in the generated
    /// <c>{Module}EventNames</c>: the name without its module prefix, in PascalCase, so
    /// <c>ordering.order-placed</c> is <c>OrderPlaced</c>. Null when nothing identifier-like is left.
    /// </summary>
    public static string? ConstantNameFor(string name, string? module)
    {
        var prefix = module is null ? string.Empty : EventNameConvention.Kebab(module) + ".";
        var local = prefix.Length > 1 && name.StartsWith(prefix, System.StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;

        var identifier = Pascal(local);
        if (identifier.Length == 0)
        {
            return null;
        }

        return char.IsDigit(identifier[0]) ? "_" + identifier : identifier;
    }

    /// <summary>PascalCase of the letters and digits in <paramref name="value"/>, every other character a word break.</summary>
    public static string Pascal(string value)
    {
        var builder = new StringBuilder(value.Length);
        var startOfWord = true;

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                startOfWord = true;
                continue;
            }

            builder.Append(startOfWord ? char.ToUpperInvariant(character) : character);
            startOfWord = false;
        }

        return builder.ToString();
    }

    private static AttributeData? Find(ITypeSymbol type, string metadataName)
        => type.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == metadataName);

    private static string? StringArgument(AttributeData? attribute)
        => attribute is { ConstructorArguments.Length: > 0 } && attribute.ConstructorArguments[0].Value is string value ? value : null;
}
