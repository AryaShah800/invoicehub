namespace InvoiceHub.Web.Services;

public enum ToastKind { Info, Success }

public sealed record Toast(Guid Id, string Title, string Text, ToastKind Kind);

public sealed class ToastService
{
    private readonly List<Toast> _toasts = [];

    public IReadOnlyList<Toast> Toasts => _toasts;
    public event Action? Changed;

    public Toast Show(string title, string text, ToastKind kind = ToastKind.Info)
    {
        var toast = new Toast(Guid.NewGuid(), title, text, kind);
        _toasts.Add(toast);
        Changed?.Invoke();
        return toast;
    }

    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0)
            Changed?.Invoke();
    }
}
