package io.github.eoftisreal.dis;

import android.media.MediaCodec;
import android.media.MediaFormat;
import android.os.Build;
import android.view.Surface;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicBoolean;

/** Hardware Surface rendering avoids decoding JPEGs, allocating bitmaps and drawing a CPU Canvas per frame. */
public final class H264Decoder implements AutoCloseable {
    public interface Ack { void consumed(int sequence) throws IOException; }
    public interface Failure { void failed(IOException error); }
    private final MediaCodec codec;
    private final Ack ack;
    private final Failure failure;
    private final AtomicBoolean running=new AtomicBoolean(true);
    private final ConcurrentHashMap<Long,Integer> pending=new ConcurrentHashMap<>();
    private final Thread output;
    private final DisplayView view;

    public H264Decoder(DisplayView view,int width,int height,int fps,byte[] configuration,Ack ack,Failure failure) throws IOException {
        this.ack=ack; this.failure=failure; this.view=view;
        byte[][] csd=H264ParameterSets.parse(configuration);
        Surface surface=view.awaitSurface(3000);
        MediaFormat format=MediaFormat.createVideoFormat("video/avc",width,height);
        format.setByteBuffer("csd-0",ByteBuffer.wrap(csd[0])); format.setByteBuffer("csd-1",ByteBuffer.wrap(csd[1]));
        format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE,Wire.MAX_FRAME);
        format.setInteger(MediaFormat.KEY_FRAME_RATE,fps);
        format.setInteger(MediaFormat.KEY_COLOR_STANDARD,MediaFormat.COLOR_STANDARD_BT709);
        format.setInteger(MediaFormat.KEY_COLOR_RANGE,MediaFormat.COLOR_RANGE_LIMITED);
        format.setInteger(MediaFormat.KEY_COLOR_TRANSFER,MediaFormat.COLOR_TRANSFER_SDR_VIDEO);
        codec=MediaCodec.createDecoderByType("video/avc");
        if (Build.VERSION.SDK_INT>=30 && codec.getCodecInfo().getCapabilitiesForType("video/avc")
            .isFeatureSupported(android.media.MediaCodecInfo.CodecCapabilities.FEATURE_LowLatency))
            format.setInteger(MediaFormat.KEY_LOW_LATENCY,1);
        try { codec.configure(format,surface,null,0); codec.start(); }
        catch (RuntimeException e) { codec.release(); throw new IOException("Android H.264 decoder could not start",e); }
        view.setVideoGeometry(width,height);
        output=new Thread(this::drain,"Dis video output"); output.start();
    }
    public void queue(byte[] data,long pts,int sequence) throws IOException {
        if (!running.get()) throw new IOException("Video decoder stopped");
        long deadline=System.nanoTime()+2_000_000_000L;
        try {
            while (running.get() && System.nanoTime()<deadline) {
                int index=codec.dequeueInputBuffer(10000);
                if (index<0) continue;
                ByteBuffer buffer=codec.getInputBuffer(index);
                if (buffer==null || buffer.capacity()<data.length) throw new IOException("Decoder input buffer is too small");
                if (pending.size()>=4 || pending.putIfAbsent(pts,sequence)!=null) throw new IOException("Decoder queue limit exceeded");
                buffer.clear(); buffer.put(data); codec.queueInputBuffer(index,0,data.length,pts,0); return;
            }
            throw new IOException("Video decoder input timed out");
        } catch (RuntimeException e) { throw new IOException("H.264 decoder input failed",e); }
    }
    private void drain() {
        MediaCodec.BufferInfo info=new MediaCodec.BufferInfo();
        try {
            while (running.get()) {
                int index=codec.dequeueOutputBuffer(info,10000);
                if (index<0) continue;
                Integer sequence=pending.remove(info.presentationTimeUs);
                boolean render=sequence!=null && (info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG)==0;
                codec.releaseOutputBuffer(index,render);
                if (sequence!=null) ack.consumed(sequence);
            }
        } catch (IOException | RuntimeException e) {
            if (running.get()) failure.failed(new IOException("Video decoder interrupted",e));
        }
    }
    @Override public void close() {
        if (!running.getAndSet(false)) return;
        try { output.join(1500); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
        try { codec.stop(); } catch (RuntimeException ignored) { }
        codec.release(); pending.clear(); view.setSurfaceInvalidated(null);
    }
}
