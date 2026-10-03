using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Requests;

/// <summary>
/// What a request of the application's tenancy module implements to say what it requires of its caller: the
/// interface Tenancy's access check is registered for, as an application's module registers it for its own.
/// </summary>
public interface IHostRequest : IRequireAccess;
