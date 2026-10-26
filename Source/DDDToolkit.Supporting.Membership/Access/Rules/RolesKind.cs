namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>The kinds of <see cref="Roles"/>.</summary>
public enum RolesKind
{
    /// <summary>Roles in code, held by name: <see cref="Roles.Fixed"/>.</summary>
    Fixed,

    /// <summary>Rows a customer makes: <see cref="Roles.Custom"/>.</summary>
    Custom,

    /// <summary>The application's own store of roles: <see cref="Roles.External"/>.</summary>
    External,
}
