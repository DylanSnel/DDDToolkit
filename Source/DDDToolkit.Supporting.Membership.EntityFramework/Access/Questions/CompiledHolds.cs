using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The statements of the access checks of one kind of resource, compiled once for each shape they take with
/// <c>EF.CompileAsyncQuery</c>. A check then sends its values as parameters, and Entity Framework neither builds the
/// statement again nor looks it up. One for each kind of resource registered.
/// </summary>
/// <remarks>
/// <para>
/// A statement's shape follows from the rules and the permission, never from the caller: how the caller can hold the
/// permission (<see cref="MemberHeldBy"/>), how it sees the resource, whether it is the application's own work, and the
/// roles the rules declare for the permission. Only a statement whose reaches compare with values alone is compiled
/// (<see cref="EfMemberReach{TResource, TResourceId, TMember, TId, TMemberId, TRoleId}.ComparesValuesOnly"/>). One with
/// roles kept as rows, or with a hold from above, puts a query of the application's into the statement, and is composed
/// for each check as before.
/// </para>
/// <para>
/// The statement is the one the reaches compose for any check. They compose it once over a stand-in for the rows. The
/// stand-in is then replaced by the resource's set of the context. The caller and the moment become parameters of the
/// compiled query. The roles that give the permission are the same for every caller, so they become constants of the
/// statement: Entity Framework 10 sends a list as one parameter per element by default, and on SQLite that cost about
/// 100 KB for each execution of a statement with roles. The query filters apply to the compiled query as to any other.
/// </para>
/// </remarks>
internal sealed class CompiledHolds<TContext, TResource, TResourceId, TMember, TId, TMemberId, TRoleId>
    where TContext : DbContext
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private static readonly MethodInfo FirstOrDefaultMethod = new Func<IQueryable<HeldRow<TResourceId>>, HeldRow<TResourceId>?>(Queryable.FirstOrDefault).Method;

    private readonly ConcurrentDictionary<Shape, Func<TContext, TResourceId, TMemberId, DateTimeOffset, CancellationToken, Task<HeldRow<TResourceId>?>>> _one = new();

    private readonly ConcurrentDictionary<Shape, Func<TContext, List<TResourceId>, TMemberId, DateTimeOffset, IAsyncEnumerable<HeldRow<TResourceId>>>> _each = new();

    /// <summary>
    /// Whether the statement of <paramref name="act"/> over what <paramref name="see"/> sees is compiled: both reaches compare
    /// with values alone, and the reach that sees asks for no role, so the roles of the statement are those of the permission.
    /// </summary>
    /// <param name="act">The reach for the permission.</param>
    /// <param name="see">The reach of what the caller sees, made for the same caller at the same moment.</param>
    public static bool Compiles(
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> act,
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> see)
        => act.ComparesValuesOnly && see.ComparesValuesOnly && !see.HeldBy.HasFlag(MemberHeldBy.Roles);

    /// <summary>
    /// How the caller holds the permission of <paramref name="act"/> on <paramref name="resource"/>, where
    /// <paramref name="see"/> lets it see the resource, and until when; <see langword="null"/> where it does not see it.
    /// </summary>
    /// <param name="context">The context the statement runs on.</param>
    /// <param name="resource">The resource.</param>
    /// <param name="act">The reach for the permission, which compares with values alone.</param>
    /// <param name="see">The reach of what the caller sees, made for the same caller at the same moment, which compares with values alone.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    public Task<HeldRow<TResourceId>?> HoldAsync(
        TContext context,
        TResourceId resource,
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> act,
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> see,
        CancellationToken cancellationToken)
    {
        var read = _one.GetOrAdd(Shape.Of(act, see), static (_, reaches) => CompileOne(reaches.Act, reaches.See), (Act: act, See: see));
        return read(context, resource, act.Member, act.Now, cancellationToken);
    }

    /// <summary>
    /// <see cref="HoldAsync"/> over every resource of <paramref name="resources"/> at once: one row for each the caller sees.
    /// </summary>
    /// <param name="context">The context the statement runs on.</param>
    /// <param name="resources">The resources, each once.</param>
    /// <param name="act">The reach for the permission, which compares with values alone.</param>
    /// <param name="see">The reach of what the caller sees, made for the same caller at the same moment, which compares with values alone.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    public async Task<List<HeldRow<TResourceId>>> HoldEachAsync(
        TContext context,
        List<TResourceId> resources,
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> act,
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> see,
        CancellationToken cancellationToken)
    {
        var read = _each.GetOrAdd(Shape.Of(act, see), static (_, reaches) => CompileEach(reaches.Act, reaches.See), (Act: act, See: see));
        var rows = new List<HeldRow<TResourceId>>();
        await foreach (var row in read(context, resources, act.Member, act.Now).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(row);
        }

        return rows;
    }

    private static Func<TContext, TResourceId, TMemberId, DateTimeOffset, CancellationToken, Task<HeldRow<TResourceId>?>> CompileOne(
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> act,
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> see)
    {
        // The row by its id, as HoldAsync reads it.
        Expression<Func<TContext, TResourceId, IQueryable<TResource>>> rows =
            (context, resource) => context.Set<TResource>().AsNoTracking().Where(candidate => candidate.Id.Equals(resource));

        var values = new Values(act.DeclaredRoles);
        var held = Composed(rows.Body, values, standIn => act.HeldWhereSeen(standIn, null, see)
            .Select(found => new HeldRow<TResourceId> { Version = found.Resource.Version, AsMember = found.AsMember, FromAbove = found.FromAbove, Until = found.Until }));

        var token = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        return EF.CompileAsyncQuery(Expression.Lambda<Func<TContext, TResourceId, TMemberId, DateTimeOffset, CancellationToken, HeldRow<TResourceId>?>>(
            Expression.Call(FirstOrDefaultMethod, held),
            rows.Parameters[0], rows.Parameters[1], values.Member, values.Now, token));
    }

    private static Func<TContext, List<TResourceId>, TMemberId, DateTimeOffset, IAsyncEnumerable<HeldRow<TResourceId>>> CompileEach(
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> act,
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> see)
    {
        // The rows by their ids, as HoldEachAsync reads them.
        Expression<Func<TContext, List<TResourceId>, IQueryable<TResource>>> rows =
            (context, about) => context.Set<TResource>().AsNoTracking().Where(candidate => about.Contains(candidate.Id));

        var values = new Values(act.DeclaredRoles);
        var held = Composed(rows.Body, values, standIn => act.HeldWhereSeen(standIn, null, see)
            .Select(found => new HeldRow<TResourceId> { Id = found.Resource.Id, Version = found.Resource.Version, AsMember = found.AsMember, FromAbove = found.FromAbove, Until = found.Until }));

        return EF.CompileAsyncQuery(Expression.Lambda<Func<TContext, List<TResourceId>, TMemberId, DateTimeOffset, IQueryable<HeldRow<TResourceId>>>>(
            held, rows.Parameters[0], rows.Parameters[1], values.Member, values.Now));
    }

    /// <summary>
    /// The statement <paramref name="compose"/> makes, over <paramref name="rows"/> instead of the stand-in it is composed
    /// over, with the parameters and constants of <paramref name="values"/> in the place of the values its conditions compare with.
    /// </summary>
    private static Expression Composed(Expression rows, Values values, Func<IQueryable<TResource>, IQueryable<HeldRow<TResourceId>>> compose)
    {
        var standIn = Enumerable.Empty<TResource>().AsQueryable();
        return new ToParameters(standIn, rows, values).Visit(compose(standIn).Expression);
    }

    /// <summary>The value <paramref name="expression"/> reads: a constant, or a field or a property of one.</summary>
    private static object? ValueOf(Expression? expression) => expression switch
    {
        null => null,
        ConstantExpression constant => constant.Value,
        MemberExpression { Member: FieldInfo field } member => field.GetValue(ValueOf(member.Expression)),
        MemberExpression { Member: PropertyInfo property } member => property.GetValue(ValueOf(member.Expression)),
        _ => throw new InvalidOperationException("A condition of a reach of " + typeof(TResource).Name + " reads its values from " + expression + ", which is no field of a closure."),
    };

    /// <summary>
    /// What a compiled statement compares with: the caller and the moment, as parameters, and the roles that give the
    /// permission, as a constant.
    /// </summary>
    /// <param name="roles">The roles the rules declare for the permission.</param>
    private sealed class Values(IEnumerable<TRoleId> roles)
    {
        public ParameterExpression Member { get; } = Expression.Parameter(typeof(TMemberId), "member");

        public ParameterExpression Now { get; } = Expression.Parameter(typeof(DateTimeOffset), "now");

        public ConstantExpression Roles { get; } = Expression.Constant(roles.ToArray(), typeof(IEnumerable<TRoleId>));
    }

    /// <summary>
    /// Puts the rows in the place of the stand-in, and in the place of each value a condition compares with its parameter or
    /// its constant. The names of the resource's properties never change, so they become constants as well.
    /// </summary>
    private sealed class ToParameters(IQueryable<TResource> standIn, Expression rows, Values values) : ExpressionVisitor
    {
        protected override Expression VisitConstant(ConstantExpression node)
            => ReferenceEquals(node.Value, standIn) ? rows : base.VisitConstant(node);

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression?.Type != typeof(ReachValues<TMemberId, TRoleId>))
            {
                return base.VisitMember(node);
            }

            return node.Member.Name switch
            {
                nameof(ReachValues<TMemberId, TRoleId>.Member) => values.Member,
                nameof(ReachValues<TMemberId, TRoleId>.Now) => values.Now,
                nameof(ReachValues<TMemberId, TRoleId>.Roles) => values.Roles,
                _ => Expression.Constant(ValueOf(node), node.Type),
            };
        }
    }

    /// <summary>
    /// The shape of a statement: whether it is the application's own work, how the caller sees the resource, how it can
    /// hold the permission, and, where a role gives it, the permission, whose roles are constants of the statement.
    /// </summary>
    private readonly record struct Shape(bool Everything, MemberHeldBy Sees, MemberHeldBy Holds, string? Roles)
    {
        public static Shape Of(
            EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> act,
            EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> see)
            => new(act.Everything, see.HeldBy, act.HeldBy, act.HeldBy.HasFlag(MemberHeldBy.Roles) ? act.Permission : null);
    }
}
