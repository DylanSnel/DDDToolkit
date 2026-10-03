using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>A reach as the statements of one kind of resource take it.</summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
internal interface IEfMemberReach<TResource>
    where TResource : class
{
    /// <summary>Those of <paramref name="resources"/> the reach reaches, in a statement that runs on <paramref name="context"/>, or on the request's own.</summary>
    IQueryable<TResource> Within(IQueryable<TResource> resources, DbContext? context);

    /// <summary>Each of <paramref name="resources"/> with how the reach reaches it, in a statement that runs on <paramref name="context"/>, or on the request's own.</summary>
    IQueryable<MemberFound<TResource>> Reached(IQueryable<TResource> resources, DbContext? context);
}
