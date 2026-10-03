namespace Examples.Tenancy.Tenants.Application.Access;

/// <summary>
/// Asks if a key is held for the whole tenant, at its root, now, and says yes or no where a check would refuse:
/// for a query that answers more to a caller who holds a key than to one who does not, and refuses neither.
/// </summary>
/// <remarks>
/// A request that requires such a key declares <see cref="TenancyRequirement.ForTheWholeTenant"/>, and the
/// package's check refuses the caller before the handler runs. This asks the same question of the same rows for
/// the part of an answer that depends on it, such as a role's keys in a list every seat of the tenant may read.
/// </remarks>
internal static class TenantWideKey
{
    /// <summary>
    /// True when <paramref name="key"/> is held by the caller at the tenant's root now. One statement, on a
    /// reading of this question's own. System work in the tenant holds every key there, and nobody holds none.
    /// </summary>
    /// <param name="answers">Tenancy's answers about the current caller.</param>
    /// <param name="reads">Where the key is asked about: Tenancy's rows, on a context of the question's own.</param>
    /// <param name="key">The key asked about.</param>
    /// <param name="cancellationToken">Cancels the question.</param>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    public static async Task<bool> IsHeldAsync(SampleAnswers answers, ITenancyReads reads, string key, CancellationToken cancellationToken)
    {
        await using var reading = reads.Open();
        return await answers.Over(reading.Rows, reading.Queries).HoldsTenantWideAsync(key, cancellationToken);
    }
}
