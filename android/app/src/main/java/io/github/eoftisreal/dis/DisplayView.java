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
import android.view.ViewConfiguration;
import android.widget.FrameLayout;
import java.io.IOException;

public final class DisplayView extends SurfaceView {
    public interface InputSink { void send(byte[] pointer); }
    private final Paint paint=new Paint(Paint.FILTER_BITMAP_FLAG);
    private volatile RectF content=new RectF();
    private volatile boolean inputEnabled, penEnabled, touchEnabled, gesturesEnabled;
    private volatile InputSink sink;
    private boolean pressed;
    private boolean stylusInRange;
    private byte[] lastPointer;
    private boolean touchLocked;
    private int pointerId=-1, kind;
    private long lastMove;
    private final Object surfaceGate=new Object();
    private volatile Runnable surfaceInvalidated;
    private int videoWidth,videoHeight;
    private final TouchScrollGesture scrollGesture;
    private final float density;
    private boolean multiGesture;
    private int scrollFirst=-1,scrollSecond=-1;
    private float scrollX,scrollY;

    public DisplayView(Context context) {
        super(context);
        density=getResources().getDisplayMetrics().density;
        scrollGesture=new TouchScrollGesture(ViewConfiguration.get(context).getScaledTouchSlop()/density,
            (dx,dy)->{InputSink output=sink;if(output!=null&&!touchLocked)output.send(Wire.pointer(0,5,0,scrollX,scrollY,0,dx,dy));});
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

    public void configure(boolean input, boolean pen, boolean touch, boolean gestures, InputSink sender) {
        post(() -> { cancelGesture(); inputEnabled=input; penEnabled=pen;touchEnabled=touch;gesturesEnabled=gestures;sink=sender; });
    }

    public void resetInput() { post(() -> { cancelGesture();inputEnabled=false;sink=null; }); }

    public boolean isTouchLocked() { return touchLocked; }
    public void setTouchLocked(boolean locked) {
        // Release a mouse/pen held by another finger before locking the display.
        cancelGesture();touchLocked=locked;
    }
    private void cancelPointer(){
        if((pressed||stylusInRange)&&sink!=null&&lastPointer!=null){
            byte[] cancel=lastPointer.clone();cancel[1]=3;
            java.nio.ByteBuffer.wrap(cancel).putFloat(12,0);sink.send(cancel);
        }
        pressed=false;stylusInRange=false;pointerId=-1;lastPointer=null;
    }
    private void cancelGesture(){cancelPointer();scrollGesture.cancel();multiGesture=false;scrollFirst=scrollSecond=-1;}
    private void updateScrollPosition(MotionEvent event,RectF rect){
        int a=event.findPointerIndex(scrollFirst),b=event.findPointerIndex(scrollSecond);
        if(a<0||b<0)return;
        float x=(event.getX(a)+event.getX(b))*.5f,y=(event.getY(a)+event.getY(b))*.5f;
        scrollX=clamp((x-rect.left)/rect.width(),0,1);scrollY=clamp((y-rect.top)/rect.height(),0,1);
        scrollGesture.move(x/density,y/density);
    }

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

    private static boolean isPen(MotionEvent event,int index){
        int tool=event.getToolType(index);
        return tool==MotionEvent.TOOL_TYPE_STYLUS||tool==MotionEvent.TOOL_TYPE_ERASER;
    }

    @Override public boolean onHoverEvent(MotionEvent event){
        if(touchLocked)return true;
        if(!inputEnabled||!penEnabled||sink==null||!isPen(event,0))return super.onHoverEvent(event);
        RectF rect=content;
        if(rect.width()<=0||rect.height()<=0)return true;
        int action=event.getActionMasked();
        if(action==MotionEvent.ACTION_HOVER_ENTER||action==MotionEvent.ACTION_HOVER_MOVE){
            if(pressed&&kind==1)return true;
            if(pressed||multiGesture)cancelGesture();
            stylusInRange=true;kind=1;send(event,0,4,rect);
        }else if(action==MotionEvent.ACTION_HOVER_EXIT){
            if(stylusInRange&&!pressed)cancelPointer();
        }
        return true;
    }

    @Override public boolean onTouchEvent(MotionEvent event) {
        if (touchLocked) return true;
        if (!inputEnabled || sink==null) return false;
        int action=event.getActionMasked();
        RectF rect=content;
        if (rect.width()<=0 || rect.height()<=0) return true;
        boolean penDown=penEnabled&&isPen(event,event.getActionIndex())
            &&(action==MotionEvent.ACTION_DOWN||action==MotionEvent.ACTION_POINTER_DOWN);
        // Suppress palms while a pen is recognized, but allow that pen to take ownership.
        if((stylusInRange||(pressed&&kind==1))&&!penDown){
            if(action==MotionEvent.ACTION_DOWN||(!pressed&&action!=MotionEvent.ACTION_CANCEL))return true;
        }
        if (action==MotionEvent.ACTION_DOWN) {
            cancelGesture();lastMove=0;
            if (!rect.contains(event.getX(),event.getY())) return true;
            pointerId=event.getPointerId(0);
            int tool=event.getToolType(0);
            kind=(tool==MotionEvent.TOOL_TYPE_STYLUS || tool==MotionEvent.TOOL_TYPE_ERASER) && penEnabled ? 1 : touchEnabled&&tool==MotionEvent.TOOL_TYPE_FINGER?2:0;
            pressed=true;stylusInRange=kind==1;
            if(kind==1)requestUnbufferedDispatch(event);
            send(event,0,0,rect); return true;
        }
        if(action==MotionEvent.ACTION_POINTER_DOWN&&penEnabled){
            int added=event.getActionIndex(),tool=event.getToolType(added);
            if(tool==MotionEvent.TOOL_TYPE_STYLUS||tool==MotionEvent.TOOL_TYPE_ERASER){
                cancelGesture();pointerId=event.getPointerId(added);kind=1;pressed=true;stylusInRange=true;
                requestUnbufferedDispatch(event);send(event,added,0,rect);return true;
            }
        }
        if(action==MotionEvent.ACTION_POINTER_DOWN&&gesturesEnabled&&kind!=1&&!multiGesture&&event.getPointerCount()>=2
            &&event.getToolType(0)==MotionEvent.TOOL_TYPE_FINGER&&event.getToolType(1)==MotionEvent.TOOL_TYPE_FINGER){
            cancelPointer();multiGesture=true;scrollFirst=event.getPointerId(0);scrollSecond=event.getPointerId(1);
            float x=(event.getX(0)+event.getX(1))*.5f,y=(event.getY(0)+event.getY(1))*.5f;
            scrollX=clamp((x-rect.left)/rect.width(),0,1);scrollY=clamp((y-rect.top)/rect.height(),0,1);
            scrollGesture.begin(x/density,y/density,event.getEventTime());return true;
        }
        if(multiGesture){
            if(action==MotionEvent.ACTION_MOVE)updateScrollPosition(event,rect);
            if(action==MotionEvent.ACTION_POINTER_UP){
                int lifted=event.getPointerId(event.getActionIndex());
                if(lifted==scrollFirst||lifted==scrollSecond){
                    updateScrollPosition(event,rect);
                    if(scrollGesture.finish(event.getEventTime())&&sink!=null)sink.send(Wire.pointer(0,6,0,scrollX,scrollY,0,0,0));
                    scrollFirst=scrollSecond=-1;
                }
            }
            if(action==MotionEvent.ACTION_UP||action==MotionEvent.ACTION_CANCEL)cancelGesture();
            return true;
        }
        if(action==MotionEvent.ACTION_CANCEL){cancelGesture();return true;}
        int index=event.findPointerIndex(pointerId);
        if (!pressed) return true;
        if(index<0){cancelGesture();return true;}
        if (action==MotionEvent.ACTION_MOVE) {
            long now=System.nanoTime();
            if(kind==1){
                for(int h=Math.max(0,event.getHistorySize()-8);h<event.getHistorySize();h++)send(event,index,1,rect,h);
                send(event,index,1,rect);
            }else if (now-lastMove>=8_000_000L) { send(event,index,1,rect); lastMove=now; }
        } else if (action==MotionEvent.ACTION_UP || action==MotionEvent.ACTION_CANCEL
            || (action==MotionEvent.ACTION_POINTER_UP && event.getPointerId(event.getActionIndex())==pointerId)) {
            send(event,index,action==MotionEvent.ACTION_CANCEL ? 3 : 2,rect);
            pressed=false; pointerId=-1;
            if (action==MotionEvent.ACTION_UP) performClick();
        }
        return true;
    }

    private void send(MotionEvent event, int index, int action, RectF rect) {
        send(event,index,action,rect,-1);
    }
    private void send(MotionEvent event, int index, int action, RectF rect,int history) {
        InputSink output=sink;
        if (output==null) return;
        float x=clamp(((history<0?event.getX(index):event.getHistoricalX(index,history))-rect.left)/rect.width(),0,1);
        float y=clamp(((history<0?event.getY(index):event.getHistoricalY(index,history))-rect.top)/rect.height(),0,1);
        float tilt=(float)Math.toDegrees(history<0?event.getAxisValue(MotionEvent.AXIS_TILT,index):event.getHistoricalAxisValue(MotionEvent.AXIS_TILT,index,history));
        float orientation=history<0?event.getOrientation(index):event.getHistoricalOrientation(index,history);
        int buttons=kind==1&&(event.getButtonState() & MotionEvent.BUTTON_STYLUS_PRIMARY)!=0 ? 1 : 0;
        if (kind==1&&event.getToolType(index)==MotionEvent.TOOL_TYPE_ERASER) buttons|=2;
        float pressure=action==0||action==1?clamp(history<0?event.getPressure(index):event.getHistoricalPressure(index,history),0,1):0;
        lastPointer=Wire.pointer(kind,action,buttons,x,y,pressure,
            clamp((float)Math.sin(orientation)*tilt,-90,90),clamp(-(float)Math.cos(orientation)*tilt,-90,90));
        output.send(lastPointer);
    }

    private static float clamp(float n, float min, float max) { return Float.isNaN(n) ? min : Math.max(min,Math.min(max,n)); }
    @Override public boolean performClick() { super.performClick(); return true; }
}
