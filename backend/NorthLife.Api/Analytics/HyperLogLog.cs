using System.Buffers.Binary;
using System.Numerics;

namespace NorthLife.Api.Analytics;

/// <summary>
/// HyperLogLog cardinality sketch (Flajolet, Fusy, Gandouet and Meunier, 2007).
/// <para>
/// Each item is hashed to 64 bits. The first <c>p</c> bits pick one of <c>m = 2^p</c> registers and the
/// register keeps the largest "rank" seen: the position of the first 1-bit in the remaining bits. A
/// rank of k happens with probability 2^-k, so the registers together estimate how many distinct
/// items were added, using <c>m</c> bytes no matter how many items there are.
/// </para>
/// <para>
/// With p = 14 (16,384 registers) the standard error is 1.04 / sqrt(m), about 0.81%. Small sets use
/// linear counting over the empty registers, which is close to exact. Sketches merge by taking the
/// register-wise maximum, so daily sketches combine into weekly or monthly unique counts without
/// double-counting a visitor who came on several days; adding the same item twice changes nothing.
/// </para>
/// <para>
/// Storage uses a sparse encoding (index and rank pairs) while few registers are set, as in
/// HyperLogLog++ (Heule, Nunkesser and Hall, 2013), and switches to one byte per register once that
/// is smaller.
/// </para>
/// </summary>
public sealed class HyperLogLog
{
    public const int DefaultPrecision = 14;
    private const byte FormatVersion = 1;
    private const byte SparseFormat = 0;
    private const byte DenseFormat = 1;
    private const int HeaderLength = 3;

    private readonly byte[] _registers;

    public HyperLogLog(int precision = DefaultPrecision)
    {
        if (precision is < 4 or > 16) throw new ArgumentOutOfRangeException(nameof(precision), "Precision must be 4 to 16.");
        Precision = precision;
        _registers = new byte[1 << precision];
    }

    public int Precision { get; }

    public int RegisterCount => _registers.Length;

    /// <summary>Theoretical relative standard error, 1.04 / sqrt(m).</summary>
    public double StandardError => 1.04 / Math.Sqrt(_registers.Length);

    private int MaxRank => 64 - Precision + 1;

    public void Add(Guid item) => AddHash(Hash64(item));

    public void AddHash(ulong hash)
    {
        var index = (int)(hash >> (64 - Precision));
        // Rank of the remaining 64 - p bits: leading zeros + 1, or 64 - p + 1 when they are all zero.
        var rank = (byte)Math.Min(BitOperations.LeadingZeroCount(hash << Precision) + 1, MaxRank);
        if (rank > _registers[index]) _registers[index] = rank;
    }

    /// <summary>Union: afterwards this sketch estimates |A ∪ B|. Commutative, associative and idempotent.</summary>
    public void Merge(HyperLogLog other)
    {
        if (other.Precision != Precision) throw new ArgumentException("Sketches must have the same precision.", nameof(other));
        for (var index = 0; index < _registers.Length; index++)
        {
            if (other._registers[index] > _registers[index]) _registers[index] = other._registers[index];
        }
    }

    public double Estimate()
    {
        var m = _registers.Length;
        var sum = 0.0;
        var zeros = 0;
        foreach (var rank in _registers)
        {
            sum += Math.ScaleB(1.0, -rank);
            if (rank == 0) zeros++;
        }

        var alpha = m switch
        {
            16 => 0.673,
            32 => 0.697,
            64 => 0.709,
            _ => 0.7213 / (1 + 1.079 / m),
        };
        var raw = alpha * m * m / sum;

        // Small range: linear counting over empty registers is far more accurate there. A 64-bit
        // hash makes the original large-range correction unnecessary.
        return raw <= 2.5 * m && zeros > 0 ? m * Math.Log((double)m / zeros) : raw;
    }

    public long Count() => (long)Math.Round(Estimate());

    public byte[] Serialize()
    {
        var used = 0;
        foreach (var rank in _registers)
        {
            if (rank != 0) used++;
        }

        // A sparse entry takes 3 bytes (2 for the index, 1 for the rank).
        if (used * 3 + 2 < _registers.Length)
        {
            var sparse = new byte[HeaderLength + 2 + used * 3];
            WriteHeader(sparse, SparseFormat);
            BinaryPrimitives.WriteUInt16LittleEndian(sparse.AsSpan(HeaderLength), (ushort)used);
            var offset = HeaderLength + 2;
            for (var index = 0; index < _registers.Length; index++)
            {
                if (_registers[index] == 0) continue;
                BinaryPrimitives.WriteUInt16LittleEndian(sparse.AsSpan(offset), (ushort)index);
                sparse[offset + 2] = _registers[index];
                offset += 3;
            }

            return sparse;
        }

        var dense = new byte[HeaderLength + _registers.Length];
        WriteHeader(dense, DenseFormat);
        _registers.CopyTo(dense, HeaderLength);
        return dense;
    }

    public static HyperLogLog Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength || data[0] != FormatVersion) throw new FormatException("Unknown sketch format.");
        var sketch = new HyperLogLog(data[1] is >= 4 and <= 16 ? data[1] : throw new FormatException("Invalid sketch precision."));
        var body = data[HeaderLength..];

        switch (data[2])
        {
            case DenseFormat:
                if (body.Length != sketch._registers.Length) throw new FormatException("Dense sketch has the wrong length.");
                foreach (var rank in body)
                {
                    if (rank > sketch.MaxRank) throw new FormatException("Register rank out of range.");
                }

                body.CopyTo(sketch._registers);
                return sketch;

            case SparseFormat:
                if (body.Length < 2) throw new FormatException("Sparse sketch is truncated.");
                var count = BinaryPrimitives.ReadUInt16LittleEndian(body);
                if (body.Length != 2 + count * 3) throw new FormatException("Sparse sketch has the wrong length.");
                for (var entry = 0; entry < count; entry++)
                {
                    var offset = 2 + entry * 3;
                    var index = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
                    var rank = body[offset + 2];
                    if (index >= sketch._registers.Length || rank == 0 || rank > sketch.MaxRank)
                    {
                        throw new FormatException("Sparse entry out of range.");
                    }

                    sketch._registers[index] = rank;
                }

                return sketch;

            default:
                throw new FormatException("Unknown sketch encoding.");
        }
    }

    /// <summary>Union of stored sketches; null or empty input gives an empty sketch.</summary>
    public static HyperLogLog Union(IEnumerable<byte[]?> sketches, int precision = DefaultPrecision)
    {
        var union = new HyperLogLog(precision);
        foreach (var data in sketches)
        {
            if (data is { Length: > 0 }) union.Merge(Deserialize(data));
        }

        return union;
    }

    /// <summary>
    /// 64-bit hash of a GUID. Visitor ids are random, but hashing keeps the estimate valid for any id
    /// scheme. SplitMix64's finalizer (Steele, Lea and Flood, 2014) gives full avalanche.
    /// </summary>
    public static ulong Hash64(Guid item)
    {
        Span<byte> bytes = stackalloc byte[16];
        item.TryWriteBytes(bytes);
        var low = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        var high = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        return Mix(low ^ Mix(high + 0x9E3779B97F4A7C15UL));
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private void WriteHeader(Span<byte> target, byte format)
    {
        target[0] = FormatVersion;
        target[1] = (byte)Precision;
        target[2] = format;
    }
}
