using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Keypaste.App.Views;

/// <summary>What a drag's data says about the files in it.</summary>
internal static class FileDragData
{
    /// <summary>The local path of the one file dragged, or null for several, for none and for one the platform does not name yet.</summary>
    internal static string? SinglePath(IDataTransfer data) =>
        !HasMany(data) && data.TryGetFile() is { } file ? file.TryGetLocalPath() : null;

    /// <summary>Whether more than one file is dragged, which no flow here takes at once.</summary>
    internal static bool HasMany(IDataTransfer data) => data.TryGetFiles() is { Length: > 1 };

    /// <summary>One judgement per target per drag: the over event comes with every pointer move, and judging one reads a file or the whole vault.</summary>
    /// <typeparam name="T">What the judgement comes to.</typeparam>
    internal sealed class Judged<T>
    {
        private (string Path, T Answer)? _last;

        internal T Of(string path, Func<string, T> judge)
        {
            if (_last is { } last && string.Equals(last.Path, path, StringComparison.Ordinal))
            {
                return last.Answer;
            }

            var answer = judge(path);
            _last = (path, answer);
            return answer;
        }

        internal void Forget() => _last = null;
    }
}
