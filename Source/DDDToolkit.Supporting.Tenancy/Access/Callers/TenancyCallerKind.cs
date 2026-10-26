namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>Who is asking Tenancy, as Tenancy sees it.</summary>
public enum TenancyCallerKind
{
    /// <summary>
    /// Nobody Tenancy lets in: no tenant named, no seat in it, or a seat or tenant that is not active. Holds
    /// nothing and changes nothing; <see cref="ITenancyCaller.Refusal"/> says why.
    /// </summary>
    Nobody,

    /// <summary>A person, through a seat that is active, in a tenant that is too. Holds what the seat's roles grant.</summary>
    Seat,

    /// <summary>
    /// System work inside one tenant, such as an import, seeding or an operator's command. Holds every key in
    /// that tenant and nothing in any other.
    /// </summary>
    SystemInTenant,

    /// <summary>System work outside any tenant. It only provisions tenants; it reads and changes nothing inside one.</summary>
    System,
}
