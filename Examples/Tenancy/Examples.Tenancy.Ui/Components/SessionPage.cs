using Examples.Tenancy.Ui.Api;
using Examples.Tenancy.Ui.Session;
using Microsoft.AspNetCore.Components;

namespace Examples.Tenancy.Ui.Components;

/// <summary>
/// A page that shows what the API answers the signed-in person in their tenant: it loads when it opens, and again
/// whenever the person or the tenant changes, so picking another tenant in the top bar redraws what is on screen.
/// </summary>
public abstract class SessionPage : ComponentBase, IDisposable
{
    /// <summary>Who is signed in, and in which tenant.</summary>
    [Inject]
    protected UiSession Session { get; set; } = default!;

    /// <summary>The Host's API.</summary>
    [Inject]
    protected SampleApi Api { get; set; } = default!;

    /// <summary>Whether a load is under way.</summary>
    protected bool Loading { get; private set; }

    /// <summary>Asks the API for what the page shows.</summary>
    protected abstract Task LoadAsync();

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        Session.Changed += OnSessionChanged;
        await ReloadAsync();
    }

    /// <summary>Loads again, for instance after an action succeeded.</summary>
    protected async Task ReloadAsync()
    {
        if (!Session.IsSignedIn)
        {
            return;
        }

        Loading = true;
        try
        {
            await LoadAsync();
        }
        finally
        {
            Loading = false;
        }
    }

    private void OnSessionChanged()
        => _ = InvokeAsync(async () =>
        {
            await ReloadAsync();
            StateHasChanged();
        });

    /// <inheritdoc />
    public void Dispose()
    {
        Session.Changed -= OnSessionChanged;
        GC.SuppressFinalize(this);
    }
}
