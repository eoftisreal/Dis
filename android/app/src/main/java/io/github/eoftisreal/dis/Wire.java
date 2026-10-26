package io.github.eoftisreal.dis;

import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** DIS1. No Android dependencies, to allow independent cross-language conformance tests. */
public final class Wire {
    public static final int HELLO=1, CONFIG=2, FRAME=3, INPUT=4, ACK=5, PING=6, PONG=7, ERROR=8;
    public static final int MAX_FRAME=8*1024*1024;
    private Wire() { }

    public static final class Packet {
        public final int type, sequence;
        public final byte[] payload;
        public Packet(int type, int sequence, byte[] payload) { this.type=type; this.sequence=sequence; this.payload=payload; }
    }

    public static void validateLength(int type, int length) throws IOException {
        boolean valid;
        switch (type) {
            case FRAME: valid=length>=13 && length<=MAX_FRAME; break;
            case INPUT: valid=length==24; break;
            case ACK: valid=length==0; break;
            case PING: case PONG: valid=length==8; break;
            case HELLO: case CONFIG: case ERROR: valid=length>=2 && length<=4096; break;
            default: valid=false;
        }
        if (!valid) throw new IOException("Invalid message type or payload length");
    }

    public static Packet read(DataInputStream input) throws IOException {
        if (input.readInt()!=0x44495331) throw new IOException("Unsupported Dis protocol");
        int type=input.readUnsignedByte();
        if (input.readUnsignedByte()!=0 || input.readUnsignedShort()!=0) throw new IOException("Unsupported protocol flags");
        int length=input.readInt();
        int sequence=input.readInt();
        validateLength(type,length);
        byte[] payload=new byte[length];
        input.readFully(payload);
        return new Packet(type,sequence,payload);
    }

    /** Callers serialize the entire operation, including both header and body. */
    public static void write(DataOutputStream out, Packet packet) throws IOException {
        validateLength(packet.type,packet.payload.length);
        out.writeInt(0x44495331); out.writeByte(packet.type); out.writeByte(0); out.writeShort(0);
        out.writeInt(packet.payload.length); out.writeInt(packet.sequence); out.write(packet.payload); out.flush();
    }

    public static byte[] pointer(int kind, int action, int buttons, float x, float y, float pressure, float tiltX, float tiltY) {
        return ByteBuffer.allocate(24).order(ByteOrder.BIG_ENDIAN).put((byte)kind).put((byte)action)
            .putShort((short)buttons).putFloat(x).putFloat(y).putFloat(pressure).putFloat(tiltX).putFloat(tiltY).array();
    }
}
