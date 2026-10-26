namespace DDDToolkit.Supporting.Tenancy.Postgres;

/// <summary>What Tenancy puts on a Postgres connection for its policies to read.</summary>
public static class TenancyRowLevelSecurity
{
    /// <summary>
    /// The setting the tenant of the current Tenancy caller travels in, next to the caller's role and claims and
    /// in the same statement: the value of its id, written with the invariant culture (a <see cref="Guid"/> as
    /// <c>D</c>), or <c>''</c> when the caller acts in no tenant. SQL reads it with
    /// <c>current_setting('tenancy.caller_tenant', true)</c>.
    /// <para>
    /// It is the application's word, as every setting is: any statement on the application's connection can set
    /// it. SQL that reads it for a signed-in user should find the seat from the verified identity and this
    /// tenant, and require that seat and tenant to be active, so a wrong tenant reaches no seat; only work the
    /// application begins on purpose, the scoped system caller's, can take it as given.
    /// </para>
    /// </summary>
    public const string TenantSetting = "tenancy.caller_tenant";
}
