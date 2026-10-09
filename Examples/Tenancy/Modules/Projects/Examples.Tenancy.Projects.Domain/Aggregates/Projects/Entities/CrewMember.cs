using DDDToolkit.Supporting.Membership;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects.ValueObjects;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Entities;

/// <summary>
/// One seat on a project's crew, for a period, with the project roles it holds there. A child entity of
/// <see cref="Project"/>: loaded and saved with it, and changed only through it.
/// </summary>
/// <remarks>
/// Declared with the Membership package's member template, which gives it everything a member is: the seat
/// (<see cref="MemberEntity{TId, TMemberId, TRoleId}.MemberId"/>), the period it is on the crew for, who put it
/// there, and the roles it holds, each a project role of the tenant held on this one project for a period of its
/// own. Being on a crew lets a seat see the project; what else it may do there comes from its roles.
/// <para>
/// The rules about a crew are the package's, and the project keeps them through the member list the package's
/// generator writes on it: a seat is on the crew once, holds a role once, and the owner stays on it. What only
/// this application knows about a crew, that a closed project changes no more and which events a change raises,
/// is the project's.
/// </para>
/// </remarks>
[Member<CrewMemberId, SeatId, ProjectRoleId, Project>]
public sealed partial class CrewMember;
