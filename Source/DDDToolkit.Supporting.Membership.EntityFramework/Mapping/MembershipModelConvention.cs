using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// Maps the members of a resource, and the role class of a resource whose roles a customer makes, with no call in the
/// context: a context that calls <c>AddDDDToolkitConventions()</c> gets them as it gets the toolkit's conventions for
/// ordinary aggregates.
/// <list type="bullet">
/// <item>A resource's collection of a class declared with the member template, <c>[Member&lt;BoardMemberId, SeatId,
/// BoardRoleId, Board&gt;]</c>, is mapped as <c>HasMembers(board =&gt; board.People)</c> maps it, with the tables named
/// after the resource and the member class.</item>
/// <item>A class declared with the role template, <c>[CustomRole&lt;BoardRoleId, Board&gt;]</c>, is mapped as
/// <c>IsCustomRole()</c> maps it.</item>
/// </list>
/// <para>
/// It runs when the model is finished, after <c>OnModelCreating</c>, and maps only what the context did not map by hand.
/// A resource whose members the context mapped with <c>HasMembers</c>, under table names of its own say, keeps what the
/// context said, and so does a role class it mapped with <c>IsCustomRole</c>. A collection the context ignores stays
/// ignored.
/// </para>
/// <para>
/// The context names the package by nothing. The two templates name this convention (<c>[ModelConvention]</c>), and the
/// toolkit's <c>MarkerConvention</c> runs it in every model that maps a class declared with one of them. A model with
/// such a class and without this package in the application is refused while it is built. The convention is found from
/// the classes the model maps, so <c>dotnet ef</c> builds the model the application runs with.
/// </para>
/// </summary>
internal sealed class MembershipModelConvention : IModelFinalizingConvention
{
    private static readonly MethodInfo MembersMethod =
        typeof(MembershipModelConvention).GetMethod(nameof(MapMembers), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo RoleMethod =
        typeof(MembershipModelConvention).GetMethod(nameof(MapRole), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var model = (IMutableModel)modelBuilder.Metadata;
        foreach (var entityType in model.GetEntityTypes().Where(static entityType => !entityType.IsOwned() && !entityType.HasSharedClrType).ToList())
        {
            // Mapping one resource's members turns the classes the model found on its own into owned ones: those are gone.
            if (!ReferenceEquals(model.FindEntityType(entityType.Name), entityType))
            {
                continue;
            }

            var resource = entityType.ClrType;

            // A collection the context ignores by hand stays ignored: what it says wins, whatever it says.
            if (entityType.FindAnnotation(MembershipModel.MembersAnnotation) is null && MembersOf(resource) is { } members && !entityType.IsIgnored(members.Name))
            {
                Invoke(MembersMethod.MakeGenericMethod(resource, ElementOf(members.PropertyType)!), entityType, members);
            }

            if (entityType.FindAnnotation(MembershipModel.RoleOfAnnotation) is null && IsRoleClass(resource))
            {
                Invoke(RoleMethod.MakeGenericMethod(resource), entityType);
            }
        }
    }

    /// <summary>Maps a resource's members as <c>HasMembers</c> does, from the collection property the resource holds them in.</summary>
    private static void MapMembers<TResource, TMember>(IMutableEntityType entityType, PropertyInfo collection)
        where TResource : class
        where TMember : class
    {
        var resource = Expression.Parameter(typeof(TResource), "resource");
        var members = Expression.Lambda<Func<TResource, IEnumerable<TMember>?>>(Expression.Property(resource, collection), resource);

        Builder<TResource>(entityType).HasMembers(members);
    }

    /// <summary>Maps a role class as <c>IsCustomRole</c> does.</summary>
    private static void MapRole<TRole>(IMutableEntityType entityType)
        where TRole : class
        => Builder<TRole>(entityType).IsCustomRole();

    /// <summary>
    /// The builder the context's own <c>modelBuilder.Entity&lt;T&gt;()</c> hands out, over an entity type of the model: what
    /// makes the mapping here the very one a call in the context makes. Entity Framework marks the constructor as its
    /// internal API (EF1001); it is the one way to that builder from a convention.
    /// </summary>
    private static EntityTypeBuilder<T> Builder<T>(IMutableEntityType entityType)
        where T : class
    {
#pragma warning disable EF1001 // Internal EF Core API usage.
        return new EntityTypeBuilder<T>(entityType);
#pragma warning restore EF1001
    }

    /// <summary>Calls a mapping, and lets what it throws out as it threw it, not wrapped in how it was called.</summary>
    private static void Invoke(MethodInfo mapping, params object[] arguments)
    {
        try
        {
            mapping.Invoke(null, arguments);
        }
        catch (TargetInvocationException failed) when (failed.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
        }
    }

    /// <summary>
    /// The property of <paramref name="resource"/> that holds its members: a collection of a member class whose template
    /// names this resource. <see langword="null"/> for a class that holds none.
    /// </summary>
    /// <exception cref="InvalidOperationException">It holds two: a resource has one member list.</exception>
    private static PropertyInfo? MembersOf(Type resource)
    {
        // The member manager's way to the members, which the generator writes as an explicit implementation, is the same
        // collection under the interface's name: it is passed over.
        var found = resource.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => !property.Name.Contains('.', StringComparison.Ordinal) && ElementOf(property.PropertyType) is { } member && MemberClassOf(member) == resource)
            .ToList();

        return found.Count switch
        {
            0 => null,
            1 => found[0],
            _ => throw new InvalidOperationException(
                resource.Name + " holds two collections of its member class, " + found[0].Name + " and " + found[1].Name + ". A resource has one member list; "
                + "keep one, or map it by hand: modelBuilder.Entity<" + resource.Name + ">().HasMembers(row => row." + found[0].Name + ")."),
        };
    }

    /// <summary>What <paramref name="collection"/> is a collection of, or <see langword="null"/> for a type that is no collection.</summary>
    private static Type? ElementOf(Type collection)
    {
        if (collection == typeof(string))
        {
            return null;
        }

        var enumerable = collection.IsGenericType && collection.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? collection
            : collection.GetInterfaces().FirstOrDefault(static implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerable?.GenericTypeArguments[0];
    }

    /// <summary>The resource a member class's template names, or <see langword="null"/> for a class that is no member class.</summary>
    private static Type? MemberClassOf(Type member)
        => member.IsClass && !member.IsAbstract
            ? member.GetCustomAttributes(inherit: false)
                .Select(static attribute => attribute.GetType())
                .FirstOrDefault(static attribute => attribute.IsGenericType && attribute.GetGenericTypeDefinition() == typeof(MemberAttribute<,,,>))
                ?.GenericTypeArguments[3]
            : null;

    /// <summary>Whether <paramref name="type"/> is declared with the role template.</summary>
    private static bool IsRoleClass(Type type)
        => type.GetCustomAttributes(inherit: false)
            .Select(static attribute => attribute.GetType())
            .Any(static attribute => attribute.IsGenericType && attribute.GetGenericTypeDefinition() == typeof(CustomRoleAttribute<,>));
}
