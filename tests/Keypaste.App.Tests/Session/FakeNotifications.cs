using Keypaste.App.Notifications;

namespace Keypaste.App.Tests.Session;

/// <summary>How <see cref="FakeNotifications"/> answers a post.</summary>
public enum SystemAnswer
{
    Accepts,
    Refuses,
    RefusesAfterAMoment,
    Throws,
    NeverAnswers,
    Held,
    Stalls,
}

/// <summary>A system that records each notice it is given, and answers as a test says.</summary>
internal sealed class FakeNotifications : ISystemNotifications
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Post> _posts = [];

    internal SystemAnswer Answers { get; init; } = SystemAnswer.Accepts;

    internal bool RemovalThrows { get; init; }

    /// <summary>The notices given so far; a post reaches this from the thread pool, some time after it is asked for.</summary>
    internal IReadOnlyList<Post> Posts
    {
        get
        {
            lock (_posts)
            {
                return [.. _posts];
            }
        }
    }

    /// <summary>Lets a post held under <see cref="SystemAnswer.Held"/> be accepted, and one under <see cref="SystemAnswer.Stalls"/> return.</summary>
    internal void Release() => _released.TrySetResult();

    public async Task<IPostedNotice?> PostAsync(SystemNotice notice, Action pressed)
    {
        var post = new Post(notice, pressed, RemovalThrows);

        lock (_posts)
        {
            _posts.Add(post);
        }

        switch (Answers)
        {
            case SystemAnswer.Refuses:
                return null;
            case SystemAnswer.RefusesAfterAMoment:
                await Task.Delay(100).ConfigureAwait(false);
                return null;
            case SystemAnswer.Stalls:
                // Blocks the calling thread before the first await, as a platform's call into the system can.
                _released.Task.GetAwaiter().GetResult();
                return null;
            case SystemAnswer.Throws:
                throw new InvalidOperationException("the system fell over");
            case SystemAnswer.NeverAnswers:
                await new TaskCompletionSource().Task.ConfigureAwait(false);
                return null;
            case SystemAnswer.Held:
                await _released.Task.ConfigureAwait(false);
                return post;
            default:
                return post;
        }
    }

    public void Dispose() => Release();

    internal sealed class Post(SystemNotice notice, Action pressed, bool removalThrows) : IPostedNotice
    {
        internal SystemNotice Notice { get; } = notice;

        /// <summary>What the system would do when the person presses it, whether or not it was removed: a press can arrive late.</summary>
        internal Action Pressed { get; } = pressed;

        private int _removals;

        internal int Removals => Volatile.Read(ref _removals);

        internal bool Removed => Removals > 0;

        public void Remove()
        {
            Interlocked.Increment(ref _removals);

            if (removalThrows)
            {
                throw new InvalidOperationException("the system kept it");
            }
        }
    }
}
