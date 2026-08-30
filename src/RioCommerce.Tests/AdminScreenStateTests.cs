using RioCommerce.Web.Services;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// Filters survive a trip into an order and die on the way out of the section.
///
/// <para>Both halves matter. Losing them meant re-entering the same filters for every order the
/// admin wanted to open; keeping them forever is worse, because arriving at Orders from elsewhere
/// in the admin and finding an old filter silently applied makes a partial list look complete.</para>
/// </summary>
public class AdminScreenStateTests
{
    /// <summary>Minimal NavigationManager — the real one needs a host, and only the URI and the
    /// LocationChanged event matter here.</summary>
    private sealed class FakeNav : NavigationManager
    {
        public FakeNav(string uri = "https://localhost/admin/orders")
            => Initialize("https://localhost/", uri);

        public void Go(string relative)
        {
            Uri = ToAbsoluteUri(relative).ToString();
            NotifyLocationChanged(isInterceptedLink: false);
        }
    }

    private sealed class Filters
    {
        public string? Status { get; init; }
        public int Page { get; init; }
    }

    private const string Orders = "/admin/orders";

    // ── Kept inside the section ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FiltersComeBackAfterOpeningAnOrder()
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid", Page = 2 });

        nav.Go("/admin/orders/712de2e5-7772-4820-b7a5-fb402df2435b");   // open an order
        nav.Go("/admin/orders");                                        // and back

        var restored = state.Restore<Filters>(Orders);
        Assert.NotNull(restored);
        Assert.Equal("Paid", restored!.Status);
        Assert.Equal(2, restored.Page);
    }

    [Fact]
    public void ReloadingTheListItselfKeepsThem()
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid" });

        nav.Go("/admin/orders");

        Assert.NotNull(state.Restore<Filters>(Orders));
    }

    [Fact]
    public void AQueryStringOrFragmentDoesNotCountAsLeaving()
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid" });

        nav.Go("/admin/orders?page=3");
        Assert.NotNull(state.Restore<Filters>(Orders));

        nav.Go("/admin/orders/abc#notes");
        Assert.NotNull(state.Restore<Filters>(Orders));
    }

    // ── Dropped on leaving the section ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/admin/products")]
    [InlineData("/admin/dashboard")]
    [InlineData("/admin/reports/sales")]
    [InlineData("/")]
    public void GoingToAnotherSectionClearsThem(string destination)
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid" });

        nav.Go(destination);

        Assert.Null(state.Restore<Filters>(Orders));
    }

    [Fact]
    public void ClearedStateStaysClearedOnReturn()
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid" });

        nav.Go("/admin/products");
        nav.Go("/admin/orders");

        // Arriving from elsewhere must show an unfiltered list — a stale filter would make a partial
        // list look like the whole thing.
        Assert.Null(state.Restore<Filters>(Orders));
    }

    /// <summary>A sibling route that merely starts with the same text is a different screen.</summary>
    [Theory]
    [InlineData("/admin/orders-import")]
    [InlineData("/admin/ordersomething")]
    public void ASimilarlyNamedRouteIsNotInsideTheSection(string destination)
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid" });

        nav.Go(destination);

        Assert.Null(state.Restore<Filters>(Orders));
    }

    // ── Bookkeeping ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SectionsAreIndependent()
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid" });
        state.Save("/admin/customers", new Filters { Status = "Active" });

        nav.Go("/admin/orders/abc");

        Assert.NotNull(state.Restore<Filters>(Orders));
        Assert.Null(state.Restore<Filters>("/admin/customers"));   // left that one
    }

    [Fact]
    public void SavingTwiceKeepsTheLatest()
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid" });
        state.Save(Orders, new Filters { Status = "Pending" });

        Assert.Equal("Pending", state.Restore<Filters>(Orders)!.Status);
    }

    [Fact]
    public void NothingSavedRestoresNull()
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);

        Assert.Null(state.Restore<Filters>(Orders));
    }

    [Fact]
    public void ForgetDropsItImmediately()
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid" });

        state.Forget(Orders);

        Assert.Null(state.Restore<Filters>(Orders));
    }

    [Fact]
    public void RestoringAsTheWrongTypeReturnsNullRatherThanThrowing()
    {
        var nav = new FakeNav();
        using var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid" });

        Assert.Null(state.Restore<string>(Orders));
    }

    [Fact]
    public void DisposingStopsListening()
    {
        var nav = new FakeNav();
        var state = new AdminScreenState(nav);
        state.Save(Orders, new Filters { Status = "Paid" });
        state.Dispose();

        // Must not throw into a disposed component when the circuit navigates on.
        nav.Go("/admin/products");
    }
}
