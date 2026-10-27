using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>
/// The model conventions the packages a context's assembly references ship (<see cref="ModelConventionAttribute"/>),
/// applied when its model is finished. Found from the context's own assembly and what it references, directly or
/// through another assembly, once for each assembly: the same at design time as when the application runs.
/// </summary>
/// <param name="services">The context's internal services, which name the context the model is built for.</param>
internal sealed class PackageConventions(IServiceProvider services) : IModelFinalizingConvention
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<Type>> Found = new();

    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        if (services.GetService<ICurrentDbContext>()?.Context.GetType().Assembly is not { } assembly)
        {
            return;
        }

        foreach (var type in Found.GetOrAdd(assembly, Discover))
        {
            var convention = Activator.CreateInstance(type) as IModelFinalizingConvention
                ?? throw new InvalidOperationException(
                    type.FullName + " is named by [assembly: ModelConvention] and is no IModelFinalizingConvention. A package's model convention runs when the model is finished.");
            convention.ProcessModelFinalizing(modelBuilder, context);
        }
    }

    /// <summary>
    /// The conventions named by <paramref name="start"/> and every assembly it references, directly or through another,
    /// each once, in the order they are found. The framework's own assemblies are passed over: they name none.
    /// </summary>
    private static IReadOnlyList<Type> Discover(Assembly start)
    {
        List<Type> conventions = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase) { start.GetName().Name ?? string.Empty };
        Queue<Assembly> next = new([start]);

        while (next.TryDequeue(out var assembly))
        {
            foreach (var named in assembly.GetCustomAttributes<ModelConventionAttribute>())
            {
                if (!conventions.Contains(named.Convention))
                {
                    conventions.Add(named.Convention);
                }
            }

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is not { } name || IsTheFrameworks(name) || !seen.Add(name))
                {
                    continue;
                }

                try
                {
                    next.Enqueue(Assembly.Load(reference));
                }
                catch (Exception failed) when (failed is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    // An assembly the application was compiled against and does not ship names no convention it needs.
                }
            }
        }

        return conventions;
    }

    private static bool IsTheFrameworks(string name)
        => name.StartsWith("System", StringComparison.Ordinal)
           || name.StartsWith("Microsoft.", StringComparison.Ordinal)
           || name is "mscorlib" or "netstandard";
}
