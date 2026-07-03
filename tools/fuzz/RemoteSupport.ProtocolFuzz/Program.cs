using RemoteSupport.Protocol;

int iterations = 100_000;
List<string> corpus = [];
for (int index = 0; index < args.Length; index++)
{
    if (args[index] == "--iterations" && index + 1 < args.Length)
    {
        iterations = int.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
    }
    else corpus.Add(args[index]);
}
if (iterations is < 1 or > 10_000_000) throw new InvalidOperationException("Iteration count must be between 1 and 10,000,000.");

List<byte[]> seeds = corpus.Count == 0 ? ["RSP1"u8.ToArray()] : corpus.Select(File.ReadAllBytes).ToList();
Random random = new(0x52535031);
for (int iteration = 0; iteration < iterations; iteration++)
{
    byte[] seed = seeds[random.Next(seeds.Count)];
    int length = Math.Min(1024 * 1024 + PeerFrameCodec.HeaderSize, Math.Max(0, seed.Length + random.Next(-32, 257)));
    byte[] candidate = new byte[length];
    seed.AsSpan(0, Math.Min(seed.Length, length)).CopyTo(candidate);
    int mutations = random.Next(1, 17);
    for (int mutation = 0; mutation < mutations && candidate.Length > 0; mutation++)
    {
        candidate[random.Next(candidate.Length)] ^= (byte)(1 << random.Next(8));
    }
    try
    {
        _ = PeerFrameCodec.Decode(candidate, (PeerChannel)random.Next(0, 7), 1024 * 1024);
    }
    catch (PeerProtocolException)
    {
    }
}
Console.WriteLine($"Protocol fuzz target completed {iterations:N0} cases across {seeds.Count:N0} seed(s).");
