using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

/// <summary>
/// The host's answer to who the current work runs as, where nothing began a user for it. In ASP.NET Core that is the
/// request's user. <see cref="CurrentUser"/> asks it after the begun user and before its own default.
/// </summary>
/// <remarks>
/// It is a singleton and reads the current request or flow of work each time it is asked, the way
/// <c>IHttpContextAccessor</c> does. <c>DDDToolkit.Auth.Supabase.AspNetCore</c> registers one that answers the user of the
/// request's validated Supabase token, or somebody who has not signed in. A host with another way to know its users
/// registers its own:
/// <code>
/// public sealed class ApiKeyUsers(IHttpContextAccessor requests) : ICurrentUserSource
/// {
///     public CurrentUserSnapshot? Current => requests.HttpContext?.Items["user"] as CurrentUserSnapshot;
/// }
///
/// services.AddSingleton&lt;ICurrentUserSource, ApiKeyUsers&gt;();
/// </code>
/// </remarks>
public interface ICurrentUserSource
{
    /// <summary>
    /// The user the current work runs as, as the host knows it, or <see langword="null"/> where the host knows none, such
    /// as outside any request.
    /// </summary>
    CurrentUserSnapshot? Current { get; }
}
