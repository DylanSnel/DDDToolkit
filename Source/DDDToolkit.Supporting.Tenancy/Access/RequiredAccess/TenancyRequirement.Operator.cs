using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

public abstract partial record TenancyRequirement
{
    /// <summary>
    /// The caller is one of the application's operators: a signed-in user whose token carries one of
    /// <see cref="TenancyOptions{TTenantId, TSeatId, TUnitId, TRoleId}.OperatorTokenRoles"/>. Anyone else is
    /// refused with <c>tenancy.operators-only</c>, a seat and system work included. A request declares it with
    /// <see cref="TenancyAccess.Operator(OperatorTenant, string)"/>, or with <see cref="OperatorAttribute"/>.
    /// </summary>
    /// <remarks>
    /// On a command, the pipeline runs the handler as operator work recorded as the operator. The work acts in no tenant,
    /// or, with <see cref="OperatorTenant.FromRequest"/>, in the tenant the request names. On a query nothing begins. It is
    /// the toolkit's current user that is asked, never the Tenancy caller, which is nobody for an operator in every tenant.
    /// </remarks>
    public sealed record Operator : TenancyRequirement
    {
        /// <summary>Made by <see cref="TenancyAccess.Operator(OperatorTenant, string)"/>, the one spelling a request writes.</summary>
        internal Operator(OperatorTenant tenant, string scope)
        {
            Tenant = tenant;
            Scope = scope;
        }

        /// <summary>
        /// Where a command's operator work acts: no tenant (<see cref="OperatorTenant.FromCommand"/>), or the tenant the
        /// request names (<see cref="OperatorTenant.FromRequest"/>).
        /// </summary>
        public OperatorTenant Tenant { get; }

        /// <summary>The scope a command's operator work runs in.</summary>
        public string Scope { get; }
    }

    /// <summary>
    /// The caller is one of the application's operators, and the request names the tenant it is about. On a command, the
    /// operator work acts in <see cref="Tenant"/>. A request that names another tenant beside it, as tenant selection reads
    /// it, is refused with <c>tenancy.tenant-mismatch</c>. A request declares it with
    /// <see cref="TenancyAccess.Operator{TTenantId}(TTenantId, OperatorTenant, string)"/>, or with
    /// <see cref="OperatorAttribute"/> and <see cref="ForTenantAttribute"/> on the member that names the tenant.
    /// </summary>
    /// <remarks>
    /// The check decides the case for the application's tenant id alone. A case over another id stops the request with an
    /// <see cref="InvalidOperationException"/> that names it.
    /// </remarks>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    public sealed record OperatorIn<TTenantId> : TenancyRequirement
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        /// <summary>Made by <see cref="TenancyAccess.Operator{TTenantId}(TTenantId, OperatorTenant, string)"/>, the one spelling a request writes.</summary>
        internal OperatorIn(TTenantId tenant, string scope)
        {
            Tenant = tenant;
            Scope = scope;
        }

        /// <summary>The tenant the request names, which a command's operator work acts in.</summary>
        public TTenantId Tenant { get; }

        /// <summary>The scope a command's operator work runs in.</summary>
        public string Scope { get; }
    }
}
