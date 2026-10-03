using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.Supporting.Tenancy.Postgres;

/// <summary>
/// Turns the one thing Postgres refuses in a save of Tenancy's store that no index states into the refusal the
/// use case gives for the same rule: a tenant's last administrator, which the trigger keeps at commit. The
/// unique indexes need no translation here, since each says in the mapping what a save that breaks it is refused
/// with, and a row a policy denies is the toolkit's <c>access.refused</c>. Anything else, the other triggers
/// included, is not a refusal: it is a write the use cases never make, and goes on as the failure it is.
/// </summary>
/// <remarks>
/// It sees what fails in the store's save. The trigger that keeps an administrator checks when the transaction
/// commits, so its refusal is translated when the save commits it: the store's own transaction, or the one it
/// begins around a two-step save. A use case run inside a transaction the host began gets the failure at the
/// host's commit instead, as the <see cref="PostgresException"/> it is, with the constraint name
/// <c>tenancy_administrator_remains</c>.
/// </remarks>
internal sealed class TenancyPostgresFailures : ITenancySaveFailures
{
    /// <inheritdoc />
    public RefusalException? Translate(Exception failure, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(context);

        return PostgresFailureOf(failure) is { SqlState: PostgresErrorCodes.CheckViolation, ConstraintName: TenancySql.AdministratorRemainsConstraint }
            ? TenancyRefusals.Of(TenancyRefusals.LastAdmin)
            : null;
    }

    /// <summary>The Postgres error <paramref name="failure"/> is, or carries among its inner exceptions.</summary>
    private static PostgresException? PostgresFailureOf(Exception? failure)
        => failure switch
        {
            null => null,
            PostgresException postgres => postgres,
            AggregateException aggregate => aggregate.InnerExceptions.Select(PostgresFailureOf).FirstOrDefault(found => found is not null),
            _ => PostgresFailureOf(failure.InnerException),
        };
}
