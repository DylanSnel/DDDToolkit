namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>A hold the request in hand passed with, as the interceptor holds a save to it.</summary>
/// <param name="Kept">The hold itself, which is one object per pass of a request's check.</param>
/// <param name="Resource">The resource it is about.</param>
/// <param name="Version">The version the check read.</param>
internal sealed record HeldVersion(object Kept, object Resource, long Version);
