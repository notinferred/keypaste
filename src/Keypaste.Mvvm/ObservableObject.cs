using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Keypaste.Mvvm;

/// <summary>
/// The smallest thing that makes a binding update, and what it updates along with it.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled rather than <c>CommunityToolkit.Mvvm</c>, for the reason D-0028 gave for writing a
/// TOML subset by hand and D-0019 gave for taking the narrow MCP package: this project is four small
/// files, and a package still enters <c>packages.lock.json</c>, still restores under
/// <c>--locked-mode</c>, and still turns the build red the day it draws a low-severity advisory
/// under <c>NuGetAudit</c> (docs/PRODUCT.md law 3.9).
/// </para>
/// <para>
/// A view model states once, usually in its constructor, what each computed property and each
/// command's <c>CanExecute</c> reads, with
/// <see cref="DependsOn(string, ReadOnlySpan{string})"/> and
/// <see cref="DependsOn(IRelayCommand, ReadOnlySpan{string})"/>. Raising a property then raises
/// everything that depends on it, directly or through another dependent, once each, and asks each
/// of those commands again; a setter is a single <see cref="Set{T}"/>.
/// </para>
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    private Dictionary<string, Dependents>? _dependents;
    private Dictionary<string, Reach>? _reach;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stores <paramref name="value"/> and raises <paramref name="name"/> when it differs.</summary>
    /// <returns>Whether the value changed.</returns>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    /// <summary>Raises <paramref name="name"/>, then every property and command that depends on it.</summary>
    protected void Raise([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        if (name is null || _dependents is null || !_dependents.ContainsKey(name))
        {
            return;
        }

        var reach = ReachOf(name);

        foreach (var dependent in reach.Properties)
        {
            PropertyChanged?.Invoke(this, dependent);
        }

        foreach (var command in reach.Commands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Declares that <paramref name="dependent"/> is computed from <paramref name="sources"/>.</summary>
    protected void DependsOn(string dependent, params ReadOnlySpan<string> sources)
    {
        ArgumentNullException.ThrowIfNull(dependent);

        foreach (var source in sources)
        {
            DependentsOf(source).Properties.Add(dependent);
        }
    }

    /// <summary>Declares that whether <paramref name="command"/> can run is computed from <paramref name="sources"/>.</summary>
    protected void DependsOn(IRelayCommand command, params ReadOnlySpan<string> sources)
    {
        ArgumentNullException.ThrowIfNull(command);

        foreach (var source in sources)
        {
            DependentsOf(source).Commands.Add(command);
        }
    }

    private Dependents DependentsOf(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _reach = null;
        _dependents ??= new Dictionary<string, Dependents>(StringComparer.Ordinal);

        if (!_dependents.TryGetValue(source, out var dependents))
        {
            dependents = new Dependents();
            _dependents.Add(source, dependents);
        }

        return dependents;
    }

    private Reach ReachOf(string source)
    {
        _reach ??= new Dictionary<string, Reach>(StringComparer.Ordinal);

        if (_reach.TryGetValue(source, out var reach))
        {
            return reach;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { source };
        var pending = new Queue<string>();
        var properties = new List<PropertyChangedEventArgs>();
        var commands = new List<IRelayCommand>();
        pending.Enqueue(source);

        while (pending.TryDequeue(out var next))
        {
            if (!_dependents!.TryGetValue(next, out var dependents))
            {
                continue;
            }

            foreach (var property in dependents.Properties)
            {
                if (seen.Add(property))
                {
                    properties.Add(new PropertyChangedEventArgs(property));
                    pending.Enqueue(property);
                }
            }

            foreach (var command in dependents.Commands)
            {
                if (!commands.Contains(command))
                {
                    commands.Add(command);
                }
            }
        }

        reach = new Reach([.. properties], [.. commands]);
        _reach.Add(source, reach);
        return reach;
    }

    private sealed class Dependents
    {
        internal List<string> Properties { get; } = [];

        internal List<IRelayCommand> Commands { get; } = [];
    }

    private sealed record Reach(PropertyChangedEventArgs[] Properties, IRelayCommand[] Commands);
}
