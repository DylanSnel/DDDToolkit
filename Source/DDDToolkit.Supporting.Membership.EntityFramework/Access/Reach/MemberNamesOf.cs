using DDDToolkit.Abstractions.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// What a reach of <typeparamref name="TResource"/> reads, read once from the model of
/// <typeparamref name="TContext"/> and kept: the names of the resource's properties, where it sits, and the
/// rows of its roles where they are kept. One for each kind of resource registered.
/// </summary>
internal sealed class MemberNamesOf<TContext, TResource, TRoleId>
    where TContext : DbContext
    where TResource : class
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private MemberNames<TResource, TRoleId>? _names;

    /// <summary>
    /// What the model of the resource's context says of it, read the first time it is needed: on a context
    /// of its own where the application registered a factory for the context, and otherwise on the context
    /// of the scope.
    /// </summary>
    /// <param name="services">The services of the scope that asks.</param>
    /// <param name="registration">The resource, as it was registered.</param>
    /// <exception cref="InvalidOperationException">The context does not map the resource as its registration and its rules say.</exception>
    public MemberNames<TResource, TRoleId> Read(IServiceProvider services, MembershipRegistration registration)
    {
        if (_names is { } known)
        {
            return known;
        }

        if (services.GetService<IDbContextFactory<TContext>>() is not { } contexts)
        {
            return _names = In(services.GetRequiredService<TContext>().Model, registration);
        }

        using var context = contexts.CreateDbContext();
        return _names = In(context.Model, registration);
    }

    private static MemberNames<TResource, TRoleId> In(Microsoft.EntityFrameworkCore.Metadata.IModel model, MembershipRegistration registration)
    {
        var rules = registration.Rules;
        var resource = model.FindEntityType(typeof(TResource))
            ?? throw new InvalidOperationException(
                typeof(TContext).Name + " does not map " + typeof(TResource).Name + ", so its members cannot be asked about there. "
                + "Register the resource with the context that maps it: Add" + typeof(TResource).Name + "Membership<TContext>(rules).");

        var mapping = MembershipModel.MembersOf(resource)
            ?? throw new InvalidOperationException(
                typeof(TContext).Name + " maps " + typeof(TResource).Name + " without its members. Map them where the context builds its model: "
                + "modelBuilder.Entity<" + typeof(TResource).Name + ">().HasMembers(resource => resource.Members, resource => resource.OwnerId).");

        if (mapping.MemberClass != registration.Member)
        {
            throw new InvalidOperationException(
                "The members of " + typeof(TResource).Name + " are of " + mapping.MemberClass.Name + ", and it was registered with the member class "
                + registration.Member.Name + ". Register each resource with its own member class.");
        }

        PlacesAbove<TResource>? above = null;
        if (rules.Above is not null)
        {
            var at = mapping.At
                ?? throw new InvalidOperationException(
                    "The rules '" + rules.Name + "' let " + typeof(TResource).Name + " be reached from above, and " + typeof(TContext).Name + " does not say where it sits. "
                    + "Say it where the context maps its members: modelBuilder.Entity<" + typeof(TResource).Name
                    + ">().HasMembers(resource => resource.Members, resource => resource.OwnerId, at: resource => resource.PlaceId).");

            // What a place is known by is the model's to say: the type of the property the resource keeps it in.
            above = (PlacesAbove<TResource>)Activator.CreateInstance(
                typeof(PlacesAbove<,,>).MakeGenericType(typeof(TResource), registration.ResourceIdType, at.ClrType),
                at.Name)!;
        }

        KeptRoles<TRoleId>? roles = null;
        if (rules.RolesKept)
        {
            var roleClass = mapping.RoleClass
                ?? throw new InvalidOperationException(
                    "The rules '" + rules.Name + "' say the roles of " + typeof(TResource).Name + " are kept, and " + typeof(TContext).Name + " maps no role class for it. "
                    + "Declare one with the role template, [KeptRole<" + typeof(TRoleId).Name + ", " + typeof(TResource).Name + ">] public sealed partial class "
                    + typeof(TResource).Name + "Role, and map it where the context builds its model: modelBuilder.Entity<" + typeof(TResource).Name + "Role>().IsKeptRole().");

            // Which class the roles are of is the model's to say, and a member holds one of them by that class's id.
            var known = roleClass.FindProperty(nameof(KeptRoleAggregate<TRoleId>.Id))!.ClrType;
            if (known != typeof(TRoleId))
            {
                throw new InvalidOperationException(
                    "The members of " + typeof(TResource).Name + " hold roles known by " + typeof(TRoleId).Name + ", and its role class " + roleClass.ClrType.Name
                    + " is known by " + known.Name + ". A member holds a role of the resource by that role's id: declare the member class with " + known.Name + " as its role.");
            }

            roles = (KeptRoles<TRoleId>)Activator.CreateInstance(typeof(KeptRoles<,>).MakeGenericType(roleClass.ClrType, typeof(TRoleId)))!;
        }

        // A query filter that reads a member of the context it runs on is the application's rule as that one
        // context knows it: whose rows a request reads, kept on the request's own context, say. A context a
        // factory makes for one reading knows nothing of it, so what such a filter guards is read on the request's own.
        var followsTheRequest = ReadsItsContext(resource) || (rules.RolesKept && mapping.RoleClass is { } kept && ReadsItsContext(kept));

        return new MemberNames<TResource, TRoleId>(mapping.Navigation.Name, mapping.Owner.Name, above, roles, followsTheRequest);
    }

    /// <summary>Whether <paramref name="entityType"/> has a query filter that reads a member of the context a query runs on.</summary>
    private static bool ReadsItsContext(Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType)
        => entityType.GetDeclaredQueryFilters().Any(filter => filter.Expression is { } expression && ContextReader.Reads(expression));

    /// <summary>
    /// Finds, in a query filter, a property, a field or a method of a context being read: what makes the filter
    /// that context's own. A context that is only handed on, as a filter does that reads what is around the
    /// work and wants to be asked again for every query, is not read.
    /// </summary>
    private sealed class ContextReader : System.Linq.Expressions.ExpressionVisitor
    {
        private bool _found;

        public static bool Reads(System.Linq.Expressions.Expression filter)
        {
            var reader = new ContextReader();
            reader.Visit(filter);
            return reader._found;
        }

        protected override System.Linq.Expressions.Expression VisitMember(System.Linq.Expressions.MemberExpression node)
        {
            _found |= IsAContext(node.Expression);
            return base.VisitMember(node);
        }

        protected override System.Linq.Expressions.Expression VisitMethodCall(System.Linq.Expressions.MethodCallExpression node)
        {
            _found |= IsAContext(node.Object);
            return base.VisitMethodCall(node);
        }

        private static bool IsAContext(System.Linq.Expressions.Expression? read) => read is not null && typeof(DbContext).IsAssignableFrom(read.Type);
    }
}
