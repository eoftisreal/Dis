package io.github.eoftisreal.dis;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.function.LongSupplier;

/** Monotonic, local measurements. Host frame timestamps are never compared with Android's clock. */
public final class StreamStats {
    public static final class Sample {
        public final double streamFps, decoderFps, mbps, roundTripMs;
        public final long receivedFrames, decodedFrames, uptimeSeconds;
        public final double decoderMs,inputWaitMs,ackWriteMs;
        Sample(double streamFps,double decoderFps,double mbps,double roundTripMs,long receivedFrames,long decodedFrames,long uptimeSeconds,double decoderMs,double inputWaitMs,double ackWriteMs) {
            this.streamFps=streamFps;this.decoderFps=decoderFps;this.mbps=mbps;this.roundTripMs=roundTripMs;
            this.receivedFrames=receivedFrames;this.decodedFrames=decodedFrames;this.uptimeSeconds=uptimeSeconds;
            this.decoderMs=decoderMs;this.inputWaitMs=inputWaitMs;this.ackWriteMs=ackWriteMs;
        }
    }
    private final LongSupplier clock;
    private final long started;
    private long sampled, bytes, frames, decoded, sampledBytes, sampledFrames, sampledDecoded;
    private long timedFrames,decoderNanos,inputWaitNanos,ackWrites,ackNanos;
    private long sampledTimedFrames,sampledDecoderNanos,sampledInputWaitNanos,sampledAckWrites,sampledAckNanos;
    private int pingSequence;
    private long pingSent;
    private boolean pingPending;
    private boolean hasPong;
    private long lastPong;
    private double roundTripMs=-1;
    public StreamStats() { this(System::nanoTime); }
    StreamStats(LongSupplier clock) { this.clock=clock; started=sampled=clock.getAsLong(); }
    public synchronized void receivedFrame(int packetBytes) {
        if(packetBytes<0)throw new IllegalArgumentException("Negative byte count");
        bytes+=packetBytes;frames++;
    }
    public synchronized void decodedFrame() { decoded++; }
    public synchronized void decodedFrame(long decoderTime,long inputWait) {
        if(decoderTime<0||inputWait<0)throw new IllegalArgumentException("Negative decoder timing");
        decoded++;timedFrames++;decoderNanos+=decoderTime;inputWaitNanos+=inputWait;
    }
    public synchronized void ackWrite(long nanos) {
        if(nanos<0)throw new IllegalArgumentException("Negative ACK timing");
        ackWrites++;ackNanos+=nanos;
    }
    public synchronized byte[] ping(int sequence) {
        pingSequence=sequence;pingSent=clock.getAsLong();pingPending=true;
        return ByteBuffer.allocate(8).order(ByteOrder.BIG_ENDIAN).putLong(pingSent).array();
    }
    public synchronized boolean pong(int sequence,byte[] payload) {
        if(!pingPending || sequence!=pingSequence || payload==null || payload.length!=8
            || ByteBuffer.wrap(payload).order(ByteOrder.BIG_ENDIAN).getLong()!=pingSent)return false;
        long elapsed=clock.getAsLong()-pingSent;
        if(elapsed<0 || elapsed>30_000_000_000L)return false;
        roundTripMs=elapsed/1_000_000.0;pingPending=false;hasPong=true;lastPong=clock.getAsLong();return true;
    }
    public synchronized Sample sample() {
        long now=clock.getAsLong(),elapsed=now-sampled;
        if(elapsed<1_000_000_000L)return null;
        double seconds=elapsed/1_000_000_000.0;
        double rtt=!hasPong || now-lastPong>3_000_000_000L ? -1 : roundTripMs;
        Sample result=new Sample((frames-sampledFrames)/seconds,(decoded-sampledDecoded)/seconds,
            (bytes-sampledBytes)*8/seconds/1_000_000.0,rtt,frames,decoded,Math.max(0,(now-started)/1_000_000_000L),
            averageMillis(decoderNanos-sampledDecoderNanos,timedFrames-sampledTimedFrames),
            averageMillis(inputWaitNanos-sampledInputWaitNanos,timedFrames-sampledTimedFrames),
            averageMillis(ackNanos-sampledAckNanos,ackWrites-sampledAckWrites));
        sampled=now;sampledFrames=frames;sampledDecoded=decoded;sampledBytes=bytes;
        sampledTimedFrames=timedFrames;sampledDecoderNanos=decoderNanos;sampledInputWaitNanos=inputWaitNanos;
        sampledAckWrites=ackWrites;sampledAckNanos=ackNanos;return result;
    }
    private static double averageMillis(long nanos,long count){return count>0?nanos/(double)count/1_000_000.0:-1;}
}
