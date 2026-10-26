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

    public DisplayView(Context context) {
        super(context);
        setContentDescription(context.getString(R.string.display_description));
        setFocusable(true);
    }

    public void configure(boolean input, boolean pen, InputSink sender) {
        post(() -> { pressed=false; pointerId=-1; inputEnabled=input; penEnabled=pen; sink=sender; });
    }

    public void resetInput() { post(() -> { inputEnabled=false; sink=null; pressed=false; pointerId=-1; }); }

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
                canvas.drawColor(Color.BLACK); canvas.drawBitmap(image,null,rect,paint); content=rect;
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
        if (action==MotionEvent.ACTION_DOWN) {
            if (!rect.contains(event.getX(),event.getY())) return true;
            pointerId=event.getPointerId(0);
            int tool=event.getToolType(0);
            kind=(tool==MotionEvent.TOOL_TYPE_STYLUS || tool==MotionEvent.TOOL_TYPE_ERASER) && penEnabled ? 1 : 0;
            pressed=true; send(event,0,0,rect); return true;
        }
        int index=event.findPointerIndex(pointerId);
        if (!pressed || index<0) return true;
        if (action==MotionEvent.ACTION_MOVE) {
            long now=System.nanoTime();
            if (now-lastMove>=8_000_000L) { send(event,index,1,rect); lastMove=now; }
        } else if (action==MotionEvent.ACTION_UP || action==MotionEvent.ACTION_CANCEL
            || (action==MotionEvent.ACTION_POINTER_UP && event.getPointerId(event.getActionIndex())==pointerId)) {
            send(event,index,action==MotionEvent.ACTION_CANCEL ? 3 : 2,rect);
            pressed=false; pointerId=-1;
            if (action==MotionEvent.ACTION_UP) performClick();
        }
        return true;
    }

    private void send(MotionEvent event, int index, int action, RectF rect) {
        InputSink output=sink;
        if (output==null) return;
        float x=clamp((event.getX(index)-rect.left)/rect.width(),0,1);
        float y=clamp((event.getY(index)-rect.top)/rect.height(),0,1);
        float tilt=(float)Math.toDegrees(event.getAxisValue(MotionEvent.AXIS_TILT,index));
        float orientation=event.getOrientation(index);
        int buttons=(event.getButtonState() & MotionEvent.BUTTON_STYLUS_PRIMARY)!=0 ? 1 : 0;
        if (event.getToolType(index)==MotionEvent.TOOL_TYPE_ERASER) buttons|=2;
        output.send(Wire.pointer(kind,action,buttons,x,y,clamp(event.getPressure(index),0,1),
            clamp((float)Math.sin(orientation)*tilt,-90,90),clamp(-(float)Math.cos(orientation)*tilt,-90,90)));
    }

    private static float clamp(float n, float min, float max) { return Float.isNaN(n) ? min : Math.max(min,Math.min(max,n)); }
    @Override public boolean performClick() { super.performClick(); return true; }
}
