using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy.Postgres;

/// <summary>
/// Puts the tenant of the current Tenancy caller on every connection, as <see cref="TenancyRowLevelSecurity.TenantSetting"/>:
/// a seat's tenant or the tenant system work acts in, and <c>''</c> for anyone else. The tenant is read from
/// Tenancy's caller, never from the toolkit's: which role may reach what with the value is for the SQL to
/// decide. A connection set for an anonymous caller names no tenant, whoever Tenancy's caller is: a signed-in
/// user the host has run as an anonymous one is shown to the database as nobody, and in no tenant either.
/// </summary>
internal sealed class TenancyTenantSetting : IRowLevelSecuritySettings
{
    /// <summary>How to write the value of each tenant id type, found once per type.</summary>
    private static readonly ConcurrentDictionary<Type, Func<object, string>> Writers = new();

    private static readonly MethodInfo WriterOfValue = typeof(TenancyTenantSetting).GetMethod(nameof(Write), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Names { get; } = [TenancyRowLevelSecurity.TenantSetting];

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, string>> For(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        return caller.Kind != CallerKind.Anonymous && TenancyCallers.CurrentTenantOrNull() is { } tenant
            ? [new KeyValuePair<string, string>(TenancyRowLevelSecurity.TenantSetting, ValueOf(tenant))]
            : [];
    }

    /// <summary>The value of a tenant id, written with the invariant culture.</summary>
    /// <exception cref="InvalidOperationException">The id wraps no single value.</exception>
    internal static string ValueOf(object tenant)
        => Writers.GetOrAdd(tenant.GetType(), static type =>
        {
            var wrapped = type.GetInterfaces()
                .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEntityId<>))
                .Select(candidate => candidate.GetGenericArguments()[0])
                .ToArray();

            return wrapped.Length == 1
                ? WriterOfValue.MakeGenericMethod(wrapped[0]).CreateDelegate<Func<object, string>>()
                : throw new InvalidOperationException(
                    "The tenant id " + type.Name + " does not wrap one value (IEntityId<TValue>), so it cannot travel to Postgres in "
                    + TenancyRowLevelSecurity.TenantSetting + ".");
        })(tenant);

    private static string Write<TValue>(object tenant)
        => ((IEntityId<TValue>)tenant).Value switch
        {
            null => string.Empty,
            Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString() ?? string.Empty,
        };
}
