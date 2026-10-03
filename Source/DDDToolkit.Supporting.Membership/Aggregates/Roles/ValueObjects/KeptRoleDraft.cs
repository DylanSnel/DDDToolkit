namespace DDDToolkit.Supporting.Membership;

/// <summary>What a new role of a resource is made from.</summary>
/// <param name="Name">
/// The role's name, 1 to <see cref="KeptRoleAggregate{TRoleId}.MaxNameLength"/> characters; white space
/// around it is dropped.
/// </param>
/// <param name="Description">
/// What the role is for, at most <see cref="KeptRoleAggregate{TRoleId}.MaxDescriptionLength"/>
/// characters, or nothing.
/// </param>
/// <param name="Keys">The permission keys it gives: each one a key the resource's rules let a member's role give.</param>
/// <param name="MadeFrom">
/// The name of the starter role it is made from, one of the roles the resource's rules declare, or
/// <see langword="null"/> for a role a customer makes. The drafts of the starter roles carry it
/// (<see cref="UseCases.StarterRoles"/>); a draft made from what a customer entered leaves it out.
/// </param>
public sealed record KeptRoleDraft(string Name, string? Description, IReadOnlyCollection<string> Keys, string? MadeFrom = null);
