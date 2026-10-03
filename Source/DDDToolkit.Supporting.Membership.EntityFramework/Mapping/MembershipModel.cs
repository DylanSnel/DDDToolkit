using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// What a model built with <c>HasMembers</c> says about the members of its resources, for whatever works from
/// the model: the access questions, which compose them into the context's own statements, and a package that
/// writes database functions for them.
/// <para>
/// A model may map several kinds of resource with members, each marked on its own entity type, so every
/// question here is asked about one resource or one member class.
/// </para>
/// </summary>
public static class MembershipModel
{
    /// <summary>
    /// The annotation <c>HasMembers</c> leaves on a resource's entity type: the name of the property that
    /// holds its members.
    /// </summary>
    public const string MembersAnnotation = "DDDToolkit:Membership:Members";

    /// <summary>
    /// The annotation <c>HasMembers</c> leaves on a resource's entity type: the name of the property that
    /// holds its owner.
    /// </summary>
    public const string OwnerAnnotation = "DDDToolkit:Membership:Owner";

    /// <summary>
    /// The annotation <c>HasMembers</c> leaves on the entity type of a resource that says where it sits: the
    /// name of the property that holds the place.
    /// </summary>
    public const string AtAnnotation = "DDDToolkit:Membership:At";

    /// <summary>
    /// The annotation <c>IsKeptRole</c> leaves on the entity type of a role class: the full name of the
    /// resource the roles are of, as its template names it.
    /// </summary>
    public const string RoleOfAnnotation = "DDDToolkit:Membership:RoleOf";

    /// <summary>
    /// The members of <paramref name="resource"/>, as <c>HasMembers</c> mapped them, or <see langword="null"/>
    /// for an entity type it was not called on.
    /// </summary>
    /// <param name="resource">An entity type of the model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The entity type is marked, and the model no longer has what the mark names: the mapping was changed
    /// after <c>HasMembers</c>. Or the resource was given another key after its members were mapped, which
    /// does not reach their tables. Or the model maps two role classes for the resource, or a role class
    /// without what <c>IsKeptRole</c> maps of it.
    /// </exception>
    public static MemberMapping? MembersOf(IEntityType resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.FindAnnotation(MembersAnnotation)?.Value is not string members
            || resource.FindAnnotation(OwnerAnnotation)?.Value is not string owner)
        {
            return null;
        }

        if (resource.FindNavigation(members) is not { IsCollection: true, ForeignKey.IsOwnership: true } navigation
            || resource.FindProperty(owner) is not { } property
            || navigation.TargetEntityType.FindNavigation(nameof(MemberEntity<,,>.Roles)) is not { IsCollection: true, ForeignKey.IsOwnership: true } roles)
        {
            throw new InvalidOperationException(
                resource.DisplayName() + " was mapped with HasMembers, and the model no longer has its members in " + members
                + ", the roles they hold, or its owner in " + owner + ". Map the members with HasMembers alone: it maps both tables.");
        }

        // The member tables are kept under the key the resource had when they were mapped. A key the resource
        // was given afterwards is not theirs: the rows would point at the resource by one key and be told
        // apart by another, with nothing saying so.
        var toResource = navigation.ForeignKey;
        if (!toResource.PrincipalKey.IsPrimaryKey()
            || navigation.TargetEntityType.FindPrimaryKey() is not { } memberKey
            || !memberKey.Properties.Take(toResource.Properties.Count).SequenceEqual(toResource.Properties))
        {
            throw new InvalidOperationException(
                resource.DisplayName() + " was keyed after its members were mapped: its member tables are kept under the key it had then, and the key it has now does not reach them. "
                + "Say the key first: call HasKey before HasMembers. A part of the key declared on the class with [KeyPart] needs nothing said.");
        }

        IProperty? place = null;
        if (resource.FindAnnotation(AtAnnotation)?.Value is string at)
        {
            place = resource.FindProperty(at)
                ?? throw new InvalidOperationException(
                    resource.DisplayName() + " was mapped with HasMembers as sitting at " + at + ", and the model no longer has that property. "
                    + "Say where the resource sits with HasMembers alone: at: resource => resource." + at + ".");
        }

        return new MemberMapping(resource, navigation, property, navigation.TargetEntityType, roles.TargetEntityType, place, RoleClassOf(resource));
    }

    /// <summary>
    /// The role class the model maps for <paramref name="resource"/> with <c>IsKeptRole</c>, or
    /// <see langword="null"/>: found by the resource its template names, so a model with several kinds of
    /// resource has each one's roles apart.
    /// </summary>
    private static IEntityType? RoleClassOf(IEntityType resource)
    {
        IEntityType? found = null;
        foreach (var entityType in resource.Model.GetEntityTypes())
        {
            if (entityType.FindAnnotation(RoleOfAnnotation)?.Value is not string of || !string.Equals(of, resource.ClrType.FullName, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null)
            {
                throw new InvalidOperationException(
                    resource.DisplayName() + " has two role classes in this model, " + found.DisplayName() + " and " + entityType.DisplayName()
                    + ". A resource has one: a member holds a role of it by its id, and that id is one class's.");
            }

            if (entityType.FindProperty(nameof(KeptRoleAggregate<>.Keys)) is null
                || entityType.FindProperty(nameof(KeptRoleAggregate<>.Status)) is null
                || entityType.FindProperty(nameof(KeptRoleAggregate<>.Id)) is null)
            {
                throw new InvalidOperationException(
                    entityType.DisplayName() + " was mapped with IsKeptRole as the role class of " + resource.DisplayName()
                    + ", and the model no longer has its keys, its status or its id. Map the role class with IsKeptRole alone: it maps all three.");
            }

            found = entityType;
        }

        return found;
    }

    /// <summary>
    /// The resource whose members are of <paramref name="memberClass"/>, with those members, or
    /// <see langword="null"/> when <paramref name="model"/> maps no such resource: a context of another module.
    /// A member class belongs to one kind of resource, so there is one answer.
    /// </summary>
    /// <param name="model">The model of a context.</param>
    /// <param name="memberClass">The application's member class, declared with the member template.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="memberClass"/> is null.</exception>
    public static MemberMapping? Of(IModel model, Type memberClass)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(memberClass);

        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.ClrType == memberClass
                && entityType.FindOwnership() is { } ownership
                && MembersOf(ownership.PrincipalEntityType) is { } mapping
                && mapping.Members == entityType)
            {
                return mapping;
            }
        }

        return null;
    }
}
