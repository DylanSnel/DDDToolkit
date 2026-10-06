using System.Runtime.CompilerServices;
using DDDToolkit.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// The parts of a context a model cannot do without. A package's mapping says so of the model it maps into, and a
/// context of that model whose options lack the part is refused, at its first save and by the start-up check
/// <c>entity-framework.toolkit-wired</c>, rather than saving without what the part does.
/// <code>
/// // In a package's mapping, next to what makes the part necessary
/// entity.Metadata.Model.RequireContextPart(
///     "billing.audit",
///     typeof(BillingAuditInterceptor),
///     registeredWith: "services.AddBillingAudit() of Billing.EntityFramework",
///     addedWith: "options.UseBillingAudit(serviceProvider)");
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// A part is brought by a registration and put on a context by <c>UseDDDToolkit</c>, and a context gets every part
/// that is registered, whatever its model asks for. So the two can come apart: a module's context that keeps rows to
/// a tenant, in a host where nothing registered Tenancy, gets no save check from the one call, where a forgotten
/// <c>UseTenancy</c> used to throw. The requirement travels with the model instead, the one place that knows: the
/// context is refused, naming the registration that brings the part where none did, and the calls that put it on
/// where one did.
/// </para>
/// <para>
/// A part is recognised on a context by the interceptor it adds, by the name of its type or of a type it derives
/// from, so a package states it without this assembly knowing the package. The requirement is a model annotation
/// whose value is text, so a model with one still goes into a migration's snapshot and a compiled model.
/// </para>
/// </remarks>
public static class ContextPartRequirements
{
    /// <summary>The prefix of the annotation that states a requirement: the part's name follows it.</summary>
    public const string AnnotationPrefix = "DDDToolkit:RequiresContextPart:";

    /// <summary>What separates the interceptor's type, the registration and the call in the annotation's value: a line break, which none of them holds.</summary>
    private const char Separator = '\n';

    /// <summary>The requirements of each model, read once: a model is built once per context type and then shared.</summary>
    private static readonly ConditionalWeakTable<IModel, Requirement[]> OfModels = new();

    /// <summary>
    /// States that a context of <paramref name="model"/> cannot do without the part <paramref name="part"/>, which
    /// adds an interceptor of <paramref name="interceptor"/>, a type that derives from it included. Stating it again
    /// changes nothing.
    /// </summary>
    /// <param name="model">The model being built: <c>modelBuilder.Model</c>, or <c>entity.Metadata.Model</c>.</param>
    /// <param name="part">The part's name, as its registration names it (<see cref="ContextPart{TBuilder}.Name"/>).</param>
    /// <param name="interceptor">The interceptor the part adds, by which a context is seen to have it.</param>
    /// <param name="registeredWith">The registration that brings the part, as a developer writes it: what a context is told where nothing registered it.</param>
    /// <param name="addedWith">The part's own call on a context's options, as a developer writes it: what a context given the base alone is told.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="part"/>, <paramref name="registeredWith"/> or <paramref name="addedWith"/> is empty, or holds a line break.</exception>
    public static void RequireContextPart(this IMutableModel model, string part, Type interceptor, string registeredWith, string addedWith)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(part);
        ArgumentNullException.ThrowIfNull(interceptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredWith);
        ArgumentException.ThrowIfNullOrWhiteSpace(addedWith);

        if (registeredWith.Contains(Separator, StringComparison.Ordinal) || addedWith.Contains(Separator, StringComparison.Ordinal))
        {
            throw new ArgumentException("What a context is told to write is one line.", registeredWith.Contains(Separator, StringComparison.Ordinal) ? nameof(registeredWith) : nameof(addedWith));
        }

        model.SetAnnotation(AnnotationPrefix + part, string.Join(Separator, interceptor.FullName ?? interceptor.Name, registeredWith, addedWith));
    }

    /// <summary>
    /// Throws when <paramref name="context"/>'s model requires a part its options do not have: where no registration
    /// brought the part, naming the one that does, and where one did, naming the calls that put it on.
    /// <c>AddDDDToolkitEntityFramework</c> holds every context to it: the toolkit's first interceptor asks it before
    /// every save, and the start-up check <c>entity-framework.toolkit-wired</c> before the first request.
    /// </summary>
    /// <param name="context">The context to check; it is not opened.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The model requires a part the context's options do not have.</exception>
    public static void EnsureRequiredParts(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var required = OfModels.GetValue(context.Model, static model => Read(model));
        if (required.Length == 0)
        {
            return;
        }

        var core = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();
        var interceptors = core?.Interceptors ?? [];
        foreach (var requirement in required)
        {
            if (!interceptors.Any(present => Is(present.GetType(), requirement.Interceptor)))
            {
                throw Missing(context, requirement, core?.ApplicationServiceProvider);
            }
        }
    }

    /// <summary>The requirements <paramref name="model"/> states, in the order of the parts' names.</summary>
    private static Requirement[] Read(IModel model)
        => [.. model.GetAnnotations()
            .Where(annotation => annotation.Name.StartsWith(AnnotationPrefix, StringComparison.Ordinal) && annotation.Value is string)
            .Select(annotation => (Part: annotation.Name[AnnotationPrefix.Length..], Said: ((string)annotation.Value!).Split(Separator)))
            .Where(stated => stated.Said.Length == 3)
            .Select(stated => new Requirement(stated.Part, stated.Said[0], stated.Said[1], stated.Said[2]))
            .OrderBy(requirement => requirement.Part, StringComparer.Ordinal)];

    /// <summary>Whether <paramref name="type"/> is the type named <paramref name="name"/>, or derives from it.</summary>
    private static bool Is(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.FullName ?? current.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What a context without a part its model requires is told: the registration to add where none brought the
    /// part, and the calls that put it on where one did. Where the context was not made by the application's
    /// services, which say what is registered, both.
    /// </summary>
    private static InvalidOperationException Missing(DbContext context, Requirement requirement, IServiceProvider? services)
    {
        var lead = $"'{context.GetType().Name}' cannot do without the part {requirement.Part}, which its model requires, and its options have no {Short(requirement.Interceptor)}, so its saves would go without it. ";
        var register = $"Register it with {requirement.RegisteredWith}, and options.UseDDDToolkit(serviceProvider) puts it on every context.";
        var wire = $"Configure the context with options.UseDDDToolkit(serviceProvider), which puts on every part the registrations brought; after options.UseDDDToolkitCore(serviceProvider), add {requirement.AddedWith}.";

        bool? registered = services is null
            ? null
            : (services.GetService(typeof(ContextParts<DbContextOptionsBuilder>)) as ContextParts<DbContextOptionsBuilder>)?.InOrder()
                .Any(part => string.Equals(part.Name, requirement.Part, StringComparison.Ordinal)) ?? false;

        return new InvalidOperationException(registered switch
        {
            false => lead + "No registration brought it. " + register,
            true => lead + wire,
            null => lead + register + " " + wire,
        });
    }

    /// <summary>The name of a type without its namespace, or the type it is nested in, as a message names it.</summary>
    private static string Short(string fullName)
    {
        var generic = fullName.IndexOf('`', StringComparison.Ordinal);
        var name = generic < 0 ? fullName : fullName[..generic];
        return name[(name.LastIndexOfAny(['.', '+']) + 1)..];
    }

    /// <summary>One part a model requires: its name, the interceptor's type by name, the registration that brings it and its own call.</summary>
    private sealed record Requirement(string Part, string Interceptor, string RegisteredWith, string AddedWith);
}
