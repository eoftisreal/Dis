package io.github.eoftisreal.dis;

import android.app.Activity;
import android.os.Looper;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.widget.FrameLayout;
import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.List;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.android.controller.ActivityController;
import org.robolectric.annotation.Config;
import org.robolectric.annotation.LooperMode;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class)
@Config(sdk=28)
@LooperMode(LooperMode.Mode.PAUSED)
public final class DisplayViewTest {
    private ActivityController<Activity> controller;
    private DisplayView view;
    private final List<byte[]> packets=new ArrayList<>();

    @Before public void attachView() {
        controller=Robolectric.buildActivity(Activity.class).setup();
        FrameLayout frame=new FrameLayout(controller.get());
        view=new DisplayView(controller.get());
        frame.addView(view,new FrameLayout.LayoutParams(400,200));
        controller.get().setContentView(frame);
        view.setVideoGeometry(400,200);
        view.configure(true,true,packets::add);
        shadowOf(Looper.getMainLooper()).idle();
        frame.measure(android.view.View.MeasureSpec.makeMeasureSpec(400,android.view.View.MeasureSpec.EXACTLY),
            android.view.View.MeasureSpec.makeMeasureSpec(200,android.view.View.MeasureSpec.EXACTLY));
        frame.layout(0,0,400,200);
        view.updateVideoLayout();
        view.layout(0,0,400,200);
        assertEquals(400,view.getWidth());
        assertEquals(200,view.getHeight());
    }
    @After public void detachView() { controller.pause().stop().destroy(); }

    @Test public void hoverHasNoContactAndLeavesRange() {
        hover(MotionEvent.ACTION_HOVER_ENTER,MotionEvent.TOOL_TYPE_STYLUS);
        hover(MotionEvent.ACTION_HOVER_MOVE,MotionEvent.TOOL_TYPE_STYLUS);
        hover(MotionEvent.ACTION_HOVER_EXIT,MotionEvent.TOOL_TYPE_STYLUS);
        assertEquals(3,packets.size());
        assertPacket(0,1,4); assertPacket(1,1,4); assertPacket(2,1,3);
        assertEquals(0,number(packets.get(0),12),0);
        assertEquals(.25f,number(packets.get(0),4),.001f);
    }

    @Test public void hoveringPenSuppressesPalmUntilItLeaves() {
        hover(MotionEvent.ACTION_HOVER_ENTER,MotionEvent.TOOL_TYPE_STYLUS);
        touch(MotionEvent.ACTION_DOWN,MotionEvent.TOOL_TYPE_FINGER,1);
        touch(MotionEvent.ACTION_UP,MotionEvent.TOOL_TYPE_FINGER,1);
        assertEquals(1,packets.size());
        hover(MotionEvent.ACTION_HOVER_EXIT,MotionEvent.TOOL_TYPE_STYLUS);
        touch(MotionEvent.ACTION_DOWN,MotionEvent.TOOL_TYPE_FINGER,1);
        touch(MotionEvent.ACTION_UP,MotionEvent.TOOL_TYPE_FINGER,1);
        assertPacket(2,0,0); assertPacket(3,0,2);
    }

    @Test public void stylusTakesOverAndReleasesAnExistingFingerDrag() {
        touch(MotionEvent.ACTION_DOWN,MotionEvent.TOOL_TYPE_FINGER,0);
        MotionEvent event=event(MotionEvent.ACTION_POINTER_DOWN | (1<<MotionEvent.ACTION_POINTER_INDEX_SHIFT),
            new int[]{0,7},new int[]{MotionEvent.TOOL_TYPE_FINGER,MotionEvent.TOOL_TYPE_STYLUS},
            new MotionEvent.PointerCoords[]{coords(40,40,.4f),coords(100,50,.8f)},0);
        view.onTouchEvent(event); event.recycle();
        assertEquals(3,packets.size());
        assertPacket(0,0,0); assertPacket(1,0,3); assertPacket(2,1,0);
        assertEquals(.8f,number(packets.get(2),12),.001f);
        assertEquals(.25f,number(packets.get(2),4),.001f);
    }

    @Test public void penCanTouchDownWhileASuppressedPalmRemainsOnScreen() {
        hover(MotionEvent.ACTION_HOVER_ENTER,MotionEvent.TOOL_TYPE_STYLUS);
        touch(MotionEvent.ACTION_DOWN,MotionEvent.TOOL_TYPE_FINGER,0);
        MotionEvent event=event(MotionEvent.ACTION_POINTER_DOWN | (1<<MotionEvent.ACTION_POINTER_INDEX_SHIFT),
            new int[]{0,7},new int[]{MotionEvent.TOOL_TYPE_FINGER,MotionEvent.TOOL_TYPE_STYLUS},
            new MotionEvent.PointerCoords[]{coords(40,40,.4f),coords(100,50,.8f)},0);
        view.onTouchEvent(event); event.recycle();
        assertEquals(3,packets.size());
        assertPacket(0,1,4); assertPacket(1,1,3); assertPacket(2,1,0);
    }

    @Test public void cancelDoesNotDependOnAFingerStillBeingInTheEvent() {
        touch(MotionEvent.ACTION_DOWN,MotionEvent.TOOL_TYPE_STYLUS,7);
        touch(MotionEvent.ACTION_CANCEL,MotionEvent.TOOL_TYPE_STYLUS,9);
        touch(MotionEvent.ACTION_MOVE,MotionEvent.TOOL_TYPE_STYLUS,7);
        assertEquals(2,packets.size()); assertPacket(1,1,3);
        assertEquals(0,number(packets.get(1),12),0);
    }

    @Test public void eraserAndBarrelButtonSurviveHover() {
        MotionEvent event=event(MotionEvent.ACTION_HOVER_ENTER,new int[]{0},new int[]{MotionEvent.TOOL_TYPE_ERASER},
            new MotionEvent.PointerCoords[]{coords(100,50,.5f)},MotionEvent.BUTTON_STYLUS_PRIMARY);
        view.onHoverEvent(event); event.recycle();
        assertPacket(0,1,4); assertEquals(3,packets.get(0)[3]);
    }

    @Test public void historicalPenPressureAndCoordinatesAreRetained() {
        touch(MotionEvent.ACTION_DOWN,MotionEvent.TOOL_TYPE_STYLUS,0);
        MotionEvent move=event(MotionEvent.ACTION_MOVE,new int[]{0},new int[]{MotionEvent.TOOL_TYPE_STYLUS},
            new MotionEvent.PointerCoords[]{coords(100,50,.3f)},0);
        move.addBatch(20,new MotionEvent.PointerCoords[]{coords(200,100,.8f)},0);
        view.onTouchEvent(move); move.recycle();
        assertEquals(3,packets.size()); assertPacket(1,1,1); assertPacket(2,1,1);
        assertEquals(.3f,number(packets.get(1),12),.001f);
        assertEquals(.8f,number(packets.get(2),12),.001f);
        assertEquals(.5f,number(packets.get(2),4),.001f);
    }

    @Test public void inputDisableAndPenDisableAreRespected() {
        view.configure(true,false,packets::add); shadowOf(Looper.getMainLooper()).idle();
        touch(MotionEvent.ACTION_DOWN,MotionEvent.TOOL_TYPE_STYLUS,0);
        touch(MotionEvent.ACTION_UP,MotionEvent.TOOL_TYPE_STYLUS,0);
        assertPacket(0,0,0); assertPacket(1,0,2);
        view.resetInput(); shadowOf(Looper.getMainLooper()).idle();
        touch(MotionEvent.ACTION_DOWN,MotionEvent.TOOL_TYPE_FINGER,0);
        assertEquals(2,packets.size());
    }

    private void touch(int action,int tool,int id) {
        MotionEvent event=event(action,new int[]{id},new int[]{tool},new MotionEvent.PointerCoords[]{coords(100,50,.5f)},0);
        view.onTouchEvent(event); event.recycle();
    }
    private void hover(int action,int tool) {
        MotionEvent event=event(action,new int[]{0},new int[]{tool},new MotionEvent.PointerCoords[]{coords(100,50,.5f)},0);
        view.onHoverEvent(event); event.recycle();
    }
    private static MotionEvent.PointerCoords coords(float x,float y,float pressure) {
        MotionEvent.PointerCoords c=new MotionEvent.PointerCoords(); c.x=x; c.y=y; c.pressure=pressure; c.size=.1f;
        return c;
    }
    private static MotionEvent event(int action,int[] ids,int[] tools,MotionEvent.PointerCoords[] coords,int buttons) {
        MotionEvent.PointerProperties[] properties=new MotionEvent.PointerProperties[ids.length];
        for (int n=0;n<ids.length;n++) { properties[n]=new MotionEvent.PointerProperties(); properties[n].id=ids[n]; properties[n].toolType=tools[n]; }
        return MotionEvent.obtain(0,10,action,ids.length,properties,coords,0,buttons,1,1,0,0,InputDevice.SOURCE_STYLUS,0);
    }
    private void assertPacket(int index,int kind,int action) {
        assertEquals(kind,packets.get(index)[0]); assertEquals(action,packets.get(index)[1]);
    }
    private static float number(byte[] packet,int offset) { return ByteBuffer.wrap(packet).getFloat(offset); }
}
