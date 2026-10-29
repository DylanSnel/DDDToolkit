namespace DDDToolkit.Access;

/// <summary>
/// Nothing began a user for the current work, and the host requires every flow of work to say who it runs as
/// (<see cref="CurrentUserOptions.RequireExplicitCurrentUser"/>). Begin a user around the work that threw it.
/// </summary>
public sealed class NoCurrentUserException : InvalidOperationException
{
    /// <summary>What went wrong, and the ways to put it right.</summary>
    public const string DefaultMessage =
        "Nothing said who this work runs as, and this host requires it (RequireExplicitCurrentUser). Begin the work with AccessManager: " +
        "BeginSystem(scope) for a module's system work, which the row access rules hold, BeginSystemBypassingRowAccess() for system work past every rule, " +
        "or BeginAs(userId) for a user.";

    /// <summary>An exception with <see cref="DefaultMessage"/>.</summary>
    public NoCurrentUserException()
        : base(DefaultMessage)
    {
    }

    /// <summary>An exception with a message that names the work and how to begin a user for it.</summary>
    public NoCurrentUserException(string message)
        : base(message)
    {
    }

    /// <summary>An exception with a message and the exception behind it.</summary>
    public NoCurrentUserException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
