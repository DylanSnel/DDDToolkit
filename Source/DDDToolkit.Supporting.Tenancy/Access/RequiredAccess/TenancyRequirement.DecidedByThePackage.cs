namespace DDDToolkit.Supporting.Tenancy.Access;

public abstract partial record TenancyRequirement
{
    /// <summary>
    /// Nothing is required before the handler, because the use case of the Tenancy package the handler calls
    /// checks the caller itself, and refuses with the package's own codes.
    /// </summary>
    /// <remarks>
    /// Who may add a unit, give a role or archive one is the package's to decide: its use cases ask who is
    /// calling, and what they hold where, whoever calls them, so a second check in front of them would only be
    /// a second place to get it wrong. For a request whose handler hands it to such a use case and to nothing
    /// else; a handler that changes anything itself declares what that takes.
    /// <para>
    /// The check lets such a request through with nothing asked, in every module that adds the check, and a
    /// requirement does not see the handler: declared on a request whose handler does anything else, it is an
    /// open door. An architecture test of the application's own holds it, over the requests of every such
    /// module: a request that declares this has a handler that takes the package's use cases and nothing else.
    /// </para>
    /// </remarks>
    public sealed record DecidedByThePackage : TenancyRequirement;
}
