namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>What Tenancy's store leaves to the database. Nothing by default.</summary>
public sealed class TenancyStoreOptions
{
    /// <summary>
    /// The database writes each seat's rights itself, from its grants, its seat and its roles, and lets a seat
    /// read only its own. The save then writes no rights rows, and Tenancy's store asks the database's functions
    /// for what reaches past the calling seat's own rows: the tenant's administrators, the rights a move of a unit
    /// changes, and who holds a key at a unit. Tenancy's reads across tenants (<see cref="TenancySystemReads"/>)
    /// ask functions too, as scoped system work in no tenant, and nothing of Tenancy's then runs as the
    /// application itself. The functions are named by <see cref="TenancyFunctionNames"/>, and
    /// a package for one database writes them, together with what keeps the rights:
    /// <c>DDDToolkit.Supporting.Tenancy.Postgres</c> does, and its <c>AddTenancyPostgres()</c> turns this on.
    /// Nothing else should: with it on and no such functions in the database, the rights are never written.
    /// </summary>
    public bool DatabaseKeepsRights { get; set; }
}
