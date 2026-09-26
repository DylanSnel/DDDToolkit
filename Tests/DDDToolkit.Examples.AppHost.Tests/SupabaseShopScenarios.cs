using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DDDToolkit.Examples.AppHost.Tests;

/// <summary>
/// What only the Supabase monolith can show: a customer who signs in with Supabase Auth sees their own
/// orders and nobody else's. The modules have no idea; the host runs each request's queries as the caller
/// its token names, and a policy in <c>supabase/migrations</c> decides. Every other scenario keeps running
/// as a guest, as before.
/// </summary>
public abstract class SupabaseShopScenarios<TAppHost>(ShopFixture<TAppHost> shop)
    : ShopScenarios<TAppHost>(shop), IAsyncLifetime where TAppHost : class
{
    private static readonly TimeSpan Settles = TimeSpan.FromSeconds(60);

    private readonly ShopFixture<TAppHost> _shop = shop;
    private readonly SupabaseCustomers _customers = SupabaseCustomers.ForThisRun();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _customers.DisposeAsync();

    [Fact]
    public async Task A_customers_order_is_theirs_alone()
    {
        _shop.SkipUnlessStarted();
        var alice = await _customers.SignInAsync("alice", Cancellation);
        var bob = await _customers.SignInAsync("bob", Cancellation);

        var id = await PlaceAsync(alice);

        using (var asAlice = await SendAsync(HttpMethod.Get, $"/orders/{id}", alice))
        {
            asAlice.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var asBob = await SendAsync(HttpMethod.Get, $"/orders/{id}", bob))
        {
            asBob.StatusCode.Should().Be(HttpStatusCode.NotFound, "Bob's query ran as Bob, and the policy does not show him Alice's order");
        }

        using (var asGuest = await SendAsync(HttpMethod.Get, $"/orders/{id}", token: null))
        {
            asGuest.StatusCode.Should().Be(HttpStatusCode.NotFound, "a request without a token runs as anon, which sees guests' orders only");
        }

        using (var cancelledByBob = await SendAsync(HttpMethod.Post, $"/orders/{id}/cancel", bob, new { reason = "Not mine to cancel." }))
        {
            cancelledByBob.StatusCode.Should().Be(HttpStatusCode.NotFound, "what Bob cannot see, he cannot change either");
        }
    }

    [Fact]
    public async Task A_customers_order_goes_through_checkout_like_anyone_elses()
    {
        _shop.SkipUnlessStarted();
        var alice = await _customers.SignInAsync("alice", Cancellation);

        var id = await PlaceAsync(alice);

        // Inventory and Payments answer from the outbox pollers, outside any request: they run as the
        // role the host logged in as, which the policy does not stop, and the order is confirmed.
        var deadline = DateTime.UtcNow + Settles;
        string? status;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);
            using var response = await SendAsync(HttpMethod.Get, $"/orders/{id}", alice);
            status = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("status").GetString();
        }
        while (status == "Placed" && DateTime.UtcNow < deadline);

        status.Should().Be("Confirmed");
    }

    /// <summary>Places a kilo of coffee as <paramref name="token"/>'s customer, asking again while Ordering has not heard Catalog's prices yet.</summary>
    private async Task<string> PlaceAsync(string token)
    {
        var deadline = DateTime.UtcNow + Settles;
        var order = new { street = "Oudegracht 1", city = "Utrecht", postalCode = "3511 AA", lines = new[] { new { sku = "COFFEE-1KG", quantity = 1 } } };

        while (true)
        {
            using var response = await SendAsync(HttpMethod.Post, "/orders", token, order);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetString()!;
            }

            var body = await response.Content.ReadAsStringAsync(Cancellation);
            if (response.StatusCode != HttpStatusCode.BadRequest || !body.Contains("not for sale") || DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException($"Placing the order answered {(int)response.StatusCode}: {body}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _shop.Client().SendAsync(request, Cancellation);
    }
}
