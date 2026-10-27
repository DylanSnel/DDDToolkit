using System.Collections.Concurrent;
using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>
/// Runs the conventions that markers name with <see cref="ModelConventionAttribute"/>: a package's convention runs in
/// a model that maps a class with its marker, on the class or on one of its properties. Tenancy's runs where a class
/// marks its tenant with <c>[OfTenant]</c>, and Membership's where a class is declared with <c>[Member&lt;…&gt;]</c> or
/// <c>[CustomRole&lt;…&gt;]</c>. Each convention runs once per model, in the order the model first names it, and finds
/// the marked classes itself.
/// <para>
/// Every class the model maps counts: one the context names, one Entity Framework reaches through a navigation of
/// another, and one the context owns, also where two owners share it. A marker may be generic: the marker's definition
/// names the convention, and every class declared with it over any type arguments runs that convention.
/// </para>
/// <para>
/// A model whose classes carry no such marker is not touched. A convention the application does not have refuses the
/// model while it is built, naming the class, the marker and the assembly to reference. Without it the marker would
/// map nothing, and nothing would say so. The conventions are found from the classes the model maps and from nothing
/// registered, so <c>dotnet ef</c> builds the model the application runs with.
/// </para>
/// </summary>
public sealed class MarkerConvention : IModelFinalizingConvention
{
    /// <summary>The convention each attribute names, or <see langword="null"/> for one that names none: read once per attribute.</summary>
    private static readonly ConcurrentDictionary<Type, string?> Named = new();

    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        // Each convention once, with the first place that names it, which a refusal names.
        var conventions = new Dictionary<string, string>(StringComparer.Ordinal);
        // A type two owners share is mapped under a name of each, over the one class. A property bag has no class of its own.
        var types = modelBuilder.Metadata.GetEntityTypes()
            .Where(static entityType => !entityType.IsPropertyBag)
            .Select(static entityType => entityType.ClrType)
            .Distinct();

        foreach (var type in types)
        {
            foreach (var (convention, place) in NamedOn(type))
            {
                conventions.TryAdd(convention, place);
            }
        }

        foreach (var (convention, place) in conventions)
        {
            Make(convention, place).ProcessModelFinalizing(modelBuilder, context);
        }
    }

    /// <summary>The conventions the markers on <paramref name="type"/> and on its properties name, each with where it is marked.</summary>
    private static IEnumerable<(string Convention, string Place)> NamedOn(Type type)
    {
        foreach (var attribute in Attribute.GetCustomAttributes(type, inherit: true))
        {
            if (ConventionOf(attribute.GetType()) is { } convention)
            {
                yield return (convention, "'" + type.Name + "' is marked [" + MarkerName(attribute.GetType()) + "]");
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var attribute in Attribute.GetCustomAttributes(property, inherit: true))
            {
                if (ConventionOf(attribute.GetType()) is { } convention)
                {
                    yield return (convention, "'" + type.Name + "." + property.Name + "' is marked [" + MarkerName(attribute.GetType()) + "]");
                }
            }
        }
    }

    /// <summary>The convention the marker <paramref name="attribute"/> names, read off its definition where the marker is generic.</summary>
    private static string? ConventionOf(Type attribute)
        => Named.GetOrAdd(
            attribute,
            static type => (type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type).GetCustomAttribute<ModelConventionAttribute>(inherit: false)?.Convention);

    /// <summary>The convention <paramref name="convention"/> names, made; throws where the application does not have it.</summary>
    private static IModelFinalizingConvention Make(string convention, string place)
    {
        var type = Type.GetType(convention, throwOnError: false);
        if (type is null)
        {
            var comma = convention.IndexOf(',', StringComparison.Ordinal);
            var assembly = comma < 0 ? "the assembly of " + convention : convention[(comma + 1)..].Trim();
            throw new InvalidOperationException(
                place + ", which " + assembly + " maps, and the application does not have it. Reference " + assembly
                + " from the project of the context, or remove the marker.");
        }

        if (!typeof(IModelFinalizingConvention).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                place + ", which names the convention '" + type.FullName + "'. A convention a marker names has a public constructor that takes nothing, "
                + "and implements IModelFinalizingConvention.");
        }

        return (IModelFinalizingConvention)Activator.CreateInstance(type)!;
    }

    /// <summary>The marker as it is written: its name without <c>Attribute</c> and without the count of its type parameters.</summary>
    private static string MarkerName(Type attribute)
    {
        var name = attribute.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        name = tick < 0 ? name : name[..tick];
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }
}
