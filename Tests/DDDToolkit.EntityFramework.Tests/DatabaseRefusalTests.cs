using System.Data.Common;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Tests.Converters;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// A unique index that says what it refuses with (<c>RefusesAs</c>), on SQLite: a save that breaks it is that
/// refusal, filled from the row that broke it, and every other failure of a save is what it was.
/// </summary>
public sealed class DatabaseRefusalTests : IDisposable
{
    private static readonly DepotId North = new(1);

    private static readonly DepotId South = new(2);

    private readonly SqliteDatabase _db = new();

    public DatabaseRefusalTests() => _db.EnsureCreated(Context);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    /// <summary>A context that saves the way <c>UseDDDToolkit</c> makes one save.</summary>
    private PalletContext Context()
        => new(_db.Options<PalletContext>(options => options.AddInterceptors(new AggregateVersionInterceptor(), new DatabaseRefusalInterceptor())));

    private async Task<Pallet> SeedAsync(DepotId depot, int number, string label)
    {
        var pallet = new Pallet(PalletId.CreateSequential(), depot, number, label, owner: null);
        await using var context = Context();
        context.Pallets.Add(pallet);
        await context.SaveChangesAsync(Cancellation);
        return pallet;
    }

    [Fact]
    public async Task A_marked_unique_index_answers_its_refusal_on_sqlite()
    {
        await SeedAsync(North, 2, "The first");

        await using var context = Context();
        context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 2, "The second", owner: null));

        var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(PalletContext.NumberTaken);
        refusal.Kind.Should().Be(RefusalKind.Conflict, "a conflict, unless the index says otherwise");
        refusal.Message.Should().Be("Depot 1 already has a pallet numbered 2, so 'The second' cannot take it.");
        refusal.InnerException.Should().BeOfType<DbUpdateException>("the refusal keeps the failure it stands for")
            .Which.InnerException.Should().BeOfType<SqliteException>();

        // The save without await answers the same.
        FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<RefusalException>().Which.Code.Should().Be(PalletContext.NumberTaken);

        _db.CountRows("Pallets").Should().Be(1, "nothing was written");
    }

    [Fact]
    public async Task UseDDDToolkit_adds_the_interceptor_last_and_its_context_answers_the_refusal()
    {
        await SeedAsync(North, 2, "The first");

        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework();
        services.AddDbContext<PalletContext>((provider, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(provider));
        await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = host.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PalletContext>();

        // The order they run in: the one that answers the database comes after the one that tells a lost race
        // from a denial, which throws first when a save found no row.
        context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!.Select(interceptor => interceptor.GetType()).Should().Equal(
            typeof(PublishDomainEventsInterceptor),
            typeof(InvariantInterceptor),
            typeof(AggregateVersionInterceptor),
            typeof(DatabaseRefusalInterceptor));

        context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 2, "The second", owner: null));
        (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>())
            .Which.Code.Should().Be(PalletContext.NumberTaken);
    }

    [Fact]
    public async Task The_refusal_names_the_conflicting_values_by_property()
    {
        await SeedAsync(North, 2, "The first");
        var other = await SeedAsync(South, 9, "Elsewhere");

        // A new row: the depot is an id with a converter, and shows as the number underneath; the label is a
        // property the index is not on.
        await using (var context = Context())
        {
            context.Pallets.AddRange(
                new Pallet(PalletId.CreateSequential(), South, 7, "A free number", owner: null),
                new Pallet(PalletId.CreateSequential(), North, 2, "The second", owner: null));

            var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
            refusal.Arguments.Should().BeEquivalentTo(
                new Dictionary<string, object?> { ["Depot"] = 1, ["Number"] = 2, ["Label"] = "The second" },
                "SQLite saves row by row, so the row that broke the index is the one the failure names");
            refusal.Arguments["depot"].Should().Be(1, "arguments are matched without regard to case, like every failure's");
            refusal.Message.Should().Be("Depot 1 already has a pallet numbered 2, so 'The second' cannot take it.");
        }

        // A row that changes into a number that is taken.
        await using (var context = Context())
        {
            var moving = await context.Pallets.SingleAsync(pallet => pallet.Id == other.Id, Cancellation);
            moving.Renumber(2);
            context.Entry(moving).Property(pallet => pallet.Depot).CurrentValue = North;

            var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
            refusal.Arguments.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Depot"] = 1, ["Number"] = 2, ["Label"] = "Elsewhere" });
        }
    }

    [Fact]
    public async Task An_index_says_which_kind_of_refusal_it_is_and_may_name_nothing()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(Cancellation);
        var options = new DbContextOptionsBuilder<LabelledContext>().UseSqlite(connection).AddInterceptors(new DatabaseRefusalInterceptor()).Options;

        await using var context = new LabelledContext(options);
        await context.Database.EnsureCreatedAsync(Cancellation);
        context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 1, "Fragile", owner: null));
        await context.SaveChangesAsync(Cancellation);

        context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 2, "Fragile", owner: null));
        var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;

        refusal.Code.Should().Be("pallets.label-taken");
        refusal.Kind.Should().Be(RefusalKind.Invalid);
        refusal.Message.Should().Be("That label is in use {here}.", "doubled braces are braces");
        refusal.Arguments.Should().BeEmpty("the message names no property");
    }

    [Fact]
    public async Task An_unmarked_unique_index_fails_as_before()
    {
        await SeedAsync(North, 2, "The first");

        // The label is unique too, and its index says nothing.
        await using (var context = Context())
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 3, "The first", owner: null));

            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
            failure.InnerException.Should().BeOfType<SqliteException>().Which.Message.Should().Contain("Pallets.Label");
        }

        // The same tables through a context whose number index says nothing either.
        await using (var context = new UnmarkedPalletContext(_db.Options<UnmarkedPalletContext>(options => options.AddInterceptors(new AggregateVersionInterceptor(), new DatabaseRefusalInterceptor()))))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 2, "The second", owner: null));

            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
            failure.InnerException.Should().BeOfType<SqliteException>().Which.Message.Should().Contain("Pallets.Depot, Pallets.Number");
        }

        // And a marked index without the interceptor.
        await using (var context = new PalletContext(_db.Options<PalletContext>()))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 2, "The second", owner: null));

            await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>();
        }

        // A key that is taken is not an index of the model's.
        await using (var context = Context())
        {
            var first = await context.Pallets.AsNoTracking().SingleAsync(Cancellation);
            context.Pallets.Add(new Pallet(first.Id, South, 5, "The same id", owner: null));

            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
            failure.InnerException.Should().BeOfType<SqliteException>();
            DatabaseRefusal.From(failure).Should().BeNull("only a unique index is read, not the key");
        }
    }

    [Fact]
    public async Task Two_unique_indexes_over_the_same_columns_refuse_only_when_they_refuse_alike()
    {
        await SeedAsync(North, 2, "The first");

        // SQLite names the columns and not the index. With a second unique index on the label, one that says what
        // it refuses with beside one that says nothing, there is no telling which was broken: it fails as before.
        await using (var context = new SecondLabelIndexContext(_db.Options<SecondLabelIndexContext>(options => options.AddInterceptors(new DatabaseRefusalInterceptor()))))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 3, "The first", owner: null));

            await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>();
        }

        // When both say the same, either one's answer is the answer.
        await using (var context = new TwoAlikeLabelIndexesContext(_db.Options<TwoAlikeLabelIndexesContext>(options => options.AddInterceptors(new DatabaseRefusalInterceptor()))))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 3, "The first", owner: null));

            var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
            refusal.Code.Should().Be("pallets.label-taken");
            refusal.Message.Should().Be("The label 'The first' is in use.");
        }
    }

    [Fact]
    public async Task A_lost_race_stays_a_conflict_on_a_database_without_policies()
    {
        var pallet = await SeedAsync(North, 2, "The first");

        var commands = new Commands();
        await using var first = Context();
        await using var second = new PalletContext(_db.Options<PalletContext>(options => options.AddInterceptors(new AggregateVersionInterceptor(), new DatabaseRefusalInterceptor(), commands)));
        var early = await first.Pallets.SingleAsync(row => row.Id == pallet.Id, Cancellation);
        var late = await second.Pallets.SingleAsync(row => row.Id == pallet.Id, Cancellation);

        early.Retitle("Changed first");
        await first.SaveChangesAsync(Cancellation);

        commands.Clear();
        late.Retitle("Too late");
        (await FluentActions.Awaiting(() => second.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>())
            .Which.AggregateId.Should().Be(pallet.Id);

        // Telling a lost race from a denial costs the failed save one read of the row's version, by its key.
        commands.Sent.Should().HaveCount(2);
        commands.Sent[0].Sql.Should().StartWith("UPDATE \"Pallets\"");
        commands.Sent[1].Sql.Should().StartWith("SELECT \"p\".\"Version\"").And.Contain("WHERE \"p\".\"Id\" = @");
        commands.Sent[1].Parameters.Should().Be(1, "the key is a parameter, so the statement is the same for every row");

        FluentActions.Invoking(() => second.SaveChanges()).Should().Throw<ConcurrencyConflictException>();

        // Removed meanwhile: not there to be read again, and a conflict as well.
        first.Pallets.Remove(early);
        await first.SaveChangesAsync(Cancellation);
        await FluentActions.Awaiting(() => second.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task A_row_the_database_left_as_it_was_is_refused_on_a_database_without_policies_too()
    {
        var pallet = await SeedAsync(North, 2, "The first");

        // What a policy does on Postgres, a trigger does here: the statement changes no row and says nothing.
        // Nobody else wrote, so no retry will ever get through, and the save is refused rather than a conflict.
        Run("CREATE TRIGGER \"Pallets_stay_as_they_are\" BEFORE UPDATE ON \"Pallets\" BEGIN SELECT RAISE(IGNORE); END");

        await using var context = Context();
        var kept = await context.Pallets.SingleAsync(row => row.Id == pallet.Id, Cancellation);
        kept.Retitle("Changed");

        var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(ToolkitRefusals.Refused);
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        refusal.InnerException.Should().BeOfType<ConcurrencyConflictException>("the refusal keeps what the save looked like")
            .Which.AggregateId.Should().Be(pallet.Id);

        FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<RefusalException>().Which.Code.Should().Be(ToolkitRefusals.Refused);
    }

    [Fact]
    public async Task A_read_that_fails_leaves_the_conflict()
    {
        var pallet = await SeedAsync(North, 2, "The first");
        Run("CREATE TRIGGER \"Pallets_stay_as_they_are\" BEFORE UPDATE ON \"Pallets\" BEGIN SELECT RAISE(IGNORE); END");

        // The save finds no row, and the read that would tell a denial from a lost race fails as well. Nothing is
        // known beyond the conflict, so the conflict it stays: neither a refusal nobody can vouch for, nor the
        // failure of a read the caller never asked for.
        var reads = new FailingReads();
        await using var context = new PalletContext(_db.Options<PalletContext>(options => options.AddInterceptors(new AggregateVersionInterceptor(), new DatabaseRefusalInterceptor(), reads)));
        var kept = await context.Pallets.SingleAsync(row => row.Id == pallet.Id, Cancellation);
        kept.Retitle("Changed");

        reads.Fail = true;
        (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>())
            .Which.AggregateId.Should().Be(pallet.Id);
        FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<ConcurrencyConflictException>();
        reads.Failed.Should().Be(2, "each save tried the read once");

        // The same save with a read that works is the refusal.
        reads.Fail = false;
        await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>();
    }

    [Fact]
    public async Task A_row_keyed_on_several_parts_is_read_again_by_all_of_them()
    {
        using var db = new SqliteDatabase();
        var commands = new Commands();
        KeyPartContext Surveys() => new(db.Options<KeyPartContext>(options => options.AddInterceptors(new AggregateVersionInterceptor(), new DatabaseRefusalInterceptor(), commands)));
        db.EnsureCreated(Surveys);

        var survey = new Survey(RegionId.CreateUnique(), SurveyId.CreateUnique(), "Before");
        await using (var seeding = Surveys())
        {
            seeding.Surveys.Add(survey);
            await seeding.SaveChangesAsync(Cancellation);
        }

        // A lost race on an aggregate keyed (RegionId, Id): the one read names both parts, each as a parameter.
        await using (var first = Surveys())
        await using (var second = Surveys())
        {
            var early = await first.Surveys.SingleAsync(row => row.Id == survey.Id, Cancellation);
            var late = await second.Surveys.SingleAsync(row => row.Id == survey.Id, Cancellation);
            early.Retitle("Changed first");
            await first.SaveChangesAsync(Cancellation);

            commands.Clear();
            late.Retitle("Too late");
            await FluentActions.Awaiting(() => second.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>();

            var read = commands.Sent.Should().ContainSingle(command => command.Sql.StartsWith("SELECT")).Which;
            read.Sql.Should().Contain("\"RegionId\" = @").And.Contain("\"Id\" = @");
            read.Parameters.Should().Be(2);
        }

        // And a row the database left as it was is found by that read, unchanged: a refusal.
        await using var command = db.Connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER \"Surveys_stay_as_they_are\" BEFORE UPDATE ON \"Surveys\" BEGIN SELECT RAISE(IGNORE); END";
        await command.ExecuteNonQueryAsync(Cancellation);

        await using var context = Surveys();
        var kept = await context.Surveys.SingleAsync(row => row.Id == survey.Id, Cancellation);
        kept.Retitle("Changed");
        (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>())
            .Which.Code.Should().Be(ToolkitRefusals.Refused);
    }

    [Fact]
    public void Marking_an_index_needs_no_migration()
    {
        using var unmarked = new UnmarkedPalletContext(_db.Options<UnmarkedPalletContext>());
        using var marked = Context();

        var index = marked.Model.FindEntityType(typeof(Pallet))!.GetIndexes().Single(candidate => candidate.Properties.Count == 2);
        index.FindAnnotation("DDDToolkit:RefusesAs:Code")!.Value.Should().Be(PalletContext.NumberTaken);
        index.FindAnnotation("DDDToolkit:RefusesAs:Message")!.Value.Should().Be(PalletContext.NumberTakenText);
        index.FindAnnotation("DDDToolkit:RefusesAs:Kind")!.Value.Should().Be("Conflict");

        // What a migration is made of: the difference between the tables as they were and as the model has them.
        var before = unmarked.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var after = marked.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var differ = marked.GetService<IMigrationsModelDiffer>();

        differ.GetDifferences(before, after).Should().BeEmpty("what an index refuses with is the application's to know, not the database's");
        differ.HasDifferences(before, after).Should().BeFalse();
        marked.Database.GenerateCreateScript().Should().Be(unmarked.Database.GenerateCreateScript());
    }

    [Fact]
    public void A_message_that_names_no_property_of_the_row_fails_when_the_model_is_built()
    {
        using var context = new MisnamedContext(_db.Options<MisnamedContext>());

        FluentActions.Invoking(() => context.Model).Should().Throw<InvalidOperationException>()
            .WithMessage("The index on (Depot, Number) of 'Pallet' is refused as 'pallets.number-taken', and its message names {Numbr}, which is not a property 'Pallet' maps.*");
    }

    [Fact]
    public void An_index_that_is_not_unique_refuses_nothing_and_fails_when_the_model_is_built()
    {
        using var context = new NotUniqueContext(_db.Options<NotUniqueContext>());

        FluentActions.Invoking(() => context.Model).Should().Throw<InvalidOperationException>()
            .WithMessage("The index on (Depot, Number) of 'Pallet' is refused as 'pallets.number-taken', but it is not unique*");
    }

    [Fact]
    public void A_refusal_needs_a_code_and_one_of_the_four_kinds()
    {
        var builder = new ModelBuilder();
        var index = builder.Entity<Pallet>().HasIndex(pallet => pallet.Number).IsUnique();

        FluentActions.Invoking(() => index.RefusesAs(" ", "A text.")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => index.RefusesAs("pallets.number-taken", null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => index.RefusesAs("pallets.number-taken", "A text.", (RefusalKind)42)).Should().Throw<ArgumentOutOfRangeException>();
        index.RefusesAs("pallets.number-taken", "A text.").Should().BeSameAs(index, "it chains like the rest of the builder");
    }

    [Fact]
    public async Task What_sqlite_refused_is_read_from_the_failure()
    {
        await SeedAsync(North, 2, "The first");

        await using (var context = new PalletContext(_db.Options<PalletContext>()))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 2, "The second", owner: null));
            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;

            var duplicate = DatabaseRefusal.From(failure)!;
            duplicate.Kind.Should().Be(DatabaseRefusalKind.DuplicateKey);
            duplicate.Constraint.Should().BeNull("SQLite names the columns, not the index");
            duplicate.Table.Should().Be("Pallets");
            duplicate.Columns.Should().Equal("Depot", "Number");
            duplicate.Schema.Should().BeNull();
        }

        // An index over an expression has no columns to name, and SQLite names the index.
        await using var command = _db.Connection.CreateCommand();
        command.CommandText = "CREATE UNIQUE INDEX \"IX_Pallets_LowerLabel\" ON \"Pallets\" (lower(\"Label\"))";
        await command.ExecuteNonQueryAsync(Cancellation);
        command.CommandText = "INSERT INTO \"Pallets\" (\"Id\", \"Depot\", \"Number\", \"Label\", \"Version\") VALUES ('" + Guid.NewGuid() + "', 2, 8, 'THE FIRST', 1)";
        var expression = (await FluentActions.Awaiting(() => command.ExecuteNonQueryAsync(Cancellation)).Should().ThrowAsync<SqliteException>()).Which;

        var named = DatabaseRefusal.From(expression)!;
        named.Kind.Should().Be(DatabaseRefusalKind.DuplicateKey);
        named.Constraint.Should().Be("IX_Pallets_LowerLabel");
        named.Table.Should().BeNull();
        named.Columns.Should().BeEmpty();

        // What is no database's failure is nothing to read.
        DatabaseRefusal.From(new InvalidOperationException("The disk is full.")).Should().BeNull();
        FluentActions.Invoking(() => DatabaseRefusal.From(null!)).Should().Throw<ArgumentNullException>();
    }

    /// <summary>Runs <paramref name="sql"/> on the test's database, past every context.</summary>
    private void Run(string sql)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Fails every query a context sends while <see cref="Fail"/> is set, and lets every write through.</summary>
    private sealed class FailingReads : DbCommandInterceptor
    {
        public bool Fail { get; set; }

        public int Failed { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Refuse(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Refuse(command);
            return ValueTask.FromResult(result);
        }

        private void Refuse(DbCommand command)
        {
            if (Fail && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
            {
                Failed++;
                throw new InvalidOperationException("The connection was lost.");
            }
        }
    }

    /// <summary>Keeps the commands a context sends.</summary>
    private sealed class Commands : DbCommandInterceptor
    {
        private readonly List<(string Sql, int Parameters)> _sent = [];

        public IReadOnlyList<(string Sql, int Parameters)> Sent => _sent;

        public void Clear() => _sent.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _sent.Add((command.CommandText, command.Parameters.Count));
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _sent.Add((command.CommandText, command.Parameters.Count));
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>The depot with a label index that refuses as invalid input, in a text that names nothing and has braces of its own.</summary>
    private sealed class LabelledContext(DbContextOptions<LabelledContext> options) : DbContext(options)
    {
        public DbSet<Pallet> Pallets => Set<Pallet>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Pallet>().HasIndex(pallet => pallet.Label).IsUnique().RefusesAs("pallets.label-taken", "That label is in use {{here}}.", RefusalKind.Invalid);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddEfTestsConverters();
        }
    }

    /// <summary>The depot with a second unique index on the label, over a part of the pallets, that says what it refuses with.</summary>
    private sealed class SecondLabelIndexContext(DbContextOptions<SecondLabelIndexContext> options) : PalletContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Pallet>().HasIndex(pallet => pallet.Label, "IX_Pallets_Label_OfTheFirstHundred")
                .IsUnique()
                .HasFilter("\"Number\" <= 100")
                .RefusesAs("pallets.label-taken", "The label '{Label}' is in use.");
        }
    }

    /// <summary>The same two indexes, both saying the same.</summary>
    private sealed class TwoAlikeLabelIndexesContext(DbContextOptions<TwoAlikeLabelIndexesContext> options) : PalletContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Pallet>().HasIndex(pallet => pallet.Label).RefusesAs("pallets.label-taken", "The label '{Label}' is in use.");
            modelBuilder.Entity<Pallet>().HasIndex(pallet => pallet.Label, "IX_Pallets_Label_OfTheFirstHundred")
                .IsUnique()
                .HasFilter("\"Number\" <= 100")
                .RefusesAs("pallets.label-taken", "The label '{Label}' is in use.");
        }
    }

    /// <summary>A message with a typo in a property's name.</summary>
    private sealed class MisnamedContext(DbContextOptions<MisnamedContext> options) : PalletContext(options)
    {
        protected override void Numbers(Microsoft.EntityFrameworkCore.Metadata.Builders.IndexBuilder<Pallet> index)
            => index.RefusesAs(NumberTaken, "Number {Numbr} is taken.");
    }

    /// <summary>A refusal on an index no save can break.</summary>
    private sealed class NotUniqueContext(DbContextOptions<NotUniqueContext> options) : PalletContext(options)
    {
        protected override void Numbers(Microsoft.EntityFrameworkCore.Metadata.Builders.IndexBuilder<Pallet> index)
            => index.IsUnique(false).RefusesAs(NumberTaken, NumberTakenText);
    }
}
