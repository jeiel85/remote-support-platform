namespace RemoteSupport.Media;

/// <summary>A bounded queue that discards the oldest superseded frame instead of accumulating latency.</summary>
public sealed class BoundedLatestFrameBuffer<T>
{
    private readonly Queue<T> frames;
    private readonly object sync = new();

    public BoundedLatestFrameBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 6);
        Capacity = capacity;
        frames = new Queue<T>(capacity);
    }

    public int Capacity { get; }

    public long DroppedFrames { get; private set; }

    public int Count
    {
        get
        {
            lock (sync)
            {
                return frames.Count;
            }
        }
    }

    public void Enqueue(T frame)
    {
        lock (sync)
        {
            if (frames.Count == Capacity)
            {
                frames.Dequeue();
                DroppedFrames++;
            }
            frames.Enqueue(frame);
        }
    }

    public bool TryDequeue(out T? frame)
    {
        lock (sync)
        {
            if (frames.Count == 0)
            {
                frame = default;
                return false;
            }
            frame = frames.Dequeue();
            return true;
        }
    }
}
