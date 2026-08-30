using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace RioCommerce.Web.Services;

/// <summary>
/// Keeps a list screen's filters alive while the user stays inside that screen's section, and drops
/// them the moment they leave it.
///
/// <para>The case this exists for: an admin filters the Orders list, opens one order to look at it,
/// and comes back. Re-entering the list built a fresh component, so the filters were gone and the
/// work had to be redone for every order they wanted to check. Persisting them forever is the other
/// wrong answer — returning to Orders from somewhere else in the admin and finding yesterday's
/// filters silently applied is worse, because the list looks complete when it is not.</para>
///
/// <para>Scoped, so in Blazor Server this lives for one circuit — the state never crosses users or
/// browser tabs, and a page reload starts clean.</para>
/// </summary>
public sealed class AdminScreenState : IDisposable
{
    private readonly NavigationManager _nav;
    private readonly Dictionary<string, object> _stash = new(StringComparer.OrdinalIgnoreCase);

    public AdminScreenState(NavigationManager nav)
    {
        _nav = nav;
        _nav.LocationChanged += OnLocationChanged;
    }

    /// <summary>Stash this screen's state under its section path (e.g. <c>/admin/orders</c>).</summary>
    public void Save(string section, object state) => _stash[section] = state;

    /// <summary>The stashed state, or null once the user has left the section.</summary>
    public T? Restore<T>(string section) where T : class
        => _stash.TryGetValue(section, out var v) ? v as T : null;

    public void Forget(string section) => _stash.Remove(section);

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var path = CurrentPath(e.Location);
        foreach (var section in _stash.Keys.ToList())
            if (!IsInside(path, section))
                _stash.Remove(section);
    }

    private string CurrentPath(string absoluteUri)
    {
        var relative = _nav.ToBaseRelativePath(absoluteUri);
        var cut = relative.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) relative = relative[..cut];
        return "/" + relative.Trim('/');
    }

    /// <summary>
    /// True for the section itself and anything below it — <c>/admin/orders</c> keeps its filters
    /// while the user is on <c>/admin/orders/{id}</c>.
    ///
    /// <para>The trailing-slash test matters: a plain <c>StartsWith</c> would treat
    /// <c>/admin/orders-import</c> as being inside <c>/admin/orders</c>.</para>
    /// </summary>
    private static bool IsInside(string path, string section)
        => path.Equals(section, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(section.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _nav.LocationChanged -= OnLocationChanged;
}
