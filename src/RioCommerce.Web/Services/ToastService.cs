namespace RioCommerce.Web.Services;

// Lightweight scoped toast bus. Pages call Show(...); ToastHost (in AdminLayout) renders + auto-dismisses.
public class ToastService
{
    public event Action? OnChange;
    public List<ToastMessage> Toasts { get; } = new();

    public void Show(string message, string type = "success", int durationMs = 3500)
    {
        var t = new ToastMessage(Guid.NewGuid(), message, type);
        Toasts.Add(t);
        OnChange?.Invoke();
        _ = DismissLater(t, durationMs);
    }

    public void Success(string m) => Show(m, "success");
    public void Error(string m) => Show(m, "error", 5000);
    public void Info(string m) => Show(m, "info");

    public void Remove(ToastMessage t) { if (Toasts.Remove(t)) OnChange?.Invoke(); }

    private async Task DismissLater(ToastMessage t, int ms)
    {
        await Task.Delay(ms);
        Remove(t);
    }
}

public record ToastMessage(Guid Id, string Message, string Type);
