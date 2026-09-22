using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>Registers the DDDToolkit model conventions.</summary>
public static class ModelConfigurationBuilderExtensions
{
    /// <summary>
    /// Adds the DDDToolkit conventions to the model. Call it from <c>DbContext.ConfigureConventions</c>
    /// next to the generated <c>Add{Module}Converters()</c> of every assembly that declares ids or value
    /// objects:
    /// <code>
    /// protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    /// {
    ///     builder.AddDDDToolkitConventions();
    ///     builder.AddCommonConverters();   // generated, per assembly
    ///     builder.AddApiConverters();
    /// }
    /// </code>
    /// The conventions:
    /// <list type="bullet">
    ///   <item><see cref="AggregateRootVersionConvention"/>: <c>Version</c> of every aggregate root becomes a concurrency token.</item>
    ///   <item><see cref="ReadOnlyCollectionConvention"/>: generated <c>IReadOnlyList&lt;T&gt;</c> properties of primitives and value-converted ids are mapped as primitive collections, with the element converter applied.</item>
    ///   <item><see cref="InternalMemberConvention"/>: members marked <c>[Internal]</c> are never mapped.</item>
    ///   <item><see cref="KeyPartConvention"/>: properties marked <c>[KeyPart]</c> join the primary key ahead of <c>Id</c>, and the foreign key of every owned type below it. Types without key parts are not touched.</item>
    /// </list>
    /// The converters stay a separate, generated call because they are produced per assembly; the
    /// conventions are the same for every context.
    /// </summary>
    public static ModelConfigurationBuilder AddDDDToolkitConventions(this ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Conventions.Add(static _ => new InternalMemberConvention());
        configurationBuilder.Conventions.Add(static _ => new ReadOnlyCollectionConvention());
        configurationBuilder.Conventions.Add(static _ => new AggregateRootVersionConvention());
        configurationBuilder.Conventions.Add(static _ => new KeyPartConvention());

        return configurationBuilder;
    }
}
