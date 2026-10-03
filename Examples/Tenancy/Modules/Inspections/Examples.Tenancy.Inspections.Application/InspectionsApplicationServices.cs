using DDDToolkit.Localization;
using DDDToolkit.Supporting.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Inspections.Application;

/// <summary>
/// Registers what this project adds to the host: Inspections' key, the check every one of its commands and
/// queries passes before its handler, and the texts of its own refusals in English and Dutch. The module's entry,
/// <c>AddInspectionsModule</c> in the API project, calls it next to the infrastructure project's
/// registration, which registers the ports, so the host still makes one call per module.
/// </summary>
public static class InspectionsApplicationServices
{
    /// <summary>
    /// Registers Inspections' key in the catalogue; the check that decides the module's own requirements
    /// (<see cref="InspectionsAccessCheck"/>); <see cref="InspectionsAccessBehavior{TMessage, TResponse}"/> in the
    /// pipeline of this module's requests, per scope like the handlers it runs before; and the resource files of
    /// <see cref="InspectionFailures"/> with the toolkit's localizer.
    /// </summary>
    /// <remarks>
    /// The behavior is the one the toolkit's generator writes for <see cref="IInspectionsRequest"/>, and
    /// <c>AddInspectionsAccessBehavior</c> is written with it. It asks the checks registered for the interface:
    /// the module's own, added here, which asks Projects' gate, and the Tenancy package's for the case that is
    /// Tenancy's, which the infrastructure project adds with <c>AddTenancyAccess</c>.
    /// <para>
    /// The handlers themselves are not registered here. The host's mediator finds them in this assembly when the
    /// host compiles and registers them itself, so a request without a handler is a build error there, not a
    /// missing line here. The ports they read and save through are the infrastructure project's to register;
    /// Tenancy's answers and catalogue come from Tenancy's own registration, and the gate the check asks from
    /// Projects', both of which come first.
    /// </para>
    /// <para>
    /// Behaviors run in the order they were registered. The host registers its own first, so this one runs
    /// inside them and right before the handler. Registered once however often the module is added.
    /// </para>
    /// <para>
    /// The texts are added here, with the module, and not by the host. The three codes Inspections passes on from
    /// Projects have their texts in Projects' files, so a code reads the same whichever module refused. Reading
    /// resource files needs <c>AddLocalization()</c>, which is the host's to call.
    /// </para>
    /// </remarks>
    /// <param name="services">The host's services.</param>
    public static IServiceCollection AddInspectionsApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Inspections' key, next to the code that asks for it. The application's catalogue lists the packs that
        // hold it; the administrators' pack holds it as it holds every key.
        services.AddTenancyPermissions(InspectionCatalogue.Permissions);

        // The check for the cases that take Projects' gate, and the behavior that asks the module's checks before
        // every handler. The project a request passed the gate for is kept for that request's handler
        // (Checked<T>), which is registered with the checks: a handler cannot be made where they were left out.
        services.AddAccessCheck<IInspectionsRequest, InspectionsAccessCheck>();
        services.AddInspectionsAccessBehavior();

        // What Inspections itself refuses with, in English and Dutch, for the edge to phrase in its reader's language.
        services.AddDDDToolkitLocalization(texts => texts.AddResource<InspectionFailures>());

        return services;
    }
}
