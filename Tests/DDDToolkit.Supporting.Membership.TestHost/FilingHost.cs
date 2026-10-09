using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Supporting.Membership.TestHost;

/// <summary>
/// The application's Membership registration, with both kinds of resource kept in <see cref="FilingContext"/>.
/// It lives here, next to the member classes it is closed over, as an application's does: the project declares
/// two member classes, so the generator writes a registration for each, named after the resource the class
/// names and internal to this project, and tests call this rather than naming the seven types.
/// </summary>
public static class FilingHost
{
    /// <summary>
    /// Registers the documents and the folders, each with its own rules, the check of each for the requests
    /// of the module, which are about both, and the handlers of the two requests that change members.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection Add(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Generated for this project's member classes, each named after the resource its class names and closed over
        // the class, its types, the resource and the resource's id: only the context is left to say.
        services.AddDocumentMembership<FilingContext>(DocumentMembership.Rules);
        services.AddFolderMembership<FilingContext>(FolderMembership.Rules);

        // One set of checks for the module's request interface, with a check for each kind of resource in it:
        // generated beside the registrations above, each closed over its resource and that resource's id.
        services.AddDocumentMemberAccess<IFilingRequest>();
        services.AddFolderMemberAccess<IFilingRequest>();

        // Who may change the members is the host's to decide, so the handlers that do are the host's own.
        services.TryAddScoped<ShareDocumentHandler>();
        services.TryAddScoped<AdmitStaffHandler>();

        return services;
    }
}
