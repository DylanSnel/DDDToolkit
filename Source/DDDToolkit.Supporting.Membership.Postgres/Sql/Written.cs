using DDDToolkit.Abstractions.Access;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.Postgres;

/// <summary>
/// What a resource's membership is written with beyond its rules, as the export works it out for the context that
/// maps the resource: who the caller is as a member, where the caller holds a permission above, and which roles may ask the
/// functions.
/// </summary>
/// <param name="Members">
/// Who the caller is as a member, as the SQL asks it: the rules' own source, or, where it follows from the member
/// id, the caller's id, or the function a contribution answers for that id, an organization package's for its seat.
/// </param>
/// <param name="Places">
/// The function that answers where the caller holds a permission, for a resource reached from above: the rules' own, or the
/// one a contribution answers for the type of the place, an organization package's for its unit; <see langword="null"/> for one that
/// is not.
/// </param>
/// <param name="GrantTo">The roles that may ask the functions, as the row access rules name them.</param>
/// <param name="Executors">The same roles as the database names them, which each function's body records.</param>
/// <param name="SystemInRole">The database role scoped system work runs as.</param>
internal sealed record Written(MemberSource Members, DatabaseFunction? Places, IReadOnlyList<string> GrantTo, IReadOnlyList<string> Executors, string SystemInRole);
