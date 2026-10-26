using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace Examples.Tenancy.Ui.Session;

/// <summary>
/// Keeps the <see cref="UiSession"/> across a reload of the page, in the tab's session storage, encrypted with the
/// server's data protection keys: the browser holds it but cannot read it, and it is gone when the tab closes.
/// </summary>
/// <remarks>
/// A reload starts a new circuit with a new, empty session; <see cref="RestoreAsync"/> signs it back in. Storage
/// needs the browser, so this runs once the circuit is interactive, which is from the start: the UI renders with
/// prerendering off. Whatever cannot be read back, such as a copy sealed with keys the server no longer has, is
/// dropped, and the person signs in again.
/// </remarks>
public sealed class SessionStore(ProtectedSessionStorage storage, UiSession session, TimeProvider clock, ILogger<SessionStore> logger)
{
    private const string Key = "tenancy-ui.session";

    /// <summary>Whether <see cref="RestoreAsync"/> has run for this circuit.</summary>
    public bool Restored { get; private set; }

    /// <summary>Signs the session back in from the copy kept before a reload, once per circuit.</summary>
    public async Task RestoreAsync()
    {
        if (Restored)
        {
            return;
        }

        try
        {
            var kept = await storage.GetAsync<SessionSnapshot>(Key);
            if (kept is { Success: true, Value: { } snapshot } && !session.Restore(snapshot, clock.GetUtcNow()))
            {
                await storage.DeleteAsync(Key);
            }
        }
        catch (Exception exception) when (exception is CryptographicException or JSException or InvalidOperationException)
        {
            logger.LogInformation(exception, "The kept session could not be read back; starting signed out.");
            await DeleteQuietlyAsync();
        }
        finally
        {
            Restored = true;
        }
    }

    /// <summary>Keeps the session as it is now, or removes the copy when nobody is signed in.</summary>
    public async Task SaveAsync()
    {
        try
        {
            if (session.Snapshot() is { } snapshot)
            {
                await storage.SetAsync(Key, snapshot);
            }
            else
            {
                await storage.DeleteAsync(Key);
            }
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // The tab is closing, or the circuit is gone: there is nothing left to keep the session for.
            logger.LogDebug(exception, "The session could not be kept.");
        }
    }

    private async Task DeleteQuietlyAsync()
    {
        try
        {
            await storage.DeleteAsync(Key);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException)
        {
            logger.LogDebug(exception, "The unreadable session could not be removed.");
        }
    }
}
