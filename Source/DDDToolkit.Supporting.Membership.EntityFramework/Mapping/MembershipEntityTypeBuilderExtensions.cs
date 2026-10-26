using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>Maps the members of a resource, and the roles a customer makes for one, into the model of the application's own context.</summary>
public static class MembershipEntityTypeBuilderExtensions
{
    /// <summary>The longest name of a status, with room to spare: how wide the column of a role's status is.</summary>
    private const int StatusLength = 16;

    private static readonly MethodInfo MapMethod =
        typeof(MembershipEntityTypeBuilderExtensions).GetMethod(nameof(Map), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo MapRoleClassMethod =
        typeof(MembershipEntityTypeBuilderExtensions).GetMethod(nameof(MapRoleClass), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ReadKeyPartsMethod =
        typeof(MembershipEntityTypeBuilderExtensions).GetMethod(nameof(ReadKeyParts), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Maps the members of a resource: the members in a table of their own, owned by the resource, and the
    /// roles they hold in a second one, owned by the member. Call it where the context maps the resource:
    /// <code>
    /// modelBuilder.Entity&lt;Document&gt;().HasMembers(document =&gt; document.Shares);
    /// </code>
    /// The resource stays the application's aggregate, mapped as it always was; this adds its members to it,
    /// so they are loaded and saved with it, under its one version. Nothing is written by hand for the member
    /// class: the member, its periods and its roles are the package's, and what the application added to its
    /// class is mapped by the conventions, as on any entity.
    /// <para>
    /// What the resource says of itself in the domain is read off its class: its owner, the property it marks
    /// <c>[Owner]</c>, of what a member is known by, and where it hangs, the property it marks <c>[At]</c>, where it has
    /// one. Both become columns of the resource whatever else the application says about them. Where it hangs is what
    /// a permission held from above is compared with: a resource whose rules let it be reached from above
    /// (<see cref="Access.MembershipRules.ReachFromAbove"/>) is held by a caller that holds the permission at that place, or
    /// above it, as the application answers where that is (<see cref="IPlacesReached{TResourceId, TPlaceId}"/>). The
    /// package keeps no places: it reads that column, and nothing of what is above it.
    /// </para>
    /// <para>
    /// The type arguments are inferred from the collection. The member class's own id, what a member is known by and
    /// what its roles are known by are read from the class when the model is built.
    /// </para>
    /// <para>
    /// What it maps:
    /// </para>
    /// <list type="bullet">
    /// <item>The members' table, keyed by the resource and the member row's id, with an index on who the
    /// member is: "which resources am I a member of" is what every list asks. Who the member is is fixed once
    /// the row is there (<c>IsFixedAfterInsert</c>): a save that changed it is refused, and the exported
    /// privileges leave the column out of UPDATE.</item>
    /// <item>The roles' table, keyed by the resource, the member row and the role, so a member holds a role
    /// once in the database as well.</item>
    /// <item>Both under the resource's whole key: what the resource is keyed by when this is called, and in
    /// front of it the parts the resource declares with <c>[KeyPart]</c>, which join its key when the model
    /// is finished. A key given with <c>HasKey</c> is said before this call: one said afterwards does not
    /// reach the member tables, and is refused where the mapping is read.</item>
    /// <item>Every moment of a period as its UTC instant, so a database that keeps no offsets compares a
    /// period in SQL.</item>
    /// <item>A role known by its name (<see cref="NamedRole"/>) as that text.</item>
    /// </list>
    /// <para>
    /// The ids are stored by the application's generated converters, like the ids of its other entities:
    /// register them and <c>AddDDDToolkitConventions()</c> in <c>ConfigureConventions</c>. Tables and the
    /// member's column take the names of <paramref name="names"/>, and the tables are in the schema of the
    /// resource's own table, as it is when this is called; everything else is named as the context names
    /// things.
    /// </para>
    /// <para>
    /// It leaves a mark on the resource's entity type (<see cref="MembershipModel"/>), which is how the access
    /// questions registered for the resource, and a package that writes database functions, find the
    /// members of this resource and of no other. An application with several kinds of resource calls it on
    /// each.
    /// </para>
    /// </summary>
    /// <param name="resource">The builder of the resource's entity type.</param>
    /// <param name="members">The resource's collection of members.</param>
    /// <param name="names">The names of the two tables and of the member's column; named after the resource and the member class when left out.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TMember">The application's member class, declared with the member template.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> or <paramref name="members"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TMember"/> was not declared with the member template; the resource marks no property
    /// <c>[Owner]</c>, or one that is not of what the members are known by; it marks two properties <c>[At]</c>, or a
    /// nullable one; or <paramref name="members"/> is not a property of the resource.
    /// </exception>
    /// <exception cref="InvalidOperationException">The resource has members already: it has one member list.</exception>
    public static EntityTypeBuilder<TResource> HasMembers<TResource, TMember>(
        this EntityTypeBuilder<TResource> resource,
        Expression<Func<TResource, IEnumerable<TMember>?>> members,
        MemberTableNames? names = null)
        where TResource : class
        where TMember : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(members);

        var parent = ParentOf(typeof(TMember))
            ?? throw new ArgumentException(
                typeof(TMember).Name + " is not a member class. Declare it with the member template, [Member<" + typeof(TMember).Name + "Id, UserId, NamedRole, "
                + typeof(TResource).Name + ">] public sealed partial class " + typeof(TMember).Name + ";, and the generator derives it from the package's member.",
                nameof(members));

        var arguments = parent.GetGenericArguments();
        var owner = Marked<OwnerAttribute>(typeof(TResource), "Owner");
        if (owner is null)
        {
            throw new ArgumentException(
                typeof(TResource).Name + " marks no property [Owner]. A resource with members has one owner, a member: mark the property of " + arguments[1].Name
                + " that holds it, [Owner] public " + arguments[1].Name + " OwnerId { get; private set; }.",
                nameof(resource));
        }

        if (owner.PropertyType != arguments[1])
        {
            throw new ArgumentException(
                "The owner of " + typeof(TResource).Name + ", " + owner.Name + ", is a " + owner.PropertyType.Name + ", and its members are known by a " + arguments[1].Name
                + ". An owner is one of the members: keep it as what a member is known by.",
                nameof(resource));
        }

        var at = Marked<AtAttribute>(typeof(TResource), "At");
        if (at is not null && Nullable.GetUnderlyingType(at.PropertyType) is not null)
        {
            throw new ArgumentException(
                typeof(TResource).Name + "." + at.Name + " is marked [At] and may be empty: a resource that hangs nowhere is reached from above by nobody. "
                + "Mark the property of where it always hangs.",
                nameof(resource));
        }

        if (resource.Metadata.FindAnnotation(MembershipModel.MembersAnnotation) is not null)
        {
            throw new InvalidOperationException(
                typeof(TResource).Name + " has members already, in " + resource.Metadata.FindAnnotation(MembershipModel.MembersAnnotation)!.Value
                + ". A resource has one member list; call HasMembers on it once.");
        }

        var navigation = PropertyOf(members, nameof(members));

        try
        {
            MapMethod.MakeGenericMethod(typeof(TResource), typeof(TMember), arguments[0], arguments[1], arguments[2])
                .Invoke(null, [resource, members, names ?? new MemberTableNames(), navigation]);
        }
        catch (TargetInvocationException failed) when (failed.InnerException is { } inner)
        {
            // What Entity Framework says about the mapping, as it said it, and not wrapped in how it was called.
            ExceptionDispatchInfo.Capture(inner).Throw();
        }

        // The owner is a column of the resource, whatever else the application says about it; so is where it hangs.
        resource.Property(owner.PropertyType, owner.Name);
        resource.HasAnnotation(MembershipModel.MembersAnnotation, navigation);
        resource.HasAnnotation(MembershipModel.OwnerAnnotation, owner.Name);
        if (at is not null)
        {
            resource.Property(at.PropertyType, at.Name);
            resource.HasAnnotation(MembershipModel.AtAnnotation, at.Name);
        }

        return resource;
    }

    /// <summary>
    /// The one property of <paramref name="type"/>, or of a class it derives from, marked with
    /// <typeparamref name="TAttribute"/>, or <see langword="null"/> where none is.
    /// </summary>
    /// <exception cref="ArgumentException">Two are.</exception>
    private static PropertyInfo? Marked<TAttribute>(Type type, string what)
        where TAttribute : Attribute
    {
        var marked = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.GetCustomAttribute<TAttribute>(inherit: true) is not null)
            .ToList();
        return marked.Count <= 1
            ? marked.FirstOrDefault()
            : throw new ArgumentException(
                type.Name + " marks two properties [" + what + "], " + marked[0].Name + " and " + marked[1].Name + ". It has one; mark the one.",
                "resource");
    }

    /// <summary>
    /// Maps the role class of a resource whose roles a customer makes: the application's class declared with the role
    /// template, in a table of its own. Call it where the context maps that class:
    /// <code>
    /// modelBuilder.Entity&lt;PlotRole&gt;(role =&gt;
    /// {
    ///     role.IsCustomRole();
    ///
    ///     // The application's own: whose a role is, that a name is used once there, and that a starter role is made once there
    ///     role.HasIndex(row =&gt; new { row.GardenId, row.Name }).IsUnique().RefusesAs("plots.role-name-taken", "Another role is called {Name} already.");
    ///     role.HasIndex(row =&gt; new { row.GardenId, row.MadeFrom }).IsUnique();
    /// });
    /// </code>
    /// The role class stays the application's aggregate: its table, its other columns, its indexes and the
    /// rule that keeps one customer's roles from another's are the application's to say, as for any aggregate.
    /// <para>
    /// What it maps is the package's part of a role: the id, which the application makes; the name, the
    /// description and the starter role it was made from, each no longer than the role allows; the status as
    /// its name; and the permissions as a collection of texts in one column, which is what the access questions and
    /// the database's functions read. The id is stored by the application's generated converter, like the ids
    /// of its other entities. The starter role a role was made from is fixed once the row is there
    /// (<c>IsFixedAfterInsert</c>): the starter roles a customer lacks are found by it, so a save that changed it is refused, and
    /// the privileges a Postgres export writes leave the column out of what a caller may update.
    /// </para>
    /// <para>
    /// It leaves a mark on the role's entity type (<see cref="MembershipModel"/>) that names the resource the
    /// roles are of, as the class's template names it. That is how the access questions registered for the
    /// resource, and a package that writes database functions, find the roles of this resource and of no
    /// other: nothing else has to be registered for them. A uniqueness the application wants, a name used
    /// once among one customer's roles, is an index it declares with its own columns, as above; the package
    /// knows of no such column.
    /// </para>
    /// </summary>
    /// <param name="role">The builder of the role class's entity type.</param>
    /// <typeparam name="TRole">The application's role class, declared with the role template.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="role"/> is null.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TRole"/> was not declared with the role template.</exception>
    public static EntityTypeBuilder<TRole> IsCustomRole<TRole>(this EntityTypeBuilder<TRole> role)
        where TRole : class
    {
        ArgumentNullException.ThrowIfNull(role);

        var parent = RoleParentOf(typeof(TRole));
        var resource = typeof(TRole).GetCustomAttributes(inherit: false)
            .Select(attribute => attribute.GetType())
            .FirstOrDefault(attribute => attribute.IsGenericType && attribute.GetGenericTypeDefinition() == typeof(CustomRoleAttribute<,>))
            ?.GenericTypeArguments[1];
        if (parent is null || resource is null)
        {
            throw new ArgumentException(
                typeof(TRole).Name + " is not a role class. Declare it with the role template, naming the resource its roles are of, [CustomRole<"
                + typeof(TRole).Name + "Id, Document>] public sealed partial class " + typeof(TRole).Name + " for the roles of a Document, and the generator derives it from the package's role.",
                nameof(role));
        }

        try
        {
            MapRoleClassMethod.MakeGenericMethod(typeof(TRole), parent.GenericTypeArguments[0]).Invoke(null, [role]);
        }
        catch (TargetInvocationException failed) when (failed.InnerException is { } inner)
        {
            // What Entity Framework says about the mapping, as it said it, and not wrapped in how it was called.
            ExceptionDispatchInfo.Capture(inner).Throw();
        }

        role.HasAnnotation(MembershipModel.RoleOfAnnotation, resource.FullName);

        return role;
    }

    /// <summary>The package's part of a role, with the role's id known.</summary>
    private static void MapRoleClass<TRole, TRoleId>(EntityTypeBuilder<TRole> role)
        where TRole : CustomRoleAggregate<TRoleId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        role.Property(row => row.Id).ValueGeneratedNever();
        role.Property(row => row.Name).HasMaxLength(CustomRoleAggregate<TRoleId>.MaxNameLength);
        role.Property(row => row.Description).HasMaxLength(CustomRoleAggregate<TRoleId>.MaxDescriptionLength);

        // A starter role is one of the roles the rules declare, and those are named as a role is held by name.
        // Where a role came from is how the starter roles still to make are found, so it is said once, when the row is added:
        // a save that changed it is refused, and the privileges the Postgres export writes leave it out of what
        // a caller may update.
        role.Property(row => row.MadeFrom).HasMaxLength(NamedRole.MaxLength).IsFixedAfterInsert();

        // As its name, so a row says what it is to whoever reads the table, and a function compares it with a word.
        role.Property(row => row.Status).HasConversion<string>().HasMaxLength(StatusLength);

        // One column: an array where the database has arrays, a JSON text where it has none. Read inside a
        // statement either way, which is what lets "the roles that give this permission" stay a subquery.
        role.PrimitiveCollection(row => row.Permissions);
    }

    /// <summary>The package's role, closed over the id <paramref name="roleClass"/> was declared with, or <see langword="null"/>.</summary>
    private static Type? RoleParentOf(Type roleClass)
    {
        for (var type = roleClass.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CustomRoleAggregate<>))
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>The two tables, with every type of the member class known.</summary>
    private static void Map<TResource, TMember, TId, TMemberId, TRoleId>(
        EntityTypeBuilder<TResource> resource,
        Expression<Func<TResource, IEnumerable<TMember>?>> members,
        MemberTableNames names,
        string navigation)
        where TResource : class
        where TMember : MemberEntity<TId, TMemberId, TRoleId>
        where TId : struct, IEntityId, IEquatable<TId>
        where TMemberId : struct, IEntityId, IEquatable<TMemberId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        // Next to the resource: in the schema the application gave the resource's table, when it gave it one.
        var schema = resource.Metadata.GetSchema();

        resource.OwnsMany(members, member =>
        {
            member.ToTable(names.Members ?? typeof(TResource).Name + navigation, schema);

            // A member row is known by its resource and its own id, and that is what a role's row carries: the
            // resource under the name the member's table has for it, and the member row under the class's name.
            var ownership = member.WithOwner();
            string[] toResource = [.. ownership.Metadata.Properties.Select(property => property.Name)];

            // The parts a resource declares for its key join that key only when the model is finished, after
            // this. They are the resource's own declaration, so they are known now: a member's row carries
            // each under its own name, in front, and points at the resource by the whole key it will have.
            // Left to themselves, the two tables would stay under the key the resource has at this moment.
            string[] keyNow = [.. resource.Metadata.FindPrimaryKey()!.Properties.Select(property => property.Name)];
            string[] parts = [.. KeyPartsOf(typeof(TResource)).Where(part => !keyNow.Contains(part, StringComparer.Ordinal))];
            if (parts.Length > 0)
            {
                foreach (var part in parts)
                {
                    var declared = typeof(TResource).GetProperty(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException(typeof(TResource).Name + " names '" + part + "' as a key part, and has no such property.");

                    // A key part is declared without a setter, which Entity Framework maps only when told to.
                    resource.Property(declared.PropertyType, part);
                    member.Property(declared.PropertyType, part);
                }

                toResource = [.. parts, .. toResource];
                ownership.HasForeignKey(toResource).HasPrincipalKey([.. parts, .. keyNow]);
            }

            var toMember = typeof(TMember).Name + "Id";
            member.HasKey([.. toResource, nameof(MemberEntity<TId, TMemberId, TRoleId>.Id)]);
            member.Property(row => row.Id).ValueGeneratedNever();

            // Who a membership is of is written once: the member list adds a member and never moves a row to
            // another, so a save that changed it is refused, and the exported privileges leave the column out of
            // UPDATE. A row moved to another member would hand it the roles and periods it holds, and keep who
            // added it and gave them.
            var known = member.Property(row => row.MemberId).IsFixedAfterInsert();
            if (names.MemberColumn is { } column)
            {
                known.HasColumnName(column);
            }

            // "Which resources am I a member of" is asked for every list, by the member.
            member.HasIndex(row => row.MemberId);
            member.Property(row => row.StartsAt).HasConversion(new UtcDateTimeOffsetConverter());
            member.Property(row => row.EndsAt).HasConversion(new NullableUtcDateTimeOffsetConverter());

            member.OwnsMany(row => row.Roles, role =>
            {
                role.ToTable(names.Roles ?? typeof(TMember).Name + "Roles", schema);
                role.WithOwner().HasForeignKey([.. toResource, toMember]);

                // A member holds a role once. The member list refuses a second hold first, by its code, and two
                // changes to one resource are kept apart by the resource's version; the key holds whatever went
                // round both.
                role.HasKey([.. toResource, toMember, nameof(MemberRole<TMemberId, TRoleId>.RoleId)]);
                role.Property(held => held.StartsAt).HasConversion(new UtcDateTimeOffsetConverter());
                role.Property(held => held.EndsAt).HasConversion(new NullableUtcDateTimeOffsetConverter());

                // The package's own id: no registration of the application's stores it, so it is stored here.
                if (typeof(TRoleId) == typeof(NamedRole))
                {
                    role.Property(held => held.RoleId).HasConversion(new SingleValueConverter<NamedRole, string>()).HasMaxLength(NamedRole.MaxLength);
                }
            });

            // The roles are a plain field behind a read-only view, which Entity Framework fills directly.
            member.Navigation(row => row.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }

    /// <summary>
    /// The names of the properties <paramref name="resource"/> declares as parts of its key, in the order they
    /// join it, in front of its id; none for a resource that declares none.
    /// </summary>
    private static IReadOnlyList<string> KeyPartsOf(Type resource)
        => typeof(IHasKeyParts).IsAssignableFrom(resource)
            ? (IReadOnlyList<string>)ReadKeyPartsMethod.MakeGenericMethod(resource).Invoke(null, null)!
            : [];

    private static IReadOnlyList<string> ReadKeyParts<T>()
        where T : IHasKeyParts
        => T.KeyParts;

    /// <summary>The package's member, closed over the three types <paramref name="memberClass"/> was declared with, or <see langword="null"/>.</summary>
    private static Type? ParentOf(Type memberClass)
    {
        for (var type = memberClass.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(MemberEntity<,,>))
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>The name of the property <paramref name="access"/> reads.</summary>
    /// <exception cref="ArgumentException">It reads something else.</exception>
    private static string PropertyOf(LambdaExpression access, string parameter)
    {
        try
        {
            return access.GetMemberAccess().Name;
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("'" + access + "' is not a property of the resource. Name the property itself: resource => resource.Members.", parameter);
        }
    }
}
