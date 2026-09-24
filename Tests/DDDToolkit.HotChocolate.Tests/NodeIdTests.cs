using System.Text;
using System.Text.Json;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.ExampleLibrary.GraphQl;
using DDDToolkit.HotChocolate.Tests.Domain;
using DDDToolkit.HotChocolate.Tests.GraphQl;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using HotChocolate.Types.Relay;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// Relay global object identification with the toolkit's identifiers as node ids. HotChocolate does the
/// identification; the generated bindings teach it to write an identifier into a node id and read one
/// back, which it cannot do by itself for a type it does not know.
/// </summary>
public class NodeIdTests
{
    [Fact]
    public async Task A_struct_id_over_a_guid_is_a_node_id_and_node_finds_it_again()
    {
        var id = await NodeIdOfAsync("{ pet { id } }", "pet");

        var node = await DataAsync($$"""{ node(id: "{{id}}") { id ... on Pet { name } } }""");

        node.GetProperty("node").GetProperty("name").GetString().Should().Be("Tibbles");
    }

    [Fact]
    public async Task A_class_id_over_a_guid_is_a_node_id_too()
    {
        var id = await NodeIdOfAsync("{ member { id } }", "member");

        var node = await DataAsync($$"""{ node(id: "{{id}}") { ... on Member { name } } }""");

        node.GetProperty("node").GetProperty("name").GetString().Should().Be("Ada");
    }

    [Theory]
    [InlineData("{ seat { id } }", "seat", "Seat:12")]
    [InlineData("{ account { id } }", "account", "Account:ada@example.com")]
    public async Task Int_and_string_ids_are_written_the_way_HotChocolate_writes_them(string query, string field, string decoded)
    {
        var id = await NodeIdOfAsync(query, field);

        Encoding.UTF8.GetString(Convert.FromBase64String(id)).Should().Be(decoded);
        (await DataAsync($$"""{ node(id: "{{id}}") { id } }""")).GetProperty("node").GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task A_guid_id_is_the_same_node_id_HotChocolate_writes_for_the_raw_guid()
    {
        // The format is HotChocolate's own, so any server or Fusion gateway reading node ids reads these.
        // The same Pet, in a schema where its id is the bare Guid HotChocolate serializes by itself.
        var native = await new ServiceCollection()
            .AddGraphQL()
            .AddGlobalObjectIdentification()
            .AddType<RawPetType>()
            .AddQueryType<RawQuery>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var result = await native.ExecuteAsync("{ pet { id } }", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(result.ToJson());

        var id = await NodeIdOfAsync("{ pet { id } }", "pet");

        id.Should().Be(document.RootElement.GetProperty("data").GetProperty("pet").GetProperty("id").GetString());
    }

    [Fact]
    public async Task An_ID_argument_arrives_as_the_identifier_it_names()
    {
        var id = await NodeIdOfAsync("{ pet { id } }", "pet");

        var data = await DataAsync($$"""{ describePet(id: "{{id}}") }""");

        data.GetProperty("describePet").GetString().Should().Be(TestData.Cat.ToString());
    }

    [Fact]
    public async Task A_node_id_of_another_type_is_refused_rather_than_read_as_empty()
    {
        var seat = await NodeIdOfAsync("{ seat { id } }", "seat");

        var raw = await RawAsync($$"""{ describePet(id: "{{seat}}") }""");

        raw.TryGetProperty("errors", out _).Should().BeTrue();
    }

    // ------------------------------------------------------------------ the schema

    private static ValueTask<IRequestExecutor> ExecutorAsync()
        => new ServiceCollection()
            .AddGraphQL()
            .AddDDDToolkitTypes()
            .AddGlobalObjectIdentification()
            .AddCommonGraphQlRuntimeBindings()
            .AddGraphQlTestsGraphQlRuntimeBindings()
            .AddType<PetType>()
            .AddType<MemberType>()
            .AddType<SeatType>()
            .AddType<AccountType>()
            .AddQueryType<NodeQuery>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

    private static async Task<string> NodeIdOfAsync(string query, string field)
        => (await DataAsync(query)).GetProperty(field).GetProperty("id").GetString()!;

    private static async Task<JsonElement> DataAsync(string query)
    {
        var raw = await RawAsync(query);
        if (raw.TryGetProperty("errors", out var errors))
        {
            Assert.Fail("The GraphQL request failed: " + errors);
        }

        return raw.GetProperty("data");
    }

    private static async Task<JsonElement> RawAsync(string query)
    {
        var executor = await ExecutorAsync();
        var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(result.ToJson());
        return document.RootElement.Clone();
    }

    public sealed record Pet(CatId Id, string Name);

    public sealed record Member(PersonId Id, string Name);

    public sealed record Seat(SeatNumber Id);

    public sealed record Account(LoginId Id);

    public sealed class NodeQuery
    {
        public static readonly Pet Tibbles = new(TestData.Cat, "Tibbles");
        public static readonly Member Ada = new(TestData.Person, "Ada");
        public static readonly Seat Twelve = new(SeatNumber.Create(12));
        public static readonly Account Login = new(LoginId.Create("ada@example.com"));

        public Pet GetPet() => Tibbles;

        public Member GetMember() => Ada;

        public Seat GetSeat() => Twelve;

        public Account GetAccount() => Login;

        public string DescribePet([ID<Pet>] CatId id) => id.ToString();
    }

    public sealed record RawPet(Guid Id);

    public sealed class RawQuery
    {
        public RawPet GetPet() => new(TestData.CatGuid);
    }

    public sealed class RawPetType : ObjectType<RawPet>
    {
        protected override void Configure(IObjectTypeDescriptor<RawPet> descriptor)
            => descriptor.Name("Pet").ImplementsNode().IdField(pet => pet.Id)
                .ResolveNode((_, id) => Task.FromResult<RawPet?>(new RawPet(id)));
    }

    public sealed class PetType : ObjectType<Pet>
    {
        protected override void Configure(IObjectTypeDescriptor<Pet> descriptor)
            => descriptor.ImplementsNode().IdField(pet => pet.Id)
                .ResolveNode((_, id) => Task.FromResult<Pet?>(id == NodeQuery.Tibbles.Id ? NodeQuery.Tibbles : null));
    }

    public sealed class MemberType : ObjectType<Member>
    {
        protected override void Configure(IObjectTypeDescriptor<Member> descriptor)
            => descriptor.ImplementsNode().IdField(member => member.Id)
                .ResolveNode((_, id) => Task.FromResult<Member?>(id == NodeQuery.Ada.Id ? NodeQuery.Ada : null));
    }

    public sealed class SeatType : ObjectType<Seat>
    {
        protected override void Configure(IObjectTypeDescriptor<Seat> descriptor)
            => descriptor.ImplementsNode().IdField(seat => seat.Id)
                .ResolveNode((_, id) => Task.FromResult<Seat?>(id == NodeQuery.Twelve.Id ? NodeQuery.Twelve : null));
    }

    public sealed class AccountType : ObjectType<Account>
    {
        protected override void Configure(IObjectTypeDescriptor<Account> descriptor)
            => descriptor.ImplementsNode().IdField(account => account.Id)
                .ResolveNode((_, id) => Task.FromResult<Account?>(id == NodeQuery.Login.Id ? NodeQuery.Login : null));
    }
}
