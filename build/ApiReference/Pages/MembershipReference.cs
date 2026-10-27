namespace DDDToolkit.Build.ApiReference.Pages;

/// <summary>
/// Membership's reference, in <c>docs/membership/reference</c>: which types each page holds. The folders are where the
/// packages keep their code; a type in a folder no claim takes still gets a section, under "Other" on its package's last
/// page, and the tool says so. Three attributes of <c>DDDToolkit.Abstractions</c> that a resource with members is declared
/// and asked about with are taken by name, beside the package's own types.
/// </summary>
internal static class MembershipReference
{
    public const string Membership = "DDDToolkit.Supporting.Membership";
    public const string Markers = "DDDToolkit.Supporting.Membership.Abstractions";
    public const string EntityFramework = "DDDToolkit.Supporting.Membership.EntityFramework";
    public const string Postgres = "DDDToolkit.Supporting.Membership.Postgres";

    // Not documented whole: only the types a claim names.
    private const string Abstractions = "DDDToolkit.Abstractions";

    public static readonly ReferencePlan Plan = new(
        "Membership",
        "docs/membership/reference",
        [Membership, Markers, EntityFramework, Postgres],
        [
            new(
                "resource-and-members.md",
                "Resource and members",
                "The classes an application declares for a resource with members, and what they keep: the member template and "
                + "the member list over it, the periods and the roles a member holds, what the resource's class says of itself, "
                + "and the template of the roles a customer makes. Each template's section opens with the name the application "
                + "sees, the template in brackets, and the attribute that declares the application's class.\n\n"
                + "A member class is declared with `[Member<TId, TMemberId, TRoleId, TResource>]`, and the generator derives it "
                + "from `MemberEntity<TId, TMemberId, TRoleId>`, closed over the first three. What fills the four type "
                + "parameters:\n\n"
                + PageWriter.TypeParameterTable + "MemberAttribute}\n\n"
                + "A role class, for a resource whose customers make its roles, is declared with `[CustomRole<TRoleId, TResource>]`, "
                + "and derives from `CustomRoleAggregate<TRoleId>`. Its id is the `TRoleId` the resource's member class holds "
                + "roles by:\n\n"
                + PageWriter.TypeParameterTable + "CustomRoleAttribute}",
                [
                    new("Member", [new(Membership, Folder: "Aggregates/Members")]),
                    new(
                        "Member list",
                        [
                            new(Membership, Type: "MemberList"),
                            new(Membership, Type: "FormerOwner"),
                            new(Membership, Type: "FormerOwnerLeaves"),
                            new(Membership, Type: "OwnerNamed"),
                        ]),
                    new(
                        "Periods and roles held",
                        [new(Membership, Folder: "Aggregates/Members/ValueObjects"), new(Membership, Type: "MemberRole")],
                        Lead: ["MemberPeriod", "MemberRole", "NamedRole"]),
                    new(
                        "The resource",
                        [
                            new(Membership, Type: "IMembershipResource"),
                            new(Membership, Type: "MembersAttribute"),
                            new(Abstractions, Type: "OwnerAttribute"),
                            new(Markers, Type: "AtAttribute"),
                        ],
                        "What the resource's own class says, or has written onto it: its name, its codes, its owner and where it "
                        + "hangs. `[At]` is Membership's, from `DDDToolkit.Supporting.Membership.Abstractions`. `[Owner]` is the "
                        + "toolkit's, from `DDDToolkit.Abstractions`, and says what it says "
                        + "for every package that reads it."),
                    new("Custom role", [new(Membership, Folder: "Aggregates/Roles")]),
                ]),
            new(
                "rules.md",
                "Rules and permissions",
                "The rules of one kind of resource, as one declaration: `MembershipRules.For<TResource>` and the mark that hands "
                + "them to the export, where the roles come from, the permissions declared on the resource and their settings, "
                + "reach from above, and who the caller is as a member. The codes a resource refuses under are with the "
                + "refusals, under [`MembershipCodes`](refusals.md#membershipcodes).",
                [
                    new("Rules", [new(Membership, Folder: "Access/Rules")], Lead: ["MembershipRules", "MembershipRulesAttribute", "MembershipFunctions"]),
                    new(
                        "Roles",
                        [
                            new(Membership, Type: "Roles"),
                            new(Membership, Type: "RolesKind"),
                            new(Membership, Type: "DeclaredRole"),
                            new(Membership, Type: "RolePermissions"),
                            new(Membership, Type: "OtherPermissions"),
                        ]),
                    new("Permissions", [new(Membership, Type: "OnAttribute"), new(Membership, Type: "ResourcePermission")]),
                    new("Reach from above", [new(Membership, Type: "ReachFromAbove")]),
                    new("Who a member is", [new(Membership, Folder: "Access/Callers")], Lead: ["MemberSource", "MemberSourceKind", "ICallerMember"]),
                ]),
            new(
                "use-cases.md",
                "Use cases and access",
                "What a handler asks before it changes the members, what a caller is shown of them, the starter roles of a "
                + "resource whose customers make its roles, the access manager of one kind of resource, the requirements a "
                + "request states, and the contracts that publish a resource's access to row access rules. The access manager "
                + "is asked for by the resource's class, `MembershipAccessManager<Board>`. The admission and the requirements "
                + "are asked for by the resource's id. So those of documents never answer for folders. Their registration is "
                + "with Entity Framework, under [Registration](entity-framework.md#registration).",
                [
                    new("Admission", [new(Membership, Folder: "UseCases/Admission")], Lead: ["MemberAdmission"]),
                    new("Overview", [new(Membership, Folder: "UseCases/Overview")], Lead: ["MemberOverviews"]),
                    new("Starter roles", [new(Membership, Folder: "UseCases/Roles")]),
                    new(
                        "Access manager",
                        [new(Membership, Folder: "Access/Questions")],
                        Lead: ["MembershipAccessManager", "MembershipAccessManagerExtensions", "MemberHold", "MemberVia", "MemberReach", "IMembershipStorage"]),
                    new(
                        "Requirements",
                        [new(Membership, Folder: "Access/RequiredAccess")],
                        Lead: ["MemberAccess", "On", "SeenWith", "Sees", "OnResourceAttribute", "ExpectedVersionAttribute", "SeenWithAttribute", "MemberAccessCheck"]),
                    new(
                        "Resource access contracts",
                        [new(Abstractions, Type: "ResourceAccessContractAttribute"), new(Abstractions, Type: "ResourceAccessSet")],
                        "From `DDDToolkit.Abstractions`: how the module that owns a resource with members publishes the sets its "
                        + "access answers, for the row access rules of any module to ask by the resource's id."),
                ]),
            new(
                "refusals.md",
                "Refusals",
                "Every rule Membership refuses with, in the members of `MembershipRefusals`; the codes one resource refuses "
                + "them under, `MembershipCodes`; and the resource that holds their texts.",
                [
                    new("Refusal codes", [new(Membership, Folder: "Refusals")]),
                    new("A resource's codes", [new(Membership, Type: "MembershipCodes")]),
                    new("Texts", [new(Membership, Folder: "Resources")]),
                ]),
            new(
                "entity-framework.md",
                "Entity Framework",
                "The public API of `DDDToolkit.Supporting.Membership.EntityFramework`, by folder: the registration a host calls "
                + "for each kind of resource, the mapping of a resource's member tables into the application's own context, the "
                + "queries on the resource's own set, `db.Boards.WhereIHold(permission)`, that put what the access manager "
                + "decides into a module's own statements, and what an application "
                + "answers for a resource whose roles or places it keeps itself.",
                [
                    new("Registration", [new(EntityFramework, Folder: "Registration")], Lead: ["MembershipEntityFrameworkServiceCollectionExtensions"]),
                    new("Mapping", [new(EntityFramework, Folder: "Mapping")], Lead: ["MembershipEntityTypeBuilderExtensions", "MemberTableNames"]),
                    new("Queries", [new(EntityFramework, Folder: "Access/Queries")], Lead: ["MemberQueryableExtensions", "PermissionOn"]),
                    new("Answered by the application", [new(EntityFramework, Folder: "Access/Ports")]),
                ]),
            new(
                "postgres.md",
                "Postgres",
                "The public API of `DDDToolkit.Supporting.Membership.Postgres`, by folder: its registration, the row access "
                + "contribution the Supabase export writes for each kind of resource, and the start-up check that the database "
                + "has the functions the rules say.",
                [
                    new("Registration", [new(Postgres, Folder: "Registration")]),
                    new("Row access contribution", [new(Postgres, Folder: "Policies")]),
                    new("Start-up checks", [new(Postgres, Folder: "Checks")]),
                ]),
        ]);
}
