package io.github.eoftisreal.dis;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.view.View;

/** Only real one-second samples are drawn; an unconnected graph stays empty. */
public final class DiagnosticsGraph extends View {
    private final float[] samples=new float[60];
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Path line=new Path();
    private int count;
    public DiagnosticsGraph(Context context){super(context);setContentDescription("Received frame rate over the last minute");}
    public void clear(){count=0;invalidate();}
    public void add(float fps){if(count==samples.length){System.arraycopy(samples,1,samples,0,samples.length-1);count--;}samples[count++]=fps;invalidate();}
    @Override protected void onDraw(Canvas canvas){
        super.onDraw(canvas);float density=getResources().getDisplayMetrics().density;
        paint.setColor(0xff2b384b);paint.setStrokeWidth(density);paint.setStyle(Paint.Style.STROKE);
        for(int i=1;i<4;i++)canvas.drawLine(0,getHeight()*i/4f,getWidth(),getHeight()*i/4f,paint);
        if(count<2)return;
        line.reset();for(int i=0;i<count;i++){float x=getWidth()*i/59f,y=getHeight()-Math.min(60,Math.max(0,samples[i]))/60f*(getHeight()-4*density)-2*density;if(i==0)line.moveTo(x,y);else line.lineTo(x,y);}
        paint.setColor(0xff64dfc1);paint.setStrokeWidth(2*density);canvas.drawPath(line,paint);
    }
}
