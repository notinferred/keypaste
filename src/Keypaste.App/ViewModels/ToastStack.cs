using System.Collections.ObjectModel;

namespace Keypaste.App.ViewModels;

internal enum ToastTone
{
    Success,
    Information,
    Warning,
}

/// <summary>One message in the corner, which times out unless the pointer is on it.</summary>
/// <remarks>Never put a secret in it: a toast is a line of plain text on screen.</remarks>
internal sealed class Toast : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;
    private readonly Action<Toast> _remove;
    private ITimer? _timer;
    private bool _held;

    internal Toast(string message, ToastTone tone, TimeProvider clock, Action<Action> post, Action<Toast> remove)
    {
        Message = message;
        Tone = tone;
        _clock = clock;
        _post = post;
        _remove = remove;
        CloseCommand = new RelayCommand(() => _remove(this));
        Restart();
    }

    internal string Message { get; }

    internal ToastTone Tone { get; }

    internal bool IsSuccess => Tone == ToastTone.Success;

    internal bool IsInformation => Tone == ToastTone.Information;

    internal bool IsWarning => Tone == ToastTone.Warning;

    internal RelayCommand CloseCommand { get; }

    /// <summary>The pointer came onto it, so its time stops.</summary>
    internal void Hold()
    {
        _held = true;
        _timer?.Dispose();
    }

    /// <summary>The pointer left, so it stays for its whole time again.</summary>
    internal void Release()
    {
        _held = false;
        Restart();
    }

    internal void Restart()
    {
        _timer?.Dispose();

        if (!_held)
        {
            _timer = _clock.CreateTimer(_ => _post(Expire), null, ToastStack.DurationOf(Message), Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose() => _timer?.Dispose();

    private void Expire()
    {
        if (!_held)
        {
            _remove(this);
        }
    }
}

/// <summary>What the toast band is saying: one toast, the newest replacing the one before, and while a dialog is up the newest waits for it to close.</summary>
internal sealed class ToastStack(TimeProvider clock, Action<Action> post) : IDisposable
{
    private static readonly TimeSpan _shortest = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan _perCharacter = TimeSpan.FromMilliseconds(60);
    private (string Message, ToastTone Tone)? _waiting;
    private bool _isHeld;

    internal ObservableCollection<Toast> Shown { get; } = [];

    internal static TimeSpan DurationOf(string message) => TimeSpan.FromTicks(Math.Max(_shortest.Ticks, _perCharacter.Ticks * message.Length));

    /// <summary>Whether a dialog is up, so a toast waits rather than lying over it.</summary>
    internal bool IsHeld
    {
        get => _isHeld;
        set
        {
            _isHeld = value;

            if (!value && _waiting is var (message, tone))
            {
                _waiting = null;
                Show(message, tone);
            }
        }
    }

    internal void Show(string message, ToastTone tone)
    {
        if (_isHeld)
        {
            _waiting = (message, tone);
            return;
        }

        if (Shown.FirstOrDefault(toast => toast.Message == message && toast.Tone == tone) is { } same)
        {
            same.Restart();
            return;
        }

        foreach (var older in Shown.ToList())
        {
            Remove(older);
        }

        Shown.Add(new Toast(message, tone, clock, post, Remove));
    }

    public void Dispose()
    {
        foreach (var toast in Shown.ToList())
        {
            Remove(toast);
        }
    }

    private void Remove(Toast toast)
    {
        toast.Dispose();
        Shown.Remove(toast);
    }
}
