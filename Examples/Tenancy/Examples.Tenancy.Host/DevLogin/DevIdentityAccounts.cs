using System.Collections.Concurrent;
using DDDToolkit.Identity;

namespace Examples.Tenancy.Host.DevLogin;

/// <summary>
/// The accounts people sign in with, for a host whose only login is the dev login: the development adapter of
/// the port that Supabase Auth's admin client answers on a real project.
/// </summary>
/// <remarks>
/// <para>
/// The demonstration people are the accounts there are: each has an address and a fixed id, and signs in with a
/// click. So an invitation to one of their addresses is answered as an address that has an account, and that
/// person accepts it after signing in as themself. Any other address gets an account made here, in memory, under
/// a new id, as an identity provider would make one, and no mail: the dev login signs in nobody but the
/// demonstration people, so such an account waits for a host with a real Auth server.
/// </para>
/// <para>
/// It keeps to what the port promises: an id it answers is the id of an account it made just now, an address is
/// one account's, and nothing here finds an account by its address for a caller.
/// </para>
/// <para>
/// One instance for the host, and safe to call from requests side by side. What it made is gone when the host stops.
/// </para>
/// </remarks>
public sealed class DevIdentityAccounts : IIdentityAccounts
{
    // The accounts made here, by address and by id. An address is compared as Auth compares one: ignoring case.
    private readonly ConcurrentDictionary<string, Guid> _made = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<IdentityAccountOutcome> CreateAsync(Guid identity, string address, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (identity == Guid.Empty || DemoPersonWith(identity) is not null || _made.Values.Contains(identity))
        {
            throw new InvalidOperationException("The id " + identity + " is empty or already an account's.");
        }

        return Task.FromResult(Make(address, identity));
    }

    /// <inheritdoc />
    public Task<IdentityAccountOutcome> InviteByEmailAsync(string address, Uri? signInRedirect, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return Task.FromResult(Make(address, Guid.NewGuid()));
    }

    /// <inheritdoc />
    public Task<IdentityInvitation> InviteAccountAsync(Guid identity, Uri? signInRedirect, CancellationToken cancellationToken)
        => Task.FromResult(
            DemoPersonWith(identity) is not null ? IdentityInvitation.AlreadyProven
            : _made.Values.Contains(identity) ? IdentityInvitation.Sent
            : IdentityInvitation.NoSuchAccount);

    /// <inheritdoc />
    public Task<IdentityAccount?> FindAsync(Guid identity, CancellationToken cancellationToken)
        => Task.FromResult(
            DemoPersonWith(identity) is not null ? new IdentityAccount(identity, HasSignedIn: true)
            : _made.Values.Contains(identity) ? new IdentityAccount(identity, HasSignedIn: false)
            : null);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid identity, CancellationToken cancellationToken)
    {
        // A demonstration person is not this adapter's to delete: the dev login would sign them in all the same.
        var deleted = false;
        foreach (var (address, id) in _made)
        {
            deleted |= id == identity && _made.TryRemove(address, out _);
        }

        return Task.FromResult(deleted);
    }

    /// <summary>An account for <paramref name="address"/> under <paramref name="identity"/>, unless the address has one.</summary>
    private IdentityAccountOutcome Make(string address, Guid identity)
    {
        var trimmed = address.Trim();
        var taken = DemoPeople.All.Concat(DemoPeople.WithoutASeat).Any(person => string.Equals(person.Email, trimmed, StringComparison.OrdinalIgnoreCase));

        return !taken && _made.TryAdd(trimmed, identity)
            ? new IdentityAccountOutcome.Created(identity)
            : new IdentityAccountOutcome.AddressTaken();
    }

    private static DemoPerson? DemoPersonWith(Guid identity)
        => DemoPeople.All.Concat(DemoPeople.WithoutASeat).FirstOrDefault(person => person.Id == identity);
}
