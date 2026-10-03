using DDDToolkit.Localization;
using DDDToolkit.Supporting.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Projects.Application;

/// <summary>
/// Registers what this project adds to the host: Projects' keys, the projects' rules with their starter roles, the
/// access rules with the gate other modules ask, the check of the requirement that is the module's own, and the
/// texts of its refusals in English and Dutch. The module's entry, <c>AddProjectsModule</c> in the API project,
/// calls it next to the infrastructure project's registration, which registers the ports and the projects with the
/// Membership package, so the host still makes one call per module.
/// </summary>
public static class ProjectsApplicationServices
{
    /// <summary>
    /// Registers Projects' keys in the catalogue; <paramref name="membership"/>, the rules and starter roles the
    /// handlers read; the rules, the gate and what the handlers share, per scope; the check that decides the
    /// module's own requirement (<see cref="ProjectsAccessCheck"/>);
    /// <see cref="ProjectsAccessBehavior{TMessage, TResponse}"/> in the pipeline of this module's requests; and the
    /// resource files of <see cref="ProjectFailures"/> with the toolkit's localizer.
    /// </summary>
    /// <remarks>
    /// The behavior is the one the toolkit's generator writes for <see cref="IProjectsRequest"/>, and
    /// <c>AddProjectsAccessBehavior</c> is written with it. It asks the checks registered for the interface: the
    /// module's own, added here; the Membership package's, for a key held on a project, which the infrastructure
    /// project adds with the projects' registration; and the Tenancy package's for the cases that are Tenancy's,
    /// which the infrastructure project adds over the module's context with <c>AddTenancyAccess</c>.
    /// <para>
    /// The handlers themselves are not registered here. The host's mediator finds them in this assembly when the
    /// host compiles and registers them itself, so a request without a handler is a build error there, not a
    /// missing line here. The ports they read and save through are the infrastructure project's to register;
    /// Tenancy's answers and its catalogue come from Tenancy's own registration, which comes first.
    /// </para>
    /// <para>
    /// Behaviors run in the order they were registered. The host registers its own first, so this one runs
    /// inside them and right before the handler. Registered once however often the module is added.
    /// </para>
    /// <para>
    /// The texts are added here, with the module, and not by the host: a host that adds the module answers its
    /// refusals in the language a request asks for without naming a resource file of the module's. The localizer
    /// asks its sources in the order they were added, so a host that wants other words for a code adds a resource
    /// file of its own before it adds the module. Reading resource files needs <c>AddLocalization()</c>, which is
    /// the host's to call.
    /// </para>
    /// </remarks>
    /// <param name="services">The host's services.</param>
    /// <param name="membership">The projects' rules, made with the starter roles the application declares.</param>
    public static IServiceCollection AddProjectsApplication(this IServiceCollection services, ProjectMembership membership)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(membership);

        // Projects' keys, next to the code that asks for them. The application's catalogue lists the packs that
        // hold them; an administrators' pack that lists no keys holds them as it holds every key.
        services.AddTenancyPermissions(ProjectCatalogue.Permissions);

        // The one instance of the projects' rules: what the role commands hold a role to, and what the projects
        // are registered with in the infrastructure project.
        services.AddSingleton(membership);

        // The gate is the same object as the rules, so another module asking a question gets exactly the answer
        // this module's own commands act on.
        services.AddScoped<ProjectAccess>();
        services.AddScoped<IProjectGate>(serviceProvider => serviceProvider.GetRequiredService<ProjectAccess>());

        // What the commands ask of the organization: one per scope, and safe for the requests of a scope that run at once.
        services.AddScoped<ProjectTenancy>();

        // The check for the case only Projects can decide, a key at a unit, and the behavior that asks the module's
        // checks before every handler. The unit a request asked at a unit passed for it keeps for that request's
        // handler (Checked<T>), which is registered with the checks: a handler cannot be made where they were left out.
        services.AddAccessCheck<IProjectsRequest, ProjectsAccessCheck>();
        services.AddProjectsAccessBehavior();

        // What Projects refuses with, in English and Dutch, for the edge to phrase in its reader's language. The
        // codes a crew's rules refuse under are among them, in the module's own words.
        services.AddDDDToolkitLocalization(texts => texts.AddResource<ProjectFailures>());

        return services;
    }
}
