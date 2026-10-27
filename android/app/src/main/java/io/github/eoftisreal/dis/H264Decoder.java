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
    private final String description;

    public H264Decoder(DisplayView view,int width,int height,int fps,byte[] configuration,Ack ack,Failure failure) throws IOException {
        this.ack=ack; this.failure=failure; this.view=view;
        byte[][] csd=H264ParameterSets.parse(configuration);
        Surface surface=view.awaitSurface(3000);
        Started started=start(surface,width,height,fps,csd);
        codec=started.codec;
        description=started.description;
        view.setVideoGeometry(width,height);
        output=new Thread(this::drain,"Dis video output"); output.start();
    }

    public String description() { return description; }

    private static Started start(Surface surface,int width,int height,int fps,byte[][] csd) throws IOException {
        Exception last=null;
        for (boolean tuned:new boolean[]{true,false}) {
            MediaCodec candidate=null;
            try {
                candidate=MediaCodec.createDecoderByType("video/avc");
                MediaFormat format=format(width,height,fps,csd);
                boolean lowLatency=false;
                if (tuned) {
                    format.setInteger(MediaFormat.KEY_PRIORITY,0);
                    format.setInteger(MediaFormat.KEY_OPERATING_RATE,fps);
                    if (Build.VERSION.SDK_INT>=30 && candidate.getCodecInfo().getCapabilitiesForType("video/avc")
                        .isFeatureSupported(android.media.MediaCodecInfo.CodecCapabilities.FEATURE_LowLatency)) {
                        format.setInteger(MediaFormat.KEY_LOW_LATENCY,1);
                        lowLatency=true;
                    }
                }
                candidate.configure(format,surface,null,0);
                candidate.start();
                return new Started(candidate,candidate.getName()+(lowLatency ? " / low latency" : tuned ? " / realtime" : " / compatibility"));
            } catch (IOException | RuntimeException e) {
                last=e;
                if (candidate!=null) try { candidate.release(); } catch (RuntimeException ignored) { }
            }
        }
        throw new IOException("Android H.264 decoder could not start",last);
    }

    private static MediaFormat format(int width,int height,int fps,byte[][] csd) {
        MediaFormat format=MediaFormat.createVideoFormat("video/avc",width,height);
        format.setByteBuffer("csd-0",ByteBuffer.wrap(csd[0])); format.setByteBuffer("csd-1",ByteBuffer.wrap(csd[1]));
        format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE,Wire.MAX_FRAME);
        format.setInteger(MediaFormat.KEY_FRAME_RATE,fps);
        format.setInteger(MediaFormat.KEY_COLOR_STANDARD,MediaFormat.COLOR_STANDARD_BT709);
        format.setInteger(MediaFormat.KEY_COLOR_RANGE,MediaFormat.COLOR_RANGE_LIMITED);
        format.setInteger(MediaFormat.KEY_COLOR_TRANSFER,MediaFormat.COLOR_TRANSFER_SDR_VIDEO);
        return format;
    }

    private static final class Started {
        final MediaCodec codec;
        final String description;
        Started(MediaCodec codec,String description) { this.codec=codec; this.description=description; }
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
                // Host PTS identifies an access unit; only the phone's clock schedules its Surface.
                if (render) codec.releaseOutputBuffer(index,System.nanoTime());
                else codec.releaseOutputBuffer(index,false);
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
