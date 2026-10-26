namespace Examples.Tenancy.Ui.Api.TryIt;

/// <summary>A field of the try-it form that an action uses.</summary>
public enum TryItField
{
    /// <summary>A project's id.</summary>
    Project,

    /// <summary>A key of the catalogue, such as <c>projects.close</c>.</summary>
    Key,

    /// <summary>A project's new name.</summary>
    Name,

    /// <summary>A unit's id.</summary>
    Unit,

    /// <summary>An inspection's title.</summary>
    Title,

    /// <summary>A seat's id.</summary>
    Seat,

    /// <summary>A role's id: one of the organization, given at a unit.</summary>
    Role,

    /// <summary>A project role's id: one the tenant keeps for its crews.</summary>
    ProjectRole,

    /// <summary>The day a grant, a crew membership or a crew role ends.</summary>
    Until,

    /// <summary>Why a grant is made.</summary>
    Reason,
}
