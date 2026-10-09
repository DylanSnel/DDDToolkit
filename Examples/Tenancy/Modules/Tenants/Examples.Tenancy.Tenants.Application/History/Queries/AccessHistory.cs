using DDDToolkit.Supporting.Tenancy.Catalogue;
using GreenDonut.Data;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.History.Queries;

/// <summary>
/// The access history of the caller's tenant: every change to who may do what, newest first and a page at a
/// time, each with who made it.
/// </summary>
/// <remarks>
/// For whoever holds <see cref="RequiredKey"/> for the whole tenant: the history says who was given what
/// everywhere in it, so a key held at one unit does not read it. The key manages no access, so a role that holds
/// it is given like any other. The history is not kept to a tenant by the storage's filter, so the handler names
/// the tenant it reads: the caller's own, and never one a request could supply.
/// </remarks>
/// <param name="Paging">Which page: how many rows, and after which marker.</param>
public sealed record AccessHistory(PagingArguments Paging) : IQuery<Page<AccessHistoryEntry>>, ITenantsRequest
{
    /// <summary>The key the caller holds for the whole tenant.</summary>
    public const string RequiredKey = TenancyKeys.HistoryView;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.ForTheWholeTenant(RequiredKey);
}

/// <summary>Answers <see cref="AccessHistory"/> for the tenant the caller works in, on a context of the read's own.</summary>
/// <param name="answers">Tenancy's answers about the current caller: which tenant it works in.</param>
/// <param name="reads">Where the history is read.</param>
public sealed class AccessHistoryHandler(SampleAnswers answers, ITenancyReads reads) : IQueryHandler<AccessHistory, Page<AccessHistoryEntry>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenants.history.page-size-invalid</c>, with <c>Max</c>; <c>tenancy.cursor-invalid</c> for a marker that is
    /// not this list's.
    /// </exception>
    public async ValueTask<Page<AccessHistoryEntry>> Handle(AccessHistory query, CancellationToken cancellationToken)
        => await reads.HistoryAsync(answers.RequireTenant().Tenant, HistoryRefusals.Checked(query.Paging), cancellationToken);
}
