using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DDDToolkit.Examples.AppHost.Tests;

/// <summary>
/// What a customer of the shop sees, whichever way the shop is run. Every sample runs these same
/// scenarios, through a subclass that names its AppHost; the point of the samples is that the answers
/// do not change with the database, the topology or the transport.
/// </summary>
/// <remarks>
/// The answers arrive a second or two after the request: each module hears about the order from an
/// outbox poller. So a scenario places an order and then polls until the order has settled, the way a
/// client of an eventually consistent system has to.
/// </remarks>
public abstract class ShopScenarios<TAppHost>(ShopFixture<TAppHost> shop) where TAppHost : class
{
    private static readonly TimeSpan Settles = TimeSpan.FromSeconds(60);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_order_that_can_be_filled_and_paid_for_is_confirmed_and_shipped()
    {
        shop.SkipUnlessStarted();

        var id = await PlaceAsync(("COFFEE-1KG", 1));

        var order = await SettledAsync(id);

        order.GetProperty("status").GetString().Should().Be("Confirmed");
        order.GetProperty("stockReserved").GetBoolean().Should().BeTrue();
        order.GetProperty("paid").GetBoolean().Should().BeTrue();
        await EventuallyAsync(async () => (await GetAsync("/shipments")).EnumerateArray()
            .Any(shipment => shipment.GetProperty("order").GetString() == id));
    }

    [Fact]
    public async Task A_payment_the_provider_refuses_cancels_the_order_and_puts_the_stock_back()
    {
        shop.SkipUnlessStarted();

        var id = await PlaceAsync(("GRINDER", 1));

        var order = await SettledAsync(id);

        order.GetProperty("status").GetString().Should().Be("Cancelled");
        order.GetProperty("cancellationReason").GetString().Should().Contain("limit");

        // The compensation: Inventory reserved the grinder, heard the order was cancelled, and let it go.
        await EventuallyAsync(async () => (await ReservationAsync(id)) == "Released");
    }

    [Fact]
    public async Task An_order_there_is_not_enough_stock_for_is_cancelled_and_its_payment_voided()
    {
        shop.SkipUnlessStarted();

        var id = await PlaceAsync(("MUG", 50));

        var order = await SettledAsync(id);

        order.GetProperty("status").GetString().Should().Be("Cancelled");
        order.GetProperty("cancellationReason").GetString().Should().Contain("Not enough stock");
        await EventuallyAsync(async () => (await PaymentAsync(id)) == "Voided");
    }

    [Fact]
    public async Task A_confirmed_order_cannot_be_cancelled_and_the_rule_says_so_by_its_code()
    {
        shop.SkipUnlessStarted();

        var id = await PlaceAsync(("COFFEE-1KG", 1));
        (await SettledAsync(id)).GetProperty("status").GetString().Should().Be("Confirmed");

        using var response = await shop.Client().PostAsJsonAsync($"/orders/{id}/cancel", new { reason = "Changed my mind." }, Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Cancellation)).Should().Contain("ORDER_ALREADY_CONFIRMED");
    }

    [Fact]
    public async Task A_line_with_no_SKU_is_refused_by_the_rule_of_the_line()
    {
        shop.SkipUnlessStarted();

        using var response = await PostOrderAsync(("   ", 1));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Cancellation)).Should().Contain("LINE_HAS_NO_SKU");
    }

    // ------------------------------------------------------------------ GraphQL

    [Fact]
    public async Task One_GraphQL_query_reads_the_order_across_every_module_that_had_a_hand_in_it()
    {
        shop.SkipUnlessStarted();

        var placed = await GraphQLAsync(
            """
            mutation { placeOrder(input: { street: "Oudegracht 1", city: "Utrecht", postalCode: "3511 AA",
                                           lines: [{ sku: "COFFEE-1KG", quantity: 1 }] }) { id } }
            """,
            retryUntilPriced: true);
        var id = placed.GetProperty("placeOrder").GetProperty("id").GetString()!;

        JsonElement order = default;
        await EventuallyAsync(async () =>
        {
            order = (await GraphQLAsync(
                $$"""
                { order(id: "{{id}}") {
                    status
                    lines { quantity product { name price { amount currency } } }
                    payment { status order }
                    shipment { destination order } } }
                """)).GetProperty("order");
            return order.GetProperty("shipment").ValueKind == JsonValueKind.Object;
        });

        // Ordering answered the order, Catalog the product, Payments the payment, Shipping the van.
        order.GetProperty("status").GetString().Should().Be("CONFIRMED");
        order.GetProperty("lines")[0].GetProperty("product").GetProperty("name").GetString().Should().Be("Coffee beans, 1 kg");
        order.GetProperty("payment").GetProperty("status").GetString().Should().Be("CAPTURED");

        // The modules point back at the order with its own node id, and node(id:) finds it again.
        order.GetProperty("payment").GetProperty("order").GetString().Should().Be(id);
        order.GetProperty("shipment").GetProperty("order").GetString().Should().Be(id);
        (await GraphQLAsync($$"""{ node(id: "{{id}}") { ... on Order { status } } }"""))
            .GetProperty("node").GetProperty("status").GetString().Should().Be("CONFIRMED");
    }

    [Fact]
    public async Task A_broken_rule_is_a_GraphQL_error_carrying_the_rules_code()
    {
        shop.SkipUnlessStarted();

        var placed = await GraphQLAsync(
            """
            mutation { placeOrder(input: { street: "Oudegracht 1", city: "Utrecht", postalCode: "3511 AA",
                                           lines: [{ sku: "COFFEE-1KG", quantity: 1 }] }) { id } }
            """,
            retryUntilPriced: true);
        var id = placed.GetProperty("placeOrder").GetProperty("id").GetString()!;
        await EventuallyAsync(async () =>
            (await GraphQLAsync($$"""{ order(id: "{{id}}") { status } }""")).GetProperty("order").GetProperty("status").GetString() == "CONFIRMED");

        var errors = await GraphQLErrorsAsync($$"""mutation { cancelOrder(id: "{{id}}") { status } }""");

        errors.EnumerateArray().Select(error => error.GetProperty("extensions").GetProperty("code").GetString())
            .Should().Contain("ORDER_ALREADY_CONFIRMED");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Sends a GraphQL request and returns its <c>data</c>. Right after the shop starts Ordering may not
    /// have heard Catalog's prices yet and refuses with <c>UNKNOWN_SKU</c>; with
    /// <paramref name="retryUntilPriced"/> that is asked again until the copy has caught up.
    /// </summary>
    private async Task<JsonElement> GraphQLAsync(string query, bool retryUntilPriced = false)
    {
        var deadline = DateTime.UtcNow + Settles;

        while (true)
        {
            var response = await PostGraphQLAsync(query);
            if (!response.TryGetProperty("errors", out var errors))
            {
                return response.GetProperty("data");
            }

            if (!retryUntilPriced || !errors.ToString().Contains("UNKNOWN_SKU") || DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException("The GraphQL request failed: " + errors);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);
        }
    }

    private async Task<JsonElement> GraphQLErrorsAsync(string query)
        => (await PostGraphQLAsync(query)).GetProperty("errors");

    private async Task<JsonElement> PostGraphQLAsync(string query)
    {
        using var response = await shop.Client().PostAsJsonAsync("/graphql", new { query }, Cancellation);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
    }

    /// <summary>
    /// Places an order and returns its id. Right after the shop starts, Ordering may not have heard
    /// Catalog's prices yet and answers 400 for an unknown SKU; that is the copy catching up, so this
    /// asks again until the copy has the price.
    /// </summary>
    private async Task<string> PlaceAsync(params (string Sku, int Quantity)[] lines)
    {
        var deadline = DateTime.UtcNow + Settles;

        while (true)
        {
            using var response = await PostOrderAsync(lines);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var order = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
                return order.GetProperty("id").GetString()!;
            }

            var body = await response.Content.ReadAsStringAsync(Cancellation);
            if (response.StatusCode != HttpStatusCode.BadRequest || !body.Contains("not for sale") || DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException($"Placing the order answered {(int)response.StatusCode}: {body}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);
        }
    }

    private Task<HttpResponseMessage> PostOrderAsync(params (string Sku, int Quantity)[] lines)
        => shop.Client().PostAsJsonAsync(
            "/orders",
            new
            {
                street = "Oudegracht 1",
                city = "Utrecht",
                postalCode = "3511 AA",
                lines = lines.Select(line => new { sku = line.Sku, quantity = line.Quantity }),
            },
            Cancellation);

    /// <summary>The order once it is no longer Placed: confirmed or cancelled.</summary>
    private async Task<JsonElement> SettledAsync(string id)
    {
        JsonElement order = default;
        await EventuallyAsync(async () =>
        {
            order = await GetAsync($"/orders/{id}");
            return order.GetProperty("status").GetString() != "Placed";
        });

        return order;
    }

    private async Task<string?> ReservationAsync(string orderId)
        => (await GetAsync("/reservations")).EnumerateArray()
            .Where(reservation => reservation.GetProperty("order").GetString() == orderId)
            .Select(reservation => reservation.GetProperty("status").GetString())
            .FirstOrDefault();

    private async Task<string?> PaymentAsync(string orderId)
        => (await GetAsync("/payments")).EnumerateArray()
            .Where(payment => payment.GetProperty("order").GetString() == orderId)
            .Select(payment => payment.GetProperty("status").GetString())
            .FirstOrDefault();

    private async Task<JsonElement> GetAsync(string path)
        => await shop.Client().GetFromJsonAsync<JsonElement>(path, Cancellation);

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Settles;

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Nothing settled within {Settles.TotalSeconds} seconds.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);
        }
    }
}
