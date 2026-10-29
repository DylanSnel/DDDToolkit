using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DDDToolkit.EntityFramework.Tests.Conventions;

/// <summary>
/// A marker of a package names the convention that maps it, and <c>AddDDDToolkitConventions()</c> runs that convention
/// in every model with a marked class: once per model, for a marker on a class or on a property, and not at all where
/// nothing is marked. Every class the model maps counts: one reached only through a navigation, one the model owns, also
/// where two owners share it, and one declared with a generic marker. A convention the application does not have
/// refuses the model, naming what to reference.
/// </summary>
public sealed class MarkerConventionTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void A_marked_property_runs_the_convention_its_marker_names_once_per_model()
    {
        using var context = new ParcelsContext(_db.Options<ParcelsContext>());

        context.Model.FindAnnotation(StampConvention.Annotation)!.Value.Should().Be("Parcel, Sack", "the convention runs once, and finds every marked class itself");
    }

    [Fact]
    public void A_marked_class_runs_it_as_well()
    {
        using var context = new CratesContext(_db.Options<CratesContext>());

        context.Model.FindAnnotation(StampConvention.Annotation)!.Value.Should().Be("Crate");
    }

    [Fact]
    public void A_class_the_model_reaches_only_through_a_navigation_runs_it()
    {
        using var context = new ShelvesContext(_db.Options<ShelvesContext>());

        context.Model.FindAnnotation(StampConvention.Annotation)!.Value.Should().Be("Jar", "the context names the shelf alone, and the jars are mapped through it");
    }

    [Fact]
    public void An_owned_type_runs_it_also_where_two_owners_share_it()
    {
        using var once = new OneOwnerContext(_db.Options<OneOwnerContext>());
        once.Model.FindAnnotation(StampConvention.Annotation)!.Value.Should().Be("Tag");

        using var shared = new TwoOwnersContext(_db.Options<TwoOwnersContext>());
        shared.Model.GetEntityTypes().Where(entityType => entityType.ClrType == typeof(Tag)).Should().OnlyContain(entityType => entityType.HasSharedClrType);
        shared.Model.FindAnnotation(StampConvention.Annotation)!.Value.Should().Be("Tag, Tag", "each owner's tag is mapped of its own, over the one class");
    }

    [Fact]
    public void A_generic_marker_names_its_convention_on_its_definition()
    {
        using var context = new PalletsContext(_db.Options<PalletsContext>());

        context.Model.FindAnnotation(GenericStampConvention.Annotation)!.Value.Should().Be("Pallet");
    }

    [Fact]
    public void A_model_without_a_marked_class_is_not_touched()
    {
        using var context = new PlainContext(_db.Options<PlainContext>());

        context.Model.FindAnnotation(StampConvention.Annotation).Should().BeNull();
    }

    [Fact]
    public void A_convention_the_application_does_not_have_refuses_the_model_naming_what_to_reference()
    {
        using var context = new LostContext(_db.Options<LostContext>());

        FluentActions.Invoking(() => context.Model).Should().Throw<InvalidOperationException>().WithMessage(
            "'Lost.Label' is marked [Elsewhere], which Nowhere.EntityFramework maps, and the application does not have it. "
            + "Reference Nowhere.EntityFramework from the project of the context, or remove the marker.");
    }

    [Fact]
    public void A_marker_that_names_no_convention_refuses_the_model()
    {
        using var context = new MisnamedContext(_db.Options<MisnamedContext>());

        FluentActions.Invoking(() => context.Model).Should().Throw<InvalidOperationException>()
            .WithMessage("'Misnamed.Label' is marked [NotAConvention], which names the convention '" + typeof(Plain).FullName + "'.*IModelFinalizingConvention.");
    }

    /// <summary>Marks what <see cref="StampConvention"/> maps, as a package's marker in a domain project would.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
    [ModelConvention("DDDToolkit.EntityFramework.Tests.Conventions.MarkerConventionTests+StampConvention, DDDToolkit.EntityFramework.Tests")]
    public sealed class StampedAttribute : Attribute;

    /// <summary>A generic marker, as a package's template attribute is, that names its convention on its definition.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    [ModelConvention("DDDToolkit.EntityFramework.Tests.Conventions.MarkerConventionTests+GenericStampConvention, DDDToolkit.EntityFramework.Tests")]
    public sealed class StampedAsAttribute<T> : Attribute;

    /// <summary>A marker whose convention is in an assembly the application does not have.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    [ModelConvention("Nowhere.EntityFramework.ElsewhereConvention, Nowhere.EntityFramework")]
    public sealed class ElsewhereAttribute : Attribute;

    /// <summary>A marker that names a class that is no convention.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    [ModelConvention("DDDToolkit.EntityFramework.Tests.Conventions.MarkerConventionTests+Plain, DDDToolkit.EntityFramework.Tests")]
    public sealed class NotAConventionAttribute : Attribute;

    /// <summary>Writes, on the model, the names of the classes with a marked property or a marked class, in order.</summary>
    public sealed class StampConvention : IModelFinalizingConvention
    {
        public const string Annotation = "Tests:Stamped";

        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            var stamped = modelBuilder.Metadata.GetEntityTypes()
                .Where(entityType => Attribute.IsDefined(entityType.ClrType, typeof(StampedAttribute))
                                     || entityType.ClrType.GetProperties().Any(property => Attribute.IsDefined(property, typeof(StampedAttribute))))
                .Select(entityType => entityType.ClrType.Name)
                .Order(StringComparer.Ordinal);

            var already = modelBuilder.Metadata.FindAnnotation(Annotation) is not null;
            modelBuilder.HasAnnotation(Annotation, already ? "run twice" : string.Join(", ", stamped));
        }
    }

    /// <summary>Writes, on the model, the names of the classes declared with the generic marker, over any type argument.</summary>
    internal sealed class GenericStampConvention : IModelFinalizingConvention
    {
        public const string Annotation = "Tests:GenericStamped";

        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            var stamped = modelBuilder.Metadata.GetEntityTypes()
                .Where(entityType => entityType.ClrType.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().IsGenericType
                    && attribute.GetType().GetGenericTypeDefinition() == typeof(StampedAsAttribute<>)))
                .Select(entityType => entityType.ClrType.Name);

            modelBuilder.HasAnnotation(Annotation, string.Join(", ", stamped));
        }
    }

    public sealed class Shelf
    {
        public Guid Id { get; set; }

        public List<Jar> Jars { get; set; } = [];
    }

    public sealed class Jar
    {
        public Guid Id { get; set; }

        [Stamped]
        public string Label { get; set; } = string.Empty;
    }

    public sealed class Box
    {
        public Guid Id { get; set; }

        public Tag Tag { get; set; } = new();
    }

    public sealed class Bag
    {
        public Guid Id { get; set; }

        public Tag Tag { get; set; } = new();
    }

    public sealed class Tag
    {
        [Stamped]
        public string Text { get; set; } = string.Empty;
    }

    [StampedAs<int>]
    public sealed class Pallet
    {
        public Guid Id { get; set; }
    }

    public sealed class Parcel
    {
        public Guid Id { get; set; }

        [Stamped]
        public string Label { get; set; } = string.Empty;
    }

    public sealed class Sack
    {
        public Guid Id { get; set; }

        [Stamped]
        public string Label { get; set; } = string.Empty;
    }

    [Stamped]
    public sealed class Crate
    {
        public Guid Id { get; set; }
    }

    public sealed class Plain
    {
        public Guid Id { get; set; }
    }

    public sealed class Lost
    {
        public Guid Id { get; set; }

        [Elsewhere]
        public string Label { get; set; } = string.Empty;
    }

    public sealed class Misnamed
    {
        public Guid Id { get; set; }

        [NotAConvention]
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>A context with the toolkit's conventions; each test's own maps the classes it is about.</summary>
    private abstract class MarkedContext(DbContextOptions options) : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
            => configurationBuilder.AddDDDToolkitConventions();
    }

    private sealed class ParcelsContext(DbContextOptions<ParcelsContext> options) : MarkedContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Parcel>();
            modelBuilder.Entity<Sack>();
        }
    }

    private sealed class CratesContext(DbContextOptions<CratesContext> options) : MarkedContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Crate>();
    }

    private sealed class ShelvesContext(DbContextOptions<ShelvesContext> options) : MarkedContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Shelf>();
    }

    private sealed class OneOwnerContext(DbContextOptions<OneOwnerContext> options) : MarkedContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Box>().OwnsOne(row => row.Tag);
    }

    private sealed class TwoOwnersContext(DbContextOptions<TwoOwnersContext> options) : MarkedContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Box>().OwnsOne(row => row.Tag);
            modelBuilder.Entity<Bag>().OwnsOne(row => row.Tag);
        }
    }

    private sealed class PalletsContext(DbContextOptions<PalletsContext> options) : MarkedContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Pallet>();
    }

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : MarkedContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Plain>();
    }

    private sealed class LostContext(DbContextOptions<LostContext> options) : MarkedContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Lost>();
    }

    private sealed class MisnamedContext(DbContextOptions<MisnamedContext> options) : MarkedContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Misnamed>();
    }
}
