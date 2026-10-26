using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.TestHost.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Persistence;

/// <summary>
/// The application's context: a plain context in a schema of its own, with both kinds of resource in it and the
/// members of each mapped by <c>HasMembers</c>. A document's member tables take the names the package gives
/// them; a folder's take names of the application's own, and its own word for who a member is.
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

            // Every type argument is inferred: the member class from the collection, the member's id from the owner.
            document.HasMembers(row => row.Shares, row => row.OwnerId);
        });

        modelBuilder.Entity<Folder>(folder =>
        {
            folder.Property(row => row.Id).ValueGeneratedNever();
            folder.HasMembers(row => row.Staff, row => row.Keeper, new MemberTableNames(FolderStaffTable, FolderStaffRolesTable, StaffColumn));
        });
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddFilingConverters();
    }
}
