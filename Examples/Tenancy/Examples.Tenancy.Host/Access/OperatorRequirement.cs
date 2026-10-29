using Microsoft.AspNetCore.Authorization;

namespace Examples.Tenancy.Host.Access;

/// <summary>
/// What every route of the application's own staff requires: the request's token carries the operators' role.
/// </summary>
/// <remarks>
/// Like <see cref="SeatRequirement"/>, a courtesy and not the protection. Every request behind such a route declares
/// <c>[Operator]</c>, and its module's access check refuses anyone else on the way to its handler. A query then reads as
/// the operators' own database role; a command runs as operator work in the tenant it names. The policy gives every such route the same answer before any
/// work starts. An operator holds no seat, so these routes are mapped outside the group that requires one, and
/// name their tenant in the path.
/// </remarks>
public sealed class OperatorRequirement : IAuthorizationRequirement;
