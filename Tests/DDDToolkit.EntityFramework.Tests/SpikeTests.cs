using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Tests;

public class SpikeTests
{
    [Fact]
    public void Model_builds_and_round_trips()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<LibraryContext>().UseSqlite(connection).Options;

        var id = ShelfId.CreateUnique();
        using (var context = new LibraryContext(options))
        {
            var dir = Environment.GetEnvironmentVariable("SPIKE_OUT") ?? Path.GetTempPath();
            IModel model;
            try
            {
                model = context.Model;
            }
            catch (Exception ex)
            {
                var designModel = context.GetService<IDesignTimeModel>().Model;
                var cfg = designModel.FindTypeMappingConfiguration(typeof(TagId));
                var tags = designModel.FindEntityType(typeof(Book))?.FindProperty("Tags");
                var element = tags?.GetElementType();
                File.WriteAllText(Path.Combine(dir, "debug.txt"),
                    $"cfg null: {cfg is null}\n" +
                    $"cfg annotations: {(cfg is null ? "" : string.Join(", ", cfg.GetAnnotations().Select(a => a.Name + "=" + a.Value)))}\n" +
                    $"cfg converter: {cfg?.GetValueConverter()}\n" +
                    $"all cfgs: {string.Join(", ", designModel.GetTypeMappingConfigurations().Select(c => c.ClrType.Name))}\n" +
                    $"tags null: {tags is null}; element null: {element is null}\n" +
                    $"element annotations: {(element is null ? "" : string.Join(", ", element.GetAnnotations().Select(a => a.Name + "=" + a.Value)))}\n" +
                    $"element converter: {element?.GetValueConverter()}\n" +
                    ex.ToString());
                throw;
            }

            context.Database.EnsureCreated();
            File.WriteAllText(Path.Combine(dir, "model.txt"), model.ToDebugString(MetadataDebugStringOptions.LongDefault));
            File.WriteAllText(Path.Combine(dir, "schema.sql"), context.Database.GenerateCreateScript());
            var shelf = new Shelf(id, "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null);
            shelf.AddBook("Dune", new TagId(1), new TagId(2));
            shelf.AddBook("Emma");
            shelf.AddNote("a");
            shelf.AddNote("b");
            context.Shelves.Add(shelf);
            context.SaveChanges();
        }

        using (var context = new LibraryContext(options))
        {
            var shelf = context.Shelves.Single(s => s.Id == id);
            shelf.Books.Should().HaveCount(2);
            shelf.Books.Single(b => b.Title == "Dune").Tags.Should().Equal(new TagId(1), new TagId(2));
            shelf.Notes.Select(n => n.Text).Should().BeEquivalentTo(["a", "b"]);
        }
    }
}
