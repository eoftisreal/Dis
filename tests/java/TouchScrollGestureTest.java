package io.github.eoftisreal.dis;

import java.util.ArrayList;

public final class TouchScrollGestureTest {
    private static int checks;
    public static void main(String[] args){
        ArrayList<float[]> events=new ArrayList<>();
        TouchScrollGesture gesture=new TouchScrollGesture(8,(x,y)->events.add(new float[]{x,y}));
        gesture.begin(100,100,0);gesture.move(102,103);
        require(events.isEmpty());require(gesture.finish(200));require(!gesture.finish(201));
        gesture.begin(100,100,1000);gesture.move(101,60);
        require(events.size()==2);require(sum(events,1)==-120);require(sum(events,0)==0);
        require(!gesture.finish(1100));
        for(float[] event:events)require(Math.abs(event[0])<=90&&Math.abs(event[1])<=90);
        events.clear();gesture.begin(100,100,2000);gesture.move(60,101);
        require(sum(events,0)==120&&sum(events,1)==0);
        gesture.move(50,120);require(sum(events,1)==0);require(!gesture.finish(2100));
        events.clear();gesture.begin(0,0,3000);gesture.cancel();gesture.move(100,100);
        require(events.isEmpty()&&!gesture.finish(3100));
        gesture.begin(0,0,4000);require(!gesture.finish(4400));
        gesture.begin(0,0,5000);gesture.move(Float.NaN,10);require(events.isEmpty());
        gesture.move(0,-10000);require(events.size()==8);
        for(float[] event:events)require(event[0]==0&&event[1]==-90);
        gesture.cancel();events.clear();gesture.begin(0,0,6000);gesture.move(0,8);
        require(events.size()==1&&sum(events,1)==24);require(!gesture.finish(6100));
        System.out.println(checks+" touch scrolling and right-click checks passed.");
    }
    private static float sum(ArrayList<float[]> events,int index){float result=0;for(float[] event:events)result+=event[index];return result;}
    private static void require(boolean value){if(!value)throw new AssertionError();checks++;}
}
