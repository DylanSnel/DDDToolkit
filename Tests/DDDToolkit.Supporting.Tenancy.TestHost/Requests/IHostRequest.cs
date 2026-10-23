using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Requests;

/// <summary>
/// What a request of the application's tenancy module implements to say what it requires of its caller: the
/// interface Tenancy's access check is registered for, as an application's module registers it for its own.
/// </summary>
/// <remarks>
/// Marked and declared partial as a module declares its own, so the requests that state what they require with
/// attributes (<see cref="StatedRequests"/>) answer it from the line the toolkit's generator writes into it. The host
/// references no dispatcher, so no behavior is written beside it.
/// </remarks>
[AccessRequests]
public partial interface IHostRequest : IRequireAccess;
