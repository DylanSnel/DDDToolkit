namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The rule every write of a tenant's row passes, whatever wrote it: a seat and system work in a tenant only
/// write their own tenant's rows, and nobody writes any. The storage applies it to every changed row that
/// belongs to a tenant, before anything is written, so a use case that loaded the wrong row cannot save it.
/// <para>
/// System work outside any tenant writes no tenant's rows either. It acts in no tenant, so every tenant's row
/// is another tenant's to it. Provisioning, the one thing it starts, saves as system work inside the tenant it
/// makes, once it knows that tenant's id, and so passes as that; nothing is left that system work outside any
/// tenant has to save.
/// </para>
/// </summary>
public static class TenancySaveCheck
{
    /// <summary>Checks one changed row against the current Tenancy caller.</summary>
    /// <param name="entryTenant">The tenant the row belongs to, boxed: its tenant id.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entryTenant"/> is null.</exception>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.other-tenant</c> when the row belongs to another tenant than the caller's, and for every row
    /// when the caller is system work outside any tenant; <c>tenancy.not-seated</c> when there is no caller
    /// Tenancy lets in.
    /// </exception>
    public static void Check(object entryTenant)
    {
        ArgumentNullException.ThrowIfNull(entryTenant);

        var caller = TenancyCallers.Ambient;
        switch (caller?.Kind)
        {
            case TenancyCallerKind.System:
                // It acts in no tenant; provisioning saves as system work in the tenant it makes.
                throw TenancyRefusals.Of(TenancyRefusals.OtherTenant);

            case TenancyCallerKind.Seat or TenancyCallerKind.SystemInTenant:
                if (!Equals(caller.TenantId, entryTenant))
                {
                    throw TenancyRefusals.Of(TenancyRefusals.OtherTenant);
                }

                return;

            default:
                throw TenancyRefusals.Of(TenancyRefusals.NotSeated);
        }
    }
}
