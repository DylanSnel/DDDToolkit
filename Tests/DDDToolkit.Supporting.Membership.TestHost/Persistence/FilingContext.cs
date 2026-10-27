using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.TestHost.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Persistence;

/// <summary>
/// The application's context: a plain context in a schema of its own, with both kinds of resource in it. A document's
/// members are mapped by the package's convention, which <c>AddDDDToolkitConventions()</c> brings, under the names the
/// package gives them; a folder's by <c>HasMembers</c> by hand, under names of the application's own and its own word for
/// who a member is, which wins over the convention.
/// </summary>
/// <param name="options">The context's options.</param>
public sealed class FilingContext(DbContextOptions<FilingContext> options) : DbContext(options)
{
    /// <summary>The schema the tables are in.</summary>
    public const string Schema = "filing";

    /// <summary>The table of the staff on folders, in the application's own words.</summary>
    public const string FolderStaffTable = "FolderStaff";

    /// <summary>The table of the roles staff hold on folders.</summary>
    public const string FolderStaffRolesTable = "FolderStaffRoles";

    /// <summary>The column that says which member of staff a row of <see cref="FolderStaffTable"/> is about.</summary>
    public const string StaffColumn = "StaffCode";

    /// <summary>The documents.</summary>
    public DbSet<Document> Documents => Set<Document>();

    /// <summary>The folders.</summary>
    public DbSet<Folder> Folders => Set<Folder>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Document>(document =>
        {
            document.Property(row => row.Id).ValueGeneratedNever();
            document.Property(row => row.Title).HasMaxLength(200);

            // Nothing about the shares: a collection of a member class is mapped by the convention, as HasMembers maps it.
        });

        modelBuilder.Entity<Folder>(folder =>
        {
            folder.Property(row => row.Id).ValueGeneratedNever();
            folder.HasMembers(row => row.Staff, new MemberTableNames(FolderStaffTable, FolderStaffRolesTable, StaffColumn));
        });
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddFilingConverters();
    }
}
