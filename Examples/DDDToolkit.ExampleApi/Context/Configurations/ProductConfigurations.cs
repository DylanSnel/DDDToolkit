using DDDToolkit.ExampleApi.Domain.ProductAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDDToolkit.ExampleApi.Context.Configurations;
internal class ProductConfigurations : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
                 .ValueGeneratedNever();
    }

    //private static void ConfigureSeekerSeekerProfileIdsTable(EntityTypeBuilder<Seeker> builder)
    //{
    //    builder.Navigation(e => e.Profiles)
    //        .HasField("_profileIds")  // Explicitly specifying the backing field
    //        .UsePropertyAccessMode(PropertyAccessMode.Field);  // Indicate that the field should be used during construction of the entity

    //    builder.OwnsMany(h => h.Profiles, sp =>
    //    {

    //        sp.ToTable("SeekerSeekerProfileIds");
    //        sp.WithOwner().HasForeignKey("SeekerId");
    //        sp.HasKey("Id");
    //        sp.Property(mi => mi.Value)
    //          .ValueGeneratedNever()
    //          .HasColumnName("SeekerProfileId");
    //    });
    //    //builder.Metadata.FindNavigation(nameof(Seeker.Profiles))!
    //    //   .SetPropertyAccessMode(PropertyAccessMode.Field);
    //}


    //private static void ConfigureSeekersTable(EntityTypeBuilder<Seeker> builder)
    //{
    //    //builder.ToTable("Seekers");
    //    builder.HasKey(s => s.Id);

    //    //builder.Property(s => s.Id)
    //    //    .ValueGeneratedNever()
    //    //    .HasConversion(
    //    //        id => id.Value,
    //    //        value => SeekerId.Create(value));

    //    // Assuming PersonName has properties like First and Last.
    //    builder.ComplexProperty(s => s.Name, pn =>
    //    {
    //        pn.Property(name => name.FirstName).HasColumnName("FirstName").HasMaxLength(50);
    //        pn.Property(name => name.MiddleNames).HasColumnName("MiddleNames").HasMaxLength(50)
    //        ;
    //        pn.Property(name => name.LastName).HasColumnName("LastName").HasMaxLength(50);
    //    });
    //}

    //private static void ConfigureSeekerLocationTable(EntityTypeBuilder<Seeker> builder)
    //{
    //    builder.OwnsOne(m => m.Location, sb =>
    //    {
    //        //sb.ToJson();
    //        sb.ToTable("SeekerLocations");
    //        sb.WithOwner().HasForeignKey("SeekerId");
    //        sb.HasKey(x => x.Id);

    //        sb.Property(s => s.Id)
    //            .HasColumnName("LocationId")
    //            .ValueGeneratedNever()
    //            .HasConversion(
    //                id => id.Value,
    //                value => LocationId.Create(value));

    //        sb.Property(s => s.Name)
    //            .HasMaxLength(100);

    //        sb.Property(s => s.City)
    //            .HasMaxLength(250);
    //    });

    //    builder.Metadata.FindNavigation(nameof(Seeker.Location))!
    //        .SetPropertyAccessMode(PropertyAccessMode.Field);
    //}    //private static void ConfigureSeekerSeekerProfileIdsTable(EntityTypeBuilder<Seeker> builder)
    //{
    //    builder.Navigation(e => e.Profiles)
    //        .HasField("_profileIds")  // Explicitly specifying the backing field
    //        .UsePropertyAccessMode(PropertyAccessMode.Field);  // Indicate that the field should be used during construction of the entity

    //    builder.OwnsMany(h => h.Profiles, sp =>
    //    {

    //        sp.ToTable("SeekerSeekerProfileIds");
    //        sp.WithOwner().HasForeignKey("SeekerId");
    //        sp.HasKey("Id");
    //        sp.Property(mi => mi.Value)
    //          .ValueGeneratedNever()
    //          .HasColumnName("SeekerProfileId");
    //    });
    //    //builder.Metadata.FindNavigation(nameof(Seeker.Profiles))!
    //    //   .SetPropertyAccessMode(PropertyAccessMode.Field);
    //}


    //private static void ConfigureSeekersTable(EntityTypeBuilder<Seeker> builder)
    //{
    //    //builder.ToTable("Seekers");
    //    builder.HasKey(s => s.Id);

    //    //builder.Property(s => s.Id)
    //    //    .ValueGeneratedNever()
    //    //    .HasConversion(
    //    //        id => id.Value,
    //    //        value => SeekerId.Create(value));

    //    // Assuming PersonName has properties like First and Last.
    //    builder.ComplexProperty(s => s.Name, pn =>
    //    {
    //        pn.Property(name => name.FirstName).HasColumnName("FirstName").HasMaxLength(50);
    //        pn.Property(name => name.MiddleNames).HasColumnName("MiddleNames").HasMaxLength(50)
    //        ;
    //        pn.Property(name => name.LastName).HasColumnName("LastName").HasMaxLength(50);
    //    });
    //}

    //private static void ConfigureSeekerLocationTable(EntityTypeBuilder<Seeker> builder)
    //{
    //    builder.OwnsOne(m => m.Location, sb =>
    //    {
    //        //sb.ToJson();
    //        sb.ToTable("SeekerLocations");
    //        sb.WithOwner().HasForeignKey("SeekerId");
    //        sb.HasKey(x => x.Id);

    //        sb.Property(s => s.Id)
    //            .HasColumnName("LocationId")
    //            .ValueGeneratedNever()
    //            .HasConversion(
    //                id => id.Value,
    //                value => LocationId.Create(value));

    //        sb.Property(s => s.Name)
    //            .HasMaxLength(100);

    //        sb.Property(s => s.City)
    //            .HasMaxLength(250);
    //    });

    //    builder.Metadata.FindNavigation(nameof(Seeker.Location))!
    //        .SetPropertyAccessMode(PropertyAccessMode.Field);
    //}
}
