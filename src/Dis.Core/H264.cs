using System.Buffers.Binary;

namespace Dis.Core;

public static class H264
{
    public static byte[] AnnexB(ReadOnlySpan<byte> data)
    {
        if (data.Length > Wire.MaxFrameBytes) throw new InvalidDataException("H.264 access unit too large.");
        if (data.StartsWith(new byte[] { 0, 0, 1 }) || data.StartsWith(new byte[] { 0, 0, 0, 1 })) return data.ToArray();
        using var output = new MemoryStream();
        int offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 4) throw new InvalidDataException("Truncated H.264 NAL size.");
            uint size = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]); offset += 4;
            if (size == 0 || size > data.Length - offset) throw new InvalidDataException("Invalid H.264 NAL size.");
            output.Write(new byte[] { 0, 0, 0, 1 }); output.Write(data.Slice(offset, (int)size)); offset += (int)size;
        }
        return output.ToArray();
    }

    public static byte[] ParameterSets(ReadOnlySpan<byte> configuration, ReadOnlySpan<byte> firstFrame)
    {
        var sets = new Dictionary<int, byte[]>();
        if (configuration.Length > 0 && configuration[0] == 1)
        {
            if (configuration.Length < 7) throw new InvalidDataException("Truncated AVC configuration.");
            sets = AvccSets(configuration);
        }
        else if (configuration.Length > 0) AddNals(AnnexB(configuration), sets);
        if (firstFrame.Length > 0) AddNals(AnnexB(firstFrame), sets);
        if (!sets.ContainsKey(7) || !sets.ContainsKey(8)) throw new InvalidDataException("H.264 encoder did not supply SPS/PPS.");
        return [.. sets[7], .. sets[8]];
    }

    private static Dictionary<int, byte[]> AvccSets(ReadOnlySpan<byte> data)
    {
        var result = new Dictionary<int, byte[]>();
        int offset = 6;
        int count = data[5] & 31;
        for (int group = 0; group < 2; group++)
        {
            for (int i = 0; i < count; i++)
            {
                if (offset + 2 > data.Length) throw new InvalidDataException("Truncated AVC parameter set.");
                int length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]); offset += 2;
                if (length < 1 || length > data.Length - offset) throw new InvalidDataException("Invalid AVC parameter set.");
                int type = data[offset] & 31;
                if (type is 7 or 8) result[type] = [0, 0, 0, 1, .. data.Slice(offset, length)];
                offset += length;
            }
            if (group == 0)
            {
                if (offset >= data.Length) throw new InvalidDataException("Missing AVC PPS count.");
                count = data[offset++];
            }
        }
        return result;
    }

    private static void AddNals(byte[] data, Dictionary<int, byte[]> result)
    {
        int start = Start(data, 0, out int prefix);
        while (start >= 0)
        {
            int content = start + prefix;
            int next = Start(data, content, out int nextPrefix);
            int end = next < 0 ? data.Length : next;
            if (content < end && (data[content] & 31) is 7 or 8)
                result[data[content] & 31] = [0, 0, 0, 1, .. data.AsSpan(content, end - content)];
            start = next; prefix = nextPrefix;
        }
    }
    private static int Start(byte[] data, int offset, out int prefix)
    {
        for (int i = offset; i + 2 < data.Length; i++)
            if (data[i] == 0 && data[i + 1] == 0)
            {
                if (data[i + 2] == 1) { prefix = 3; return i; }
                if (i + 3 < data.Length && data[i + 2] == 0 && data[i + 3] == 1) { prefix = 4; return i; }
            }
        prefix = 0; return -1;
    }
}
