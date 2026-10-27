package io.github.eoftisreal.dis;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.RectF;
import android.view.MotionEvent;
import android.view.SurfaceView;
import android.view.Surface;
import android.view.SurfaceHolder;
import android.view.Gravity;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import java.io.IOException;

public final class DisplayView extends SurfaceView {
    public interface InputSink { void send(byte[] pointer); }
    private final Paint paint=new Paint(Paint.FILTER_BITMAP_FLAG);
    private volatile RectF content=new RectF();
    private volatile boolean inputEnabled, penEnabled;
    private volatile InputSink sink;
    private boolean pressed;
    private int pointerId=-1, kind;
    private long lastMove;
    private boolean stylusInRange;
    private byte[] lastPointer;
    private final Object surfaceGate=new Object();
    private volatile Runnable surfaceInvalidated;
    private int videoWidth,videoHeight;

    public DisplayView(Context context) {
        super(context);
        setContentDescription(context.getString(R.string.display_description));
        setFocusable(true);
        getHolder().addCallback(new SurfaceHolder.Callback() {
            @Override public void surfaceCreated(SurfaceHolder holder) { synchronized(surfaceGate) { surfaceGate.notifyAll(); } }
            @Override public void surfaceChanged(SurfaceHolder holder,int format,int width,int height) { content=new RectF(0,0,getWidth(),getHeight()); }
            @Override public void surfaceDestroyed(SurfaceHolder holder) {
                Runnable callback=surfaceInvalidated; if (callback!=null) callback.run();
            }
        });
    }

    public void setSurfaceInvalidated(Runnable callback) { surfaceInvalidated=callback; }
    public Surface awaitSurface(long timeoutMs) throws IOException {
        long deadline=System.nanoTime()+timeoutMs*1_000_000L;
        synchronized(surfaceGate) {
            while (!getHolder().getSurface().isValid()) {
                long left=(deadline-System.nanoTime())/1_000_000L;
                if (left<=0) throw new IOException("Display surface is not ready");
                try { surfaceGate.wait(left); } catch (InterruptedException e) { Thread.currentThread().interrupt(); throw new IOException("Display closed",e); }
            }
            return getHolder().getSurface();
        }
    }
    public void setVideoGeometry(int width,int height) {
        post(() -> { videoWidth=width; videoHeight=height; updateVideoLayout(); });
    }
    public void updateVideoLayout() {
        if (!(getParent() instanceof FrameLayout) || videoWidth<=0 || videoHeight<=0) return;
        ViewGroup parent=(ViewGroup)getParent();
        if (parent.getWidth()<=0 || parent.getHeight()<=0) return;
        float scale=Math.min(parent.getWidth()/(float)videoWidth,parent.getHeight()/(float)videoHeight);
        int w=Math.max(1,Math.round(videoWidth*scale)), h=Math.max(1,Math.round(videoHeight*scale));
        if (getLayoutParams().width!=w || getLayoutParams().height!=h)
            setLayoutParams(new FrameLayout.LayoutParams(w,h,Gravity.CENTER));
        content=new RectF(0,0,w,h);
    }

    public void configure(boolean input, boolean pen, InputSink sender) {
        post(() -> { pressed=false; pointerId=-1; stylusInRange=false; lastPointer=null; inputEnabled=input; penEnabled=pen; sink=sender; });
    }

    public void resetInput() { post(() -> { inputEnabled=false; sink=null; pressed=false; pointerId=-1; stylusInRange=false; lastPointer=null; }); }

    /** Returns after the frame has been consumed, whether displayed or discarded during a surface transition. */
    public void render(byte[] jpeg, int width, int height) throws IOException {
        BitmapFactory.Options bounds=new BitmapFactory.Options(); bounds.inJustDecodeBounds=true;
        BitmapFactory.decodeByteArray(jpeg,0,jpeg.length,bounds);
        if (bounds.outWidth!=width || bounds.outHeight!=height) throw new IOException("Image dimensions do not match the negotiated display");
        BitmapFactory.Options options=new BitmapFactory.Options(); options.inPreferredConfig=Bitmap.Config.ARGB_8888;
        Bitmap image=BitmapFactory.decodeByteArray(jpeg,0,jpeg.length,options);
        if (image==null) throw new IOException("Invalid JPEG frame");
        try {
            if (!getHolder().getSurface().isValid()) return;
            Canvas canvas=null;
            try {
                canvas=getHolder().lockCanvas();
                if (canvas==null) return;
                float scale=Math.min(canvas.getWidth()/(float)width,canvas.getHeight()/(float)height);
                float w=width*scale, h=height*scale;
                RectF rect=new RectF((canvas.getWidth()-w)/2,(canvas.getHeight()-h)/2,(canvas.getWidth()+w)/2,(canvas.getHeight()+h)/2);
                canvas.drawColor(Color.BLACK); canvas.drawBitmap(image,null,rect,paint);
                content=new RectF(0,0,getWidth(),getHeight());
            } catch (IllegalArgumentException e) {
                // Surface can disappear between isValid and lockCanvas when the activity stops.
                if (getHolder().getSurface().isValid()) throw new IOException("Cannot render display",e);
            } finally {
                if (canvas!=null) {
                    try { getHolder().unlockCanvasAndPost(canvas); }
                    catch (IllegalArgumentException e) {
                        if (getHolder().getSurface().isValid()) throw new IOException("Display surface changed while presenting",e);
                    }
                }
            }
        } finally { image.recycle(); }
    }

    @Override public boolean onTouchEvent(MotionEvent event) {
        if (!inputEnabled || sink==null) return false;
        int action=event.getActionMasked();
        RectF rect=content;
        if (rect.width()<=0 || rect.height()<=0) return true;
        int changed=event.getActionIndex();
        boolean changedPen=penEnabled && isPen(event,changed);
        if (action==MotionEvent.ACTION_DOWN || (action==MotionEvent.ACTION_POINTER_DOWN && changedPen && (!pressed || kind!=1))) {
            if (!rect.contains(event.getX(changed),event.getY(changed))) return true;
            if (!changedPen && stylusInRange) return true;
            cancelPointer();
            pointerId=event.getPointerId(changed);
            kind=changedPen ? 1 : 0;
            pressed=true; lastMove=0;
            if (changedPen) requestUnbufferedDispatch(event);
            send(event,changed,0,rect,-1); return true;
        }
        if (action==MotionEvent.ACTION_CANCEL) { cancelPointer(); return true; }
        int index=event.findPointerIndex(pointerId);
        if (!pressed) return true;
        if (index<0) { cancelPointer(); return true; }
        if (action==MotionEvent.ACTION_MOVE) {
            long now=System.nanoTime();
            if (kind==1 || now-lastMove>=8_000_000L) {
                if (kind==1) for (int h=Math.max(0,event.getHistorySize()-8);h<event.getHistorySize();h++) send(event,index,1,rect,h);
                send(event,index,1,rect,-1); lastMove=now;
            }
        } else if (action==MotionEvent.ACTION_UP
            || (action==MotionEvent.ACTION_POINTER_UP && event.getPointerId(event.getActionIndex())==pointerId)) {
            send(event,index,2,rect,-1);
            pressed=false; pointerId=-1;
            if (action==MotionEvent.ACTION_UP) performClick();
        }
        return true;
    }

    @Override public boolean onHoverEvent(MotionEvent event) {
        if (!inputEnabled || !penEnabled || sink==null || !isPen(event,event.getActionIndex())) return super.onHoverEvent(event);
        int action=event.getActionMasked();
        if (action==MotionEvent.ACTION_HOVER_EXIT) {
            if (stylusInRange && !pressed) cancelPointer();
            stylusInRange=false;
            return true;
        }
        if (action!=MotionEvent.ACTION_HOVER_ENTER && action!=MotionEvent.ACTION_HOVER_MOVE) return super.onHoverEvent(event);
        if (content.width()<=0 || content.height()<=0) return true;
        if (pressed && kind==1) return true;
        if (pressed) cancelPointer();
        stylusInRange=true; kind=1;
        send(event,event.getActionIndex(),4,content,-1);
        return true;
    }

    private static boolean isPen(MotionEvent event,int index) {
        int tool=event.getToolType(index);
        return tool==MotionEvent.TOOL_TYPE_STYLUS || tool==MotionEvent.TOOL_TYPE_ERASER;
    }

    private void cancelPointer() {
        InputSink output=sink;
        if (output!=null && lastPointer!=null && (pressed || stylusInRange)) {
            byte[] cancel=lastPointer.clone(); cancel[1]=3;
            java.nio.ByteBuffer.wrap(cancel).putFloat(12,0);
            output.send(cancel);
        }
        pressed=false; pointerId=-1; lastPointer=null;
    }

    private void send(MotionEvent event, int index, int action, RectF rect,int history) {
        InputSink output=sink;
        if (output==null) return;
        float px=history<0 ? event.getX(index) : event.getHistoricalX(index,history);
        float py=history<0 ? event.getY(index) : event.getHistoricalY(index,history);
        float pressure=history<0 ? event.getPressure(index) : event.getHistoricalPressure(index,history);
        float tilt=(float)Math.toDegrees(history<0 ? event.getAxisValue(MotionEvent.AXIS_TILT,index)
            : event.getHistoricalAxisValue(MotionEvent.AXIS_TILT,index,history));
        float orientation=history<0 ? event.getOrientation(index) : event.getHistoricalOrientation(index,history);
        float x=clamp((px-rect.left)/rect.width(),0,1);
        float y=clamp((py-rect.top)/rect.height(),0,1);
        int buttons=(event.getButtonState() & MotionEvent.BUTTON_STYLUS_PRIMARY)!=0 ? 1 : 0;
        if (event.getToolType(index)==MotionEvent.TOOL_TYPE_ERASER) buttons|=2;
        lastPointer=Wire.pointer(kind,action,buttons,x,y,action==0 || action==1 ? clamp(pressure,0,1) : 0,
            clamp((float)Math.sin(orientation)*tilt,-90,90),clamp(-(float)Math.cos(orientation)*tilt,-90,90));
        output.send(lastPointer);
    }

    private static float clamp(float n, float min, float max) { return Float.isNaN(n) ? min : Math.max(min,Math.min(max,n)); }
    @Override public boolean performClick() { super.performClick(); return true; }
}
