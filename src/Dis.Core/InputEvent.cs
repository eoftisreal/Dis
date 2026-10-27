using System.Buffers.Binary;

namespace Dis.Core;

public enum PointerKind : byte { Mouse, Pen, Touch }
public enum PointerAction : byte { Down, Move, Up, Cancel, Hover, Scroll, ContextClick }

public readonly record struct InputEvent(PointerKind Kind, PointerAction Action, ushort Buttons,
    float X, float Y, float Pressure, float TiltX, float TiltY)
{
    public static InputEvent Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 24) throw new InvalidDataException("Invalid pointer packet.");
        var e = new InputEvent((PointerKind)bytes[0], (PointerAction)bytes[1],
            BinaryPrimitives.ReadUInt16BigEndian(bytes[2..]),
            BinaryPrimitives.ReadSingleBigEndian(bytes[4..]), BinaryPrimitives.ReadSingleBigEndian(bytes[8..]),
            BinaryPrimitives.ReadSingleBigEndian(bytes[12..]), BinaryPrimitives.ReadSingleBigEndian(bytes[16..]),
            BinaryPrimitives.ReadSingleBigEndian(bytes[20..]));
        if (!Enum.IsDefined(e.Kind) || !Enum.IsDefined(e.Action) || e.Buttons > 3 ||
            !In(e.X, 0, 1) || !In(e.Y, 0, 1) || !In(e.Pressure, 0, 1) ||
            !In(e.TiltX, -90, 90) || !In(e.TiltY, -90, 90))
            throw new InvalidDataException("Pointer value outside allowed range.");
        if ((e.Action is PointerAction.Scroll or PointerAction.ContextClick) && (e.Kind != PointerKind.Mouse || e.Buttons != 0 || e.Pressure != 0))
            throw new InvalidDataException("Invalid mouse gesture.");
        if (e.Kind == PointerKind.Touch && (e.Action == PointerAction.Hover || e.Buttons != 0))
            throw new InvalidDataException("Invalid touch contact.");
        return e;
    }

    private static bool In(float value, float min, float max) => float.IsFinite(value) && value >= min && value <= max;
}
