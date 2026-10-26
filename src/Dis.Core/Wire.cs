using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dis.Core;

public enum MessageType : byte { Hello = 1, Config, Frame, Input, Ack, Ping, Pong, Error, CodecConfig }
public readonly record struct Packet(MessageType Type, uint Sequence, byte[] Payload);

/// <summary>Versioned, bounded framing shared by every transport. Integers are big endian.</summary>
public static class Wire
{
    public const int HeaderSize = 16;
    public const int MaxFrameBytes = 8 * 1024 * 1024;
    public const int MaxJsonBytes = 4096;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void ValidateLength(MessageType type, int length)
    {
        bool valid = type switch
        {
            MessageType.Frame => length is >= 13 and <= MaxFrameBytes,
            MessageType.Input => length == 24,
            MessageType.CodecConfig => length is >= 8 and <= 65536,
            MessageType.Ack => length == 0,
            MessageType.Ping or MessageType.Pong => length == 8,
            MessageType.Hello or MessageType.Config or MessageType.Error => length is >= 2 and <= MaxJsonBytes,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Invalid message type or payload length.");
    }

    public static async ValueTask<Packet> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, ct);
        if (!header.AsSpan(0, 4).SequenceEqual("DIS1"u8) || header[5] != 0 || header[6] != 0 || header[7] != 0)
            throw new InvalidDataException("Unsupported Dis protocol header.");
        var type = (MessageType)header[4];
        uint length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        if (length > int.MaxValue) throw new InvalidDataException("Message too large.");
        ValidateLength(type, (int)length); // Validate before allocating peer-controlled memory.
        byte[] payload = new byte[(int)length];
        await stream.ReadExactlyAsync(payload, ct);
        return new(type, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12)), payload);
    }

    // Exactly one writer owns each direction of a connection, or the caller must lock around this operation.
    public static async ValueTask WriteAsync(Stream stream, Packet packet, CancellationToken ct = default)
    {
        ValidateLength(packet.Type, packet.Payload.Length);
        byte[] header = new byte[HeaderSize];
        "DIS1"u8.CopyTo(header);
        header[4] = (byte)packet.Type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), packet.Payload.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), packet.Sequence);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(packet.Payload, ct);
    }

    public static Packet Json<T>(MessageType type, T value, uint sequence = 0) =>
        new(type, sequence, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

    public static T ParseJson<T>(Packet packet) =>
        JsonSerializer.Deserialize<T>(packet.Payload, JsonOptions) ?? throw new InvalidDataException("Empty JSON.");
}
