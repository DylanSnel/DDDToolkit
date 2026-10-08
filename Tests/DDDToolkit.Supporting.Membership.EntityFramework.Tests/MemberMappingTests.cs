using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.Supporting.Membership.TestHost.Binders;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// What <c>HasMembers</c> maps: the two member tables of a resource in the application's own context, under
/// the application's names, and the mark it leaves, which is how everything else finds the members of that
/// resource and of no other.
/// </summary>
public sealed class MemberMappingTests
{
    private static IModel Model()
    {
        using var filing = new SqliteFiling();
        using var scope = filing.Services.Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<FilingContext>().Model;
    }

    private static string[] Columns(IEntityType entity)
        => [.. entity.GetProperties().Select(property => property.GetColumnName()).Order(StringComparer.Ordinal)];

    private static string[] Key(IEntityType entity) => [.. entity.FindPrimaryKey()!.Properties.Select(property => property.Name)];

    [Fact]
    public void The_members_of_a_resource_are_two_tables_owned_by_it()
    {
        var model = Model();
        var mapping = MembershipModel.MembersOf(model.FindEntityType(typeof(Document))!)!;

        // Named after the resource and its collection, and after the member class, when the host names nothing.
        (mapping.Members.GetTableName(), mapping.Members.GetSchema()).Should().Be(("DocumentShares", FilingContext.Schema));
        (mapping.Roles.GetTableName(), mapping.Roles.GetSchema()).Should().Be(("DocumentShareRoles", FilingContext.Schema));

        // Owned, the members by the document and the roles by the member: loaded and saved with the document,
        // under its one version, and written under whatever rules the document's rows are.
        mapping.Members.FindOwnership()!.PrincipalEntityType.Should().BeSameAs(mapping.Resource);
        mapping.Roles.FindOwnership()!.PrincipalEntityType.Should().BeSameAs(mapping.Members);
        mapping.MemberClass.Should().Be(typeof(DocumentShare));

        Columns(mapping.Members).Should().Equal("AddedBy", "DocumentId", "EndsAt", "Id", "MemberId", "StartsAt");
        Columns(mapping.Roles).Should().Equal("DocumentId", "DocumentShareId", "EndsAt", "GivenBy", "RoleId", "StartsAt");
    }

    [Fact]
    public void A_member_row_is_known_by_its_resource_and_its_id_and_a_role_by_its_member_and_the_role()
    {
        var mapping = MembershipModel.MembersOf(Model().FindEntityType(typeof(Document))!)!;

        Key(mapping.Members).Should().Equal("DocumentId", "Id");
        Key(mapping.Roles).Should().Equal(["DocumentId", "DocumentShareId", "RoleId"], "a member holds a role once, in the database as well");
        mapping.Roles.FindOwnership()!.Properties.Select(property => property.Name).Should().Equal("DocumentId", "DocumentShareId");

        // "Which documents am I a member of" is asked for every list, by the member.
        mapping.Members.GetIndexes().Should().ContainSingle(index => index.Properties.Count == 1 && index.Properties[0].Name == "MemberId");
        mapping.Members.FindProperty("Id")!.ValueGenerated.Should().Be(ValueGenerated.Never, "the member list gives a row its id");
    }

    [Fact]
    public void The_tables_and_the_members_column_take_the_hosts_own_names()
    {
        var mapping = MembershipModel.MembersOf(Model().FindEntityType(typeof(Folder))!)!;

        mapping.Members.GetTableName().Should().Be(FilingContext.FolderStaffTable);
        mapping.Roles.GetTableName().Should().Be(FilingContext.FolderStaffRolesTable);
        mapping.Members.FindProperty("MemberId")!.GetColumnName().Should().Be(FilingContext.StaffColumn);

        // Everything else is named as the context names things, and what the host added to its member class is a column like any other.
        Columns(mapping.Members).Should().Equal("AddedBy", "EndsAt", "FolderId", "Id", "Note", "StaffCode", "StartsAt");
        Columns(mapping.Roles).Should().Equal("EndsAt", "FolderId", "FolderMemberId", "GivenBy", "RoleId", "StartsAt");
        Key(mapping.Roles).Should().Equal("FolderId", "FolderMemberId", "RoleId");
    }

    [Fact]
    public void The_member_tables_are_in_the_schema_the_resources_table_is_in()
    {
        var model = ModelOf(document =>
        {
            document.ToTable("Papers", "office");
            document.HasMembers(row => row.Shares, row => row.OwnerId, new MemberTableNames(Roles: "PaperRoles"));
        });
        var mapping = MembershipModel.MembersOf(model.FindEntityType(typeof(Document))!)!;

        (mapping.Members.GetSchema(), mapping.Members.GetTableName()).Should().Be(("office", "DocumentShares"));
        (mapping.Roles.GetSchema(), mapping.Roles.GetTableName()).Should().Be(("office", "PaperRoles"), "a name the host gives is the name, and the rest keeps the package's");
    }

    [Fact]
    public void A_resource_known_by_more_than_its_id_keeps_its_members_under_its_whole_key()
    {
        // A key of two parts, said before the members are mapped: a member's row carries both, and a role's row carries them on.
        var model = ModelOf(document =>
        {
            document.HasKey(nameof(Document.Title), nameof(Document.Id));
            document.HasMembers(row => row.Shares, row => row.OwnerId);
        });
        var mapping = MembershipModel.MembersOf(model.FindEntityType(typeof(Document))!)!;

        Key(mapping.Members).Should().Equal("DocumentTitle", "DocumentId", "Id");
        Key(mapping.Roles).Should().Equal("DocumentTitle", "DocumentId", "DocumentShareId", "RoleId");
        mapping.Roles.FindOwnership()!.Properties.Select(property => property.Name).Should().Equal("DocumentTitle", "DocumentId", "DocumentShareId");
        mapping.Roles.FindOwnership()!.PrincipalKey.Properties.Select(property => property.Name).Should().Equal("DocumentTitle", "DocumentId", "Id");
    }

    [Fact]
    public void A_resource_with_a_key_part_carries_it_into_both_member_tables()
    {
        // A binder is known by its shelf and its id: the part joins its key when the model is finished, after
        // its members were mapped. The member tables are keyed under the whole of it all the same.
        var model = ShelvesOf(binder => binder.HasMembers(row => row.Borrowers, row => row.OwnerId));
        var binder = model.FindEntityType(typeof(Binder))!;
        var mapping = MembershipModel.MembersOf(binder)!;

        Key(binder).Should().Equal("ShelfId", "Id");
        binder.GetKeys().Should().ContainSingle("the key a binder had while its members were mapped is gone, not kept beside the one it has");

        Key(mapping.Members).Should().Equal("ShelfId", "BinderId", "Id");
        var toBinder = mapping.Members.FindOwnership()!;
        toBinder.Properties.Select(property => property.Name).Should().Equal("ShelfId", "BinderId");
        toBinder.PrincipalKey.Should().BeSameAs(binder.FindPrimaryKey(), "a member's row points at its binder by the binder's whole key");

        Key(mapping.Roles).Should().Equal("ShelfId", "BinderId", "BinderBorrowerId", "RoleId");
        var toMember = mapping.Roles.FindOwnership()!;
        toMember.Properties.Select(property => property.Name).Should().Equal("ShelfId", "BinderId", "BinderBorrowerId");
        toMember.PrincipalKey.Should().BeSameAs(mapping.Members.FindPrimaryKey());

        Columns(mapping.Members).Should().Equal("AddedBy", "BinderId", "EndsAt", "Id", "MemberId", "ShelfId", "StartsAt");
        Columns(mapping.Roles).Should().Equal("BinderBorrowerId", "BinderId", "EndsAt", "GivenBy", "RoleId", "ShelfId", "StartsAt");
        mapping.Members.FindProperty("ShelfId")!.ClrType.Should().Be(typeof(ShelfId));
    }

    [Fact]
    public async Task A_resource_with_a_key_part_is_saved_and_read_with_its_members_and_their_roles()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(cancellation);
        var options = new DbContextOptionsBuilder<ShelvesContext>().UseSqlite(connection).Options;
        void Map(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Binder> binder) => binder.HasMembers(row => row.Borrowers, row => row.OwnerId);

        var now = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var (upper, lower) = (ShelfId.CreateSequential(), ShelfId.CreateSequential());
        var (ada, ben) = (UserId.CreateSequential(), UserId.CreateSequential());
        var folio = new Binder(upper, BinderId.CreateSequential(), ada, BinderMembership.Owner, now);
        folio.LendTo(ben, BinderMembership.Borrower, MemberPeriod.Between(now, now.AddDays(7)), now, by: ada);
        var atlas = new Binder(lower, BinderId.CreateSequential(), ben, BinderMembership.Owner, now);

        await using (var writing = new ShelvesContext(options, Map))
        {
            await writing.Database.EnsureCreatedAsync(cancellation);
            writing.AddRange(folio, atlas);
            await writing.SaveChangesAsync(cancellation);
        }

        // Read on another context: the members and the roles came back with their binder, each row under its shelf.
        await using (var reading = new ShelvesContext(options, Map))
        {
            var read = await reading.Set<Binder>().SingleAsync(binder => binder.Id == folio.Id, cancellation);
            read.ShelfId.Should().Be(upper);
            read.Borrowers.Select(row => (row.MemberId, Roles: string.Join(",", row.Roles.Select(held => held.RoleId.Value))))
                .Should().BeEquivalentTo([(ada, "owner"), (ben, "reader")]);
            (await reading.Database.SqlQuery<int>($"SELECT count(*) AS \"Value\" FROM \"BinderBorrowers\" WHERE \"ShelfId\" = {upper.Value} AND \"BinderId\" = {folio.Id.Value}").SingleAsync(cancellation))
                .Should().Be(2);
            (await reading.Database.SqlQuery<int>($"SELECT count(*) AS \"Value\" FROM \"BinderBorrowerRoles\" WHERE \"ShelfId\" = {upper.Value}").SingleAsync(cancellation))
                .Should().Be(2);

            // And a change of the members is saved under the same key: the loan and its role are gone, the owner's stay.
            read.TakeBackFrom(ben);
            await reading.SaveChangesAsync(cancellation);
        }

        await using var after = new ShelvesContext(options, Map);
        (await after.Set<Binder>().SingleAsync(binder => binder.Id == folio.Id, cancellation)).Borrowers.Should().ContainSingle().Which.MemberId.Should().Be(ada);
        (await after.Set<Binder>().SingleAsync(binder => binder.Id == atlas.Id, cancellation)).Borrowers.Should().ContainSingle().Which.Roles.Should().ContainSingle();
    }

    [Fact]
    public void A_resource_whose_key_is_said_after_its_members_is_refused_where_its_members_are_read()
    {
        // The other order: the members are mapped under the key the resource has then, and a key said
        // afterwards does not reach their tables. Refused where the mapping is read, with what to write.
        var model = ModelOf(document =>
        {
            document.HasMembers(row => row.Shares, row => row.OwnerId);
            document.HasKey(nameof(Document.Title), nameof(Document.Id));
        });

        FluentActions.Invoking(() => MembershipModel.MembersOf(model.FindEntityType(typeof(Document))!))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Document was keyed after its members were mapped*Say the key first: call HasKey before HasMembers*");

        // A key part is no such key: it is the resource's own declaration, known when the members are mapped.
        FluentActions.Invoking(() => MembershipModel.MembersOf(ShelvesOf(binder => binder.HasMembers(row => row.Borrowers, row => row.OwnerId)).FindEntityType(typeof(Binder))!))
            .Should().NotThrow();
    }

    [Fact]
    public void Periods_are_stored_as_instants_and_a_named_role_as_its_name()
    {
        var mapping = MembershipModel.MembersOf(Model().FindEntityType(typeof(Document))!)!;

        foreach (var entity in new[] { mapping.Members, mapping.Roles })
        {
            entity.FindProperty("StartsAt")!.GetValueConverter().Should().BeOfType<UtcDateTimeOffsetConverter>("a database that keeps no offsets compares a period in SQL");
            entity.FindProperty("EndsAt")!.GetValueConverter().Should().BeOfType<NullableUtcDateTimeOffsetConverter>();
            entity.FindProperty("EndsAt")!.IsNullable.Should().BeTrue("a period may have no end");
        }

        var role = mapping.Roles.FindProperty("RoleId")!;
        role.GetValueConverter().Should().BeOfType<SingleValueConverter<NamedRole, string>>("the role's name is the package's own id, which no registration of the host's stores");
        role.GetMaxLength().Should().Be(NamedRole.MaxLength);
    }

    [Fact]
    public async Task Who_a_membership_is_of_is_written_once_and_a_save_that_moved_it_to_another_member_is_refused()
    {
        // Fixed once the row is there, for every member class: the member list never moves a row to another
        // member, and a row moved so would hand that member its roles, with who added it and gave them kept.
        var model = Model();
        foreach (var resource in new[] { typeof(Document), typeof(Folder) })
        {
            MembershipModel.MembersOf(model.FindEntityType(resource)!)!.Members.FindProperty("MemberId")!.GetAfterSaveBehavior()
                .Should().Be(PropertySaveBehavior.Throw, resource.Name);
        }

        // A save that moved Ben's share of the minutes to somebody else, by whatever went round the member list.
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var stranger = UserId.CreateSequential();
        var moved = await FluentActions.Awaiting(() => filing.Services.ChangeAsync(
                data.Minutes,
                document => Break.Set(document.Shares.Single(share => share.MemberId == data.Ben), nameof(DocumentShare.MemberId), stranger)))
            .Should().ThrowAsync<InvalidOperationException>();
        moved.Which.Message.Should().Contain(nameof(DocumentShare.MemberId));

        (await filing.Services.ReadAsync(data.Minutes)).Shares.Select(share => share.MemberId).Should().Contain(data.Ben).And.NotContain(stranger);
    }

    [Fact]
    public void The_mapping_leaves_its_mark_on_the_resource_and_on_nothing_else()
    {
        var model = Model();
        var document = model.FindEntityType(typeof(Document))!;
        var folder = model.FindEntityType(typeof(Folder))!;

        document.FindAnnotation(MembershipModel.MembersAnnotation)!.Value.Should().Be(nameof(Document.Shares));
        document.FindAnnotation(MembershipModel.OwnerAnnotation)!.Value.Should().Be(nameof(Document.OwnerId));
        folder.FindAnnotation(MembershipModel.MembersAnnotation)!.Value.Should().Be(nameof(Folder.Staff));
        folder.FindAnnotation(MembershipModel.OwnerAnnotation)!.Value.Should().Be(nameof(Folder.Keeper));

        var documents = MembershipModel.MembersOf(document)!;
        (documents.Navigation.Name, documents.Owner.Name).Should().Be((nameof(Document.Shares), nameof(Document.OwnerId)));

        // The member tables themselves are no resource.
        MembershipModel.MembersOf(documents.Members).Should().BeNull();
        MembershipModel.MembersOf(documents.Roles).Should().BeNull();
    }

    [Fact]
    public void Each_resources_members_are_found_by_its_own_member_class()
    {
        var model = Model();

        var shares = MembershipModel.For(model, typeof(DocumentShare))!;
        var staff = MembershipModel.For(model, typeof(FolderMember))!;

        shares.Resource.ClrType.Should().Be(typeof(Document));
        staff.Resource.ClrType.Should().Be(typeof(Folder));
        shares.Members.Should().NotBeSameAs(staff.Members);
        shares.Roles.Should().NotBeSameAs(staff.Roles);
        new[] { shares.Members.GetTableName(), shares.Roles.GetTableName(), staff.Members.GetTableName(), staff.Roles.GetTableName() }
            .Should().OnlyHaveUniqueItems("two kinds of resource share no table");

        // A class no resource of this model has members of: none of its business.
        MembershipModel.For(model, typeof(Document)).Should().BeNull();
        MembershipModel.For(model, typeof(string)).Should().BeNull();
    }

    [Fact]
    public void A_class_that_was_not_declared_with_the_member_template_is_refused()
    {
        var builder = new ModelBuilder();

        FluentActions.Invoking(() => builder.Entity<Shelf>().HasMembers(shelf => shelf.Books, shelf => shelf.Owner))
            .Should().Throw<ArgumentException>()
            .WithMessage("Book is not a member class*[Member<BookId, UserId, NamedRole, Shelf>]*");
    }

    [Fact]
    public void An_owner_is_known_by_what_the_members_are_known_by()
    {
        var builder = new ModelBuilder();

        FluentActions.Invoking(() => builder.Entity<Document>().HasMembers(document => document.Shares, document => document.Id))
            .Should().Throw<ArgumentException>()
            .WithMessage("The owner of Document is a DocumentId, and its members are known by a UserId*");
    }

    [Fact]
    public void A_resource_has_one_member_list_and_its_parts_are_named_as_properties()
    {
        FluentActions.Invoking(() => ModelOf(document =>
            {
                document.HasMembers(row => row.Shares, row => row.OwnerId);
                document.HasMembers(row => row.Shares, row => row.OwnerId);
            }))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Document has members already, in Shares*");

        FluentActions.Invoking(() => ModelOf(document => document.HasMembers(row => row.Shares.Where(share => share.EndsAt == null), row => row.OwnerId)))
            .Should().Throw<ArgumentException>()
            .WithMessage("*is not a property of the resource*");
    }

    [Fact]
    public void Where_a_resource_sits_is_marked_with_its_members_and_changes_nothing_of_their_tables()
    {
        using var depot = new SqliteDepot();
        using var scope = depot.Services.Provider.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<DepotContext>().Model;
        var crate = model.FindEntityType(typeof(Crate))!;
        var pallet = model.FindEntityType(typeof(Pallet))!;

        // A crate says where it sits, next to its members and its owner. A pallet sits nowhere, and says nothing.
        crate.FindAnnotation(MembershipModel.AtAnnotation)!.Value.Should().Be(nameof(Crate.BayId));
        pallet.FindAnnotation(MembershipModel.AtAnnotation).Should().BeNull();

        var crates = MembershipModel.MembersOf(crate)!;
        (crates.At!.Name, crates.At.ClrType, crates.At.DeclaringType).Should().Be((nameof(Crate.BayId), typeof(BayId), crate));
        (crates.Navigation.Name, crates.Owner.Name).Should().Be((nameof(Crate.Porters), nameof(Crate.OwnerId)));
        MembershipModel.MembersOf(pallet)!.At.Should().BeNull();
        MembershipModel.For(model, typeof(CratePorter))!.At.Should().BeSameAs(crates.At, "found by the member class, a resource is the same one");

        // The place is a column of the resource, and of nothing else: the member tables are what they are without it.
        Columns(crates.Members).Should().Equal("AddedBy", "CrateId", "EndsAt", "Id", "MemberId", "StartsAt");
        Columns(crates.Roles).Should().Equal("CrateId", "CratePorterId", "EndsAt", "GivenBy", "RoleId", "StartsAt");
        crates.Roles.FindProperty("RoleId")!.ClrType.Should().Be(typeof(DepotRoleId), "a role that is kept elsewhere is held by its id");
    }

    [Fact]
    public void Where_a_resource_sits_is_named_as_a_property_of_the_resource()
    {
        // What a place is known by is the resource's own to say: here a document sits at its owner.
        var model = ModelOf(document => document.HasMembers(row => row.Shares, row => row.OwnerId, at: row => row.OwnerId, new MemberTableNames(Roles: "PaperRoles")));
        var mapping = MembershipModel.MembersOf(model.FindEntityType(typeof(Document))!)!;
        (mapping.At!.Name, mapping.At.ClrType).Should().Be((nameof(Document.OwnerId), typeof(UserId)));
        mapping.Roles.GetTableName().Should().Be("PaperRoles", "the host's names are taken as they are without a place");

        FluentActions.Invoking(() => ModelOf(document => document.HasMembers(row => row.Shares, row => row.OwnerId, at: row => new UserId(row.Id.Value))))
            .Should().Throw<ArgumentException>().WithParameterName("at").WithMessage("*is not a property of the resource*");
        FluentActions.Invoking(() => ModelOf(document => document.HasMembers(row => row.Shares, row => row.OwnerId, at: (System.Linq.Expressions.Expression<Func<Document, UserId>>)null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("at");

        // Said with the members, and only there: a mark that names a property the model does not have is refused where it is read.
        var marked = ModelOf(document =>
        {
            document.HasMembers(row => row.Shares, row => row.OwnerId);
            document.HasAnnotation(MembershipModel.AtAnnotation, "Shelf");
        });
        FluentActions.Invoking(() => MembershipModel.MembersOf(marked.FindEntityType(typeof(Document))!))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Document was mapped with HasMembers as sitting at Shelf, and the model no longer has that property.*");
    }

    /// <summary>The model of a context that maps the documents as <paramref name="map"/> says.</summary>
    private static IModel ModelOf(Action<Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Document>> map)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        using var context = new MappedContext(new DbContextOptionsBuilder<MappedContext>().UseSqlite(connection).Options, map);
        return context.Model;
    }

    /// <summary>A context whose mapping of the documents is the test's to say. Its model is built for each test, never cached.</summary>
    private sealed class MappedContext(DbContextOptions<MappedContext> options, Action<Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Document>> map) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Document>(map);

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, EveryTime>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }

        private sealed class EveryTime : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => new object();
        }
    }

    /// <summary>The model of a context that maps the binders as <paramref name="map"/> says.</summary>
    private static IModel ShelvesOf(Action<Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Binder>> map)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        using var context = new ShelvesContext(new DbContextOptionsBuilder<ShelvesContext>().UseSqlite(connection).Options, map);
        return context.Model;
    }

    /// <summary>A context of binders, a resource with a key part, whose mapping is the test's to say. Its model is built for each test, never cached.</summary>
    private sealed class ShelvesContext(DbContextOptions<ShelvesContext> options, Action<Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Binder>> map) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Binder>(binder =>
        {
            binder.Property(row => row.Id).ValueGeneratedNever();
            map(binder);
        });

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, EveryTime>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }

        private sealed class EveryTime : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => new object();
        }
    }

    /// <summary>Something with a collection that is no member list.</summary>
    private sealed class Shelf
    {
        public Guid Id { get; set; }

        public UserId Owner { get; set; }

        public List<Book> Books { get; } = [];
    }

    private sealed class Book
    {
        public Guid Id { get; set; }
    }
}
