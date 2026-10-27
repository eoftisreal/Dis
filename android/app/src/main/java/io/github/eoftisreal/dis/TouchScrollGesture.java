package io.github.eoftisreal.dis;

/** Two-finger wheel gestures with slop, direction locking, and bounded wire deltas. */
public final class TouchScrollGesture {
    public interface Sink { void scroll(float horizontal,float vertical); }
    private final float slop;
    private final Sink sink;
    private boolean active,moved,horizontal;
    private float startX,startY,lastX,lastY,remainderX,remainderY;
    private long started;
    public TouchScrollGesture(float slop,Sink sink){this.slop=slop;this.sink=sink;}
    public void begin(float x,float y,long time){cancel();active=true;startX=lastX=x;startY=lastY=y;started=time;}
    public boolean active(){return active;}
    public void move(float x,float y){
        if(!active||!Float.isFinite(x)||!Float.isFinite(y))return;
        if(!moved){
            if(Math.hypot(x-startX,y-startY)<slop)return;
            horizontal=Math.abs(x-startX)>Math.abs(y-startY)*1.5f;moved=true;
        }
        // Natural direction: finger up scrolls content down; finger left pans right.
        if(horizontal)remainderX-=(x-lastX)*3;else remainderY+=(y-lastY)*3;
        lastX=x;lastY=y;
        for(int i=0;i<8&&(Math.abs(remainderX)>=1||Math.abs(remainderY)>=1);i++){
            float dx=Math.max(-90,Math.min(90,remainderX)),dy=Math.max(-90,Math.min(90,remainderY));
            remainderX-=dx;remainderY-=dy;sink.scroll(dx,dy);
        }
    }
    public boolean finish(long time){boolean tap=active&&!moved&&time>=started&&time-started<=350;cancel();return tap;}
    public void cancel(){active=false;moved=false;remainderX=remainderY=0;}
}
