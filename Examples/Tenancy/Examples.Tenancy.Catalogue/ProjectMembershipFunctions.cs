using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Membership.Postgres;
using Examples.Tenancy.Catalogue;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects.Entities;

[assembly: RowAccessContribution(typeof(ProjectMembershipFunctions))]

namespace Examples.Tenancy.Catalogue;

/// <summary>
/// What the Membership package writes into the exported access files for the projects: the four functions that
/// answer, for the caller of a connection, which projects it sees and holds a key on, through their crews, by
/// owning them and from the organization, and the lock that holds the crews' tables and a project's owner to the
/// keys the rules name. All of it written from the projects' rules (<see cref="SampleCatalogue.Projects"/>), made
/// from the starter roles the host adds the Projects module with.
/// </summary>
/// <remarks>
/// Offered here, next to the rules it is written from, and used by the project that exports, with
/// <c>[assembly: UseRowAccessContribution(typeof(ProjectMembershipFunctions))]</c>. The export makes it with
/// <c>new</c>, before any host exists, which is why the rules are an instance the catalogue holds. Row access rules
/// ask two of the functions by the project's id, through Projects' contracts, and the export writes their policies
/// with the names the rules give the functions; the host's start-up check holds the database to the rules it runs
/// with.
/// </remarks>
public sealed class ProjectMembershipFunctions() : MembershipRowAccessContribution<CrewMember>(SampleCatalogue.Projects.Rules);
