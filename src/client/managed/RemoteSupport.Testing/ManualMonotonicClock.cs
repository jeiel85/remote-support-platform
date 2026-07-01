namespace RemoteSupport.Testing;

public sealed class ManualMonotonicClock
{
    private long nanoseconds;

    public ulong Nanoseconds => checked((ulong)Interlocked.Read(ref nanoseconds));

    public void Advance(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        Interlocked.Add(ref nanoseconds, checked(duration.Ticks * 100));
    }
}
