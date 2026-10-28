using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

/// <summary>
/// The user code began for the current flow of work. <see cref="AccessManager"/> begins it, and <see cref="CurrentUser"/>
/// answers it before the host's answer. It follows the flow the way an <see cref="AsyncLocal{T}"/> does: into awaited
/// calls and into tasks started inside, and back to the user before it when the scope is disposed.
/// </summary>
internal static class BegunUser
{
    private static readonly AsyncLocal<CurrentUserSnapshot?> Begun = new();

    /// <summary>The begun user, or <see langword="null"/> where nothing was begun.</summary>
    public static CurrentUserSnapshot? Value => Begun.Value;

    /// <summary>
    /// Makes <paramref name="user"/> the begun user until the result is disposed, and the one before it again afterwards.
    /// <see langword="null"/> makes nobody the begun user.
    /// </summary>
    public static IDisposable Begin(CurrentUserSnapshot? user)
    {
        var previous = Begun.Value;
        Begun.Value = user;
        return new Scope(previous);
    }

    /// <summary>Puts back the begun user from before <see cref="Begin"/>, once.</summary>
    private sealed class Scope(CurrentUserSnapshot? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Begun.Value = previous;
        }
    }
}
