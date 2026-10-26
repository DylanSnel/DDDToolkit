namespace DDDToolkit.Supporting.Tenancy;

/// <summary>What a new role is made from.</summary>
/// <param name="Name">The role's name, 1 to 120 characters, unique in its tenant.</param>
/// <param name="Description">What the role is for, at most 1000 characters.</param>
/// <param name="Keys">The permission keys it grants; the keys they imply are added.</param>
/// <param name="FromPack">The key of the pack it was copied from, or <see langword="null"/> for a role made by hand.</param>
public sealed record RoleDraft(string Name, string Description, IReadOnlyCollection<string> Keys, string? FromPack = null);
