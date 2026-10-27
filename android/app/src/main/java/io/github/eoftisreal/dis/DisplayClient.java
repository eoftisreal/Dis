package io.github.eoftisreal.dis;

import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLHandshakeException;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.TrustManager;
import org.json.JSONException;
import org.json.JSONObject;

public final class DisplayClient implements AutoCloseable, DisplayView.InputSink {
    public interface Listener { void status(String text); void ended(); }
    private final ConnectionSpec spec;
    private final DisplayView view;
    private final Listener listener;
    private final AtomicBoolean running=new AtomicBoolean(true);
    private final InputQueue inputs=new InputQueue();
    private final Object writes=new Object();
    private volatile Socket socket;
    private Thread worker;
    private volatile boolean connected;
    private volatile boolean forceJpeg;
    private final AtomicBoolean surfaceReset=new AtomicBoolean(false);

    public DisplayClient(ConnectionSpec spec, DisplayView view, Listener listener) { this.spec=spec; this.view=view; this.listener=listener; }
    public void start() { worker=new Thread(this::run,"Dis connection"); worker.start(); }

    private void run() {
        try {
            int attempt=0;
            while (running.get()) {
                long started=System.nanoTime();
                try { streamOnce(); break; }
                catch (SSLHandshakeException e) { listener.status("Certificate verification failed. Copy a fresh connection link from Windows."); break; }
                catch (ProtocolException e) { listener.status(e.getMessage()); break; }
                catch (IOException e) {
                    if (!running.get()) break;
                    if (surfaceReset.getAndSet(false)) { Thread.sleep(200); continue; }
                    if (System.nanoTime()-started>10_000_000_000L) attempt=0;
                    if (++attempt>5) { listener.status("Disconnected: "+e.getMessage()); break; }
                    listener.status("Reconnecting… ("+attempt+"/5)");
                    Thread.sleep(Math.min(4000,(1L<<Math.min(attempt-1,2))*500));
                }
            }
        } catch (GeneralSecurityException e) { listener.status("Cannot initialize encrypted connection"); }
        catch (InterruptedException e) { Thread.currentThread().interrupt(); }
        finally { close(); listener.ended(); }
    }

    private void streamOnce() throws IOException, GeneralSecurityException {
        listener.status("Connecting…");
        inputs.clear(); view.resetInput();
        Socket raw=new Socket(); socket=raw;
        Thread writer=null;
        H264Decoder decoder=null;
        AtomicBoolean session=new AtomicBoolean(true);
        try {
            if (!running.get()) return;
            raw.connect(new InetSocketAddress(spec.host,spec.port),5000);
            raw.setTcpNoDelay(true);
            SSLContext context=SSLContext.getInstance("TLS");
            context.init(null,new TrustManager[]{new CertificatePin(spec.fingerprint)},null);
            SSLSocket tls=(SSLSocket)context.getSocketFactory().createSocket(raw,spec.host,spec.port,true);
            socket=tls; tls.setSoTimeout(10000);
            // TLS 1.2 is mandatory; TLS 1.3 is negotiated by supported Android versions.
            tls.startHandshake();
            DataInputStream input=new DataInputStream(tls.getInputStream());
            DataOutputStream output=new DataOutputStream(tls.getOutputStream());
            try {
                JSONObject hello=new JSONObject().put("version",2).put("token",spec.token).put("maxWidth",1920)
                    .put("maxHeight",1920).put("fps",60).put("codec",forceJpeg ? "jpeg" : "h264");
                write(output,new Wire.Packet(Wire.HELLO,0,hello.toString().getBytes(StandardCharsets.UTF_8)));
                Wire.Packet packet=Wire.read(input);
                if (packet.type==Wire.ERROR) throw new ProtocolException(new JSONObject(new String(packet.payload,StandardCharsets.UTF_8)).optString("message","Connection rejected"));
                if (packet.type!=Wire.CONFIG || packet.sequence!=0) throw new ProtocolException("Expected display configuration");
                JSONObject config=new JSONObject(new String(packet.payload,StandardCharsets.UTF_8));
                int width=config.getInt("width"), height=config.getInt("height");
                String codec=config.getString("codec");
                if (config.getInt("version")!=2 || !("jpeg".equals(codec) || "h264".equals(codec)) || width<1 || height<1
                    || width>1920 || height>1920 || (long)width*height>1920L*1080 || config.getInt("fps")<1 || config.getInt("fps")>60)
                    throw new ProtocolException("Unsupported display configuration");
                if ("h264".equals(codec)) {
                    Wire.Packet csd=Wire.read(input);
                    if (csd.type!=Wire.CODEC_CONFIG || csd.sequence!=0) throw new ProtocolException("Missing codec configuration");
                    try {
                        decoder=new H264Decoder(view,width,height,config.getInt("fps"),csd.payload,
                            sequence -> write(output,new Wire.Packet(Wire.ACK,sequence,new byte[0])),
                            error -> {
                                if (!surfaceReset.get() && view.getHolder().getSurface().isValid()) forceJpeg=true;
                                else surfaceReset.set(true);
                                closeTransport();
                            });
                    } catch (IOException e) {
                        if (!surfaceReset.get() && view.getHolder().getSurface().isValid()) forceJpeg=true;
                        else surfaceReset.set(true);
                        throw e;
                    }
                    view.setSurfaceInvalidated(() -> { surfaceReset.set(true); closeTransport(); });
                }
                view.setVideoGeometry(width,height);
                connected=true;
                view.configure(config.getBoolean("input"),config.getBoolean("pen"),this);
                listener.status(config.optString("encoder",codec)+" · "+width+" × "+height
                    +(decoder!=null ? " · "+decoder.description() : "")+" · encrypted");
                writer=new Thread(() -> {
                    try {
                        while (running.get() && session.get()) write(output,new Wire.Packet(Wire.INPUT,0,inputs.take()));
                    } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
                    catch (IOException e) { try { tls.close(); } catch (IOException ignored) { } }
                },"Dis input");
                writer.start();
                int lastSequence=0;
                while (running.get()) {
                    packet=Wire.read(input);
                    if (packet.type==Wire.FRAME) {
                        if (packet.sequence!=lastSequence+1) throw new ProtocolException("Unexpected frame sequence");
                        lastSequence=packet.sequence;
                        ByteBuffer body=ByteBuffer.wrap(packet.payload).order(ByteOrder.BIG_ENDIAN);
                        if ((body.getShort() & 0xffff)!=width || (body.getShort() & 0xffff)!=height)
                            throw new ProtocolException("Frame dimensions changed without negotiation");
                        long pts=body.getLong(); // Host capture clock identifies frames; it is not compared with Android's clock.
                        byte[] jpeg=new byte[body.remaining()]; body.get(jpeg);
                        if (decoder!=null) decoder.queue(jpeg,pts,packet.sequence);
                        else {
                            view.render(jpeg,width,height);
                            write(output,new Wire.Packet(Wire.ACK,packet.sequence,new byte[0]));
                        }
                    } else if (packet.type==Wire.PONG) { /* Reserved for future round-trip measurements. */ }
                    else throw new ProtocolException("Unexpected server message");
                }
            } catch (JSONException e) { throw new ProtocolException("Invalid server configuration"); }
        } finally {
            connected=false; view.resetInput(); session.set(false);
            closeTransport();
            if (decoder!=null) decoder.close();
            view.setSurfaceInvalidated(null);
            try { if (socket!=null) socket.close(); } catch (IOException ignored) { }
            try { raw.close(); } catch (IOException ignored) { }
            if (writer!=null) {
                writer.interrupt();
                try { writer.join(2000); }
                catch (InterruptedException e) { Thread.currentThread().interrupt(); }
            }
            inputs.clear();
        }
    }

    private void write(DataOutputStream out, Wire.Packet packet) throws IOException { synchronized(writes) { Wire.write(out,packet); } }

    @Override public void send(byte[] pointer) {
        if (running.get() && connected && !inputs.offer(pointer)) {
            listener.status("Input connection stalled. Reconnect to continue."); close();
        }
    }

    @Override public void close() {
        running.set(false); connected=false; view.resetInput();
        try { if (socket!=null) socket.close(); } catch (IOException ignored) { }
        if (worker!=null) worker.interrupt();
    }

    private void closeTransport() { try { if (socket!=null) socket.close(); } catch (IOException ignored) { } }

    private static final class ProtocolException extends IOException {
        ProtocolException(String message) { super(message); }
    }

}
