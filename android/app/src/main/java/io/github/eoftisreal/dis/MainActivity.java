package io.github.eoftisreal.dis;

import android.app.Activity;
import android.app.Dialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Bundle;
import android.text.Editable;
import android.text.TextWatcher;
import android.text.InputType;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageButton;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;
import java.util.Locale;

public final class MainActivity extends Activity {
    private static final int BG=0xff10151e,CARD=0xff192230,INSET=0xff101924,TEXT=0xffedf3fa,MUTED=0xff95a4b8,ACCENT=0xff64dfc1;
    private DisplayClient client;
    private DisplayView display;
    private FrameLayout root;
    private LinearLayout empty,networkSection,reveal;
    private Dialog controls;
    private ScrollView dashboardScroll;
    private TextView phaseLabel,message,standbyMessage,overlay,streamFps,decoderFps,ping,bandwidth,videoDetails,sessionDetails;
    private Button primary,stop;
    private ImageButton touchLock;
    private EditText link;
    private ProgressBar progress;
    private DiagnosticsGraph graph;
    private boolean foreground,disconnecting,streaming,showOverlay,manualStop;
    private int generation;
    private String currentLink="",pendingLink,lastMessage="Ready to connect from your PC.",target="Not paired",encoder="—",decoder="—",codec="—";
    private int videoWidth,videoHeight,targetFps;
    private DisplayClient.Phase phase;
    private StreamStats.Sample latest;
    private boolean revealSessionActive,revealStreaming;
    private final Runnable hideReveal=()->{if(reveal!=null)reveal.setVisibility(View.GONE);};

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        root=new FrameLayout(this);root.setBackgroundColor(BG);
        FrameLayout video=new FrameLayout(this);video.setBackgroundColor(Color.BLACK);
        display=new DisplayView(this);video.addView(display,new FrameLayout.LayoutParams(-1,-1));
        video.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob)->display.updateVideoLayout());
        root.addView(video,new FrameLayout.LayoutParams(-1,-1));
        empty=column();empty.setGravity(Gravity.CENTER);empty.setPadding(dp(32),dp(24),dp(32),dp(24));empty.setBackgroundColor(BG);
        TextView brand=text("Dis",44,ACCENT);brand.setTypeface(null,Typeface.BOLD);empty.addView(brand);
        TextView heading=text(getString(R.string.android_home_title),22,TEXT);heading.setGravity(Gravity.CENTER);add(empty,heading,18);
        standbyMessage=text(getString(R.string.android_home_help),13,MUTED);standbyMessage.setGravity(Gravity.CENTER);standbyMessage.setLineSpacing(dp(5),1);add(empty,standbyMessage,14);
        progress=new ProgressBar(this);progress.setIndeterminateTintList(android.content.res.ColorStateList.valueOf(ACCENT));
        LinearLayout.LayoutParams progressLayout=new LinearLayout.LayoutParams(dp(28),dp(28));progressLayout.topMargin=dp(18);empty.addView(progress,progressLayout);
        Button open=button(getString(R.string.connection_controls),true);open.setId(R.id.open_dashboard);open.setOnClickListener(v->showControls());
        LinearLayout.LayoutParams openLayout=new LinearLayout.LayoutParams(dp(230),dp(48));openLayout.topMargin=dp(24);empty.addView(open,openLayout);
        root.addView(empty,new FrameLayout.LayoutParams(-1,-1));
        overlay=text("",11,TEXT);overlay.setPadding(dp(10),dp(7),dp(10),dp(7));overlay.setBackground(shape(0xcc10151e,8));overlay.setContentDescription(getString(R.string.live_diagnostics));
        FrameLayout.LayoutParams overlayLayout=new FrameLayout.LayoutParams(-2,-2,Gravity.TOP | Gravity.START);overlayLayout.setMargins(dp(12),dp(12),dp(12),0);root.addView(overlay,overlayLayout);
        reveal=row();reveal.setAlpha(.9f);
        ImageButton controlsIcon=iconButton(R.drawable.ic_controls,R.string.controls);controlsIcon.setId(R.id.open_controls);controlsIcon.setOnClickListener(v->showControls());
        reveal.addView(controlsIcon,new LinearLayout.LayoutParams(dp(48),dp(48)));
        touchLock=iconButton(R.drawable.ic_touch_unlocked,R.string.lock_touch);touchLock.setId(R.id.touch_lock);
        touchLock.setOnClickListener(v->{display.setTouchLocked(!display.isTouchLocked());updateTouchLock();showRevealControls();Toast.makeText(this,display.isTouchLocked()?R.string.touch_locked:R.string.touch_unlocked,Toast.LENGTH_SHORT).show();});
        LinearLayout.LayoutParams lockLayout=new LinearLayout.LayoutParams(dp(48),dp(48));lockLayout.leftMargin=dp(8);reveal.addView(touchLock,lockLayout);
        FrameLayout.LayoutParams revealLayout=new FrameLayout.LayoutParams(-2,dp(48),Gravity.TOP | Gravity.END);revealLayout.setMargins(dp(12),dp(10),dp(12),0);root.addView(reveal,revealLayout);
        setContentView(root);buildControls();
        if(Build.VERSION.SDK_INT>=33)getOnBackInvokedDispatcher().registerOnBackInvokedCallback(android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT,this::handleBack);
        showOverlay=getPreferences(MODE_PRIVATE).getBoolean("diagnostics_overlay",false);
        applyOrientation(getPreferences(MODE_PRIVATE).getInt("orientation",0));render();
    }

    private void buildControls() {
        controls=new Dialog(this,android.R.style.Theme_Material_Dialog_NoActionBar);
        LinearLayout sheet=column();sheet.setPadding(dp(20),dp(16),dp(20),dp(18));sheet.setBackground(shape(CARD,20));sheet.setFocusableInTouchMode(true);sheet.requestFocus();
        ScrollView scroll=new ScrollView(this);dashboardScroll=scroll;scroll.setFillViewport(false);scroll.setClipToPadding(false);scroll.addView(sheet);
        controls.setContentView(scroll);
        LinearLayout header=row();header.setGravity(Gravity.CENTER_VERTICAL);
        TextView title=text(getString(R.string.connection_controls),21,TEXT);title.setTypeface(null,Typeface.BOLD);header.addView(title,new LinearLayout.LayoutParams(0,-2,1));
        Button done=button(getString(R.string.done),false);done.setOnClickListener(v->controls.dismiss());header.addView(done,new LinearLayout.LayoutParams(dp(76),dp(44)));add(sheet,header,0);
        phaseLabel=text(getString(R.string.ready),11,ACCENT);phaseLabel.setId(R.id.connection_phase);phaseLabel.setTypeface(null,Typeface.BOLD);add(sheet,phaseLabel,12);
        message=text(lastMessage,12,MUTED);message.setId(R.id.connection_message);message.setLineSpacing(dp(3),1);add(sheet,message,6);
        LinearLayout actions=row();primary=button(getString(R.string.reconnect),true);primary.setId(R.id.reconnect_display);primary.setOnClickListener(v->reconnect());
        stop=button(getString(R.string.disconnect),false);stop.setId(R.id.disconnect_display);stop.setOnClickListener(v->{pendingLink=null;manualStop=true;phase=null;lastMessage=getString(R.string.disconnected);disconnect();render();});
        actions.addView(primary,weighted(48,0));actions.addView(stop,weighted(48,10));add(sheet,actions,16);
        TextView diagnostics=text(getString(R.string.live_diagnostics),14,TEXT);diagnostics.setTypeface(null,Typeface.BOLD);add(sheet,diagnostics,22);
        LinearLayout values=row();
        streamFps=metric(values,getString(R.string.stream_fps),R.id.stream_fps);
        decoderFps=metric(values,getString(R.string.decoder_fps),R.id.decoder_fps);
        ping=metric(values,getString(R.string.ping_rtt),R.id.ping_rtt);
        bandwidth=metric(values,getString(R.string.video_mbps),R.id.video_mbps);add(sheet,values,10);
        sessionDetails=text("",10,MUTED);add(sheet,sessionDetails,8);
        graph=new DiagnosticsGraph(this);add(sheet,graph,10,58);
        TextView explanation=text(getString(R.string.ping_explanation),10,MUTED);explanation.setLineSpacing(dp(2),1);add(sheet,explanation,7);
        videoDetails=text("",11,TEXT);videoDetails.setId(R.id.video_details);videoDetails.setLineSpacing(dp(4),1);add(sheet,videoDetails,16);
        LinearLayout orientationRow=row();orientationRow.setGravity(Gravity.CENTER_VERTICAL);
        orientationRow.addView(text(getString(R.string.phone_orientation),12,TEXT),new LinearLayout.LayoutParams(0,-2,1));
        Spinner orientation=new Spinner(this);orientation.setContentDescription(getString(R.string.phone_orientation));
        ArrayAdapter<CharSequence> choices=new ArrayAdapter<CharSequence>(this,android.R.layout.simple_spinner_dropdown_item,getResources().getTextArray(R.array.orientation_options)) {
            @Override public View getView(int p,View recycled,android.view.ViewGroup parent){TextView value=(TextView)super.getView(p,recycled,parent);value.setTextColor(TEXT);value.setTextSize(12);return value;}
        };
        orientation.setAdapter(choices);orientation.setBackgroundTintList(android.content.res.ColorStateList.valueOf(MUTED));
        orientation.setSelection(Math.max(0,Math.min(2,getPreferences(MODE_PRIVATE).getInt("orientation",0))));
        orientation.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){
            @Override public void onNothingSelected(AdapterView<?> parent){}
            @Override public void onItemSelected(AdapterView<?> parent,View view,int position,long id){getPreferences(MODE_PRIVATE).edit().putInt("orientation",position).apply();applyOrientation(position);}
        });orientationRow.addView(orientation,new LinearLayout.LayoutParams(dp(140),dp(44)));add(sheet,orientationRow,16);
        @SuppressWarnings("deprecation") Switch overlaySwitch=new Switch(this);overlaySwitch.setText(R.string.show_diagnostics_overlay);overlaySwitch.setTextColor(TEXT);overlaySwitch.setTextSize(12);
        overlaySwitch.setChecked(getPreferences(MODE_PRIVATE).getBoolean("diagnostics_overlay",false));overlaySwitch.setOnCheckedChangeListener((v,checked)->{showOverlay=checked;getPreferences(MODE_PRIVATE).edit().putBoolean("diagnostics_overlay",checked).apply();renderMetrics();});add(sheet,overlaySwitch,8,44);
        Button copy=button(getString(R.string.copy_diagnostics),false);copy.setId(R.id.copy_diagnostics);copy.setOnClickListener(v->copyDiagnostics());add(sheet,copy,12,46);
        TextView gesturesTitle=text(getString(R.string.touch_gestures_title),14,TEXT);gesturesTitle.setTypeface(null,Typeface.BOLD);add(sheet,gesturesTitle,20);
        TextView gesturesHelp=text(getString(R.string.touch_gestures_help),11,MUTED);gesturesHelp.setLineSpacing(dp(3),1);add(sheet,gesturesHelp,8);
        @SuppressWarnings("deprecation") Switch usbAutostart=new Switch(this);
        usbAutostart.setText(R.string.usb_autostart);usbAutostart.setTextColor(TEXT);usbAutostart.setTextSize(12);
        usbAutostart.setChecked(getSharedPreferences("usb",MODE_PRIVATE).getBoolean("autostart",false));
        usbAutostart.setOnCheckedChangeListener((v,checked)->getSharedPreferences("usb",MODE_PRIVATE).edit().putBoolean("autostart",checked).apply());add(sheet,usbAutostart,16,48);
        add(sheet,text(getString(R.string.usb_autostart_help),11,MUTED),6);
        Button network=button(getString(R.string.network_pairing),false);network.setOnClickListener(v->{networkSection.setVisibility(networkSection.getVisibility()==View.VISIBLE?View.GONE:View.VISIBLE);resizeControls();});add(sheet,network,10,46);
        networkSection=column();networkSection.setVisibility(View.GONE);
        TextView help=text(getString(R.string.network_pairing_help),11,MUTED);help.setLineSpacing(dp(3),1);add(networkSection,help,10);
        link=new EditText(this);link.setTextColor(TEXT);link.setHintTextColor(MUTED);link.setTextSize(12);link.setHint(R.string.connection_hint);link.setSingleLine(true);
        link.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);link.setAutofillHints((String[])null);link.setSaveEnabled(false);link.setBackgroundTintList(android.content.res.ColorStateList.valueOf(ACCENT));
        link.addTextChangedListener(new TextWatcher(){@Override public void beforeTextChanged(CharSequence s,int start,int count,int after){}@Override public void onTextChanged(CharSequence s,int start,int before,int count){}@Override public void afterTextChanged(Editable value){renderActions();}});add(networkSection,link,8,52);add(sheet,networkSection,0);
        controls.setOnDismissListener(v->{applyImmersive();showRevealControls();});
    }

    private void reconnect() {
        String uri=link.getText().toString().trim();if(uri.length()==0)uri=currentLink;
        if(uri.length()==0){lastMessage=getString(R.string.android_home_help);render();return;}
        manualStop=false;
        if(client!=null){pendingLink=uri;disconnect();}else connectTo(uri);
    }
    private void connectTo(String uri) {
        if(client!=null){if(!currentLink.equals(uri)||disconnecting){pendingLink=uri;disconnect();}return;}
        try {
            ConnectionSpec spec=ConnectionSpec.parse(uri);pendingLink=null;currentLink=uri;manualStop=false;target=(spec.host.equals("127.0.0.1")?"USB / ADB":"Network")+" · "+spec.host+":"+spec.port;
            latest=null;streaming=false;phase=DisplayClient.Phase.CONNECTING;lastMessage=getString(R.string.connecting);final int epoch=++generation;
            client=new DisplayClient(spec,display,new DisplayClient.Listener(){
                @Override public void status(String value){runOnUiThread(()->{if(epoch==generation){lastMessage=value;render();}});}
                @Override public void phase(DisplayClient.Phase value){runOnUiThread(()->{if(epoch==generation){phase=value;streaming=value==DisplayClient.Phase.STREAMING;render();}});}
                @Override public void video(String value,String hostEncoder,String androidDecoder,int width,int height,int fps){runOnUiThread(()->{if(epoch==generation){codec=value;encoder=hostEncoder;decoder=androidDecoder;videoWidth=width;videoHeight=height;targetFps=fps;latest=null;graph.clear();render();}});}
                @Override public void metrics(StreamStats.Sample sample){runOnUiThread(()->{if(epoch==generation){latest=sample;graph.add((float)sample.streamFps);renderMetrics();}});}
                @Override public void ended(){runOnUiThread(()->{
                    if(epoch!=generation)return;
                    client=null;disconnecting=false;streaming=false;
                    if(phase!=DisplayClient.Phase.ERROR)phase=null;
                    render();String next=pendingLink;if(next!=null&&foreground){pendingLink=null;connectTo(next);}
                });}
            });
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);render();client.start();
        }catch(IllegalArgumentException e){phase=DisplayClient.Phase.ERROR;lastMessage=e.getMessage();render();}
    }
    private void disconnect(){if(client!=null){disconnecting=true;client.close();}renderActions();}
    private void render() {
        boolean active=client!=null;
        empty.setVisibility(streaming?View.GONE:View.VISIBLE);progress.setVisibility(active&&!streaming?View.VISIBLE:View.GONE);
        if(!active){reveal.removeCallbacks(hideReveal);reveal.setVisibility(View.GONE);}
        else if(!revealSessionActive||streaming&&!revealStreaming)showRevealControls();
        revealSessionActive=active;revealStreaming=streaming;
        standbyMessage.setText(active?lastMessage:phase==DisplayClient.Phase.ERROR?lastMessage:getString(R.string.android_home_help));
        phaseLabel.setText(phase==DisplayClient.Phase.STREAMING?R.string.phase_streaming:phase==DisplayClient.Phase.AUTHENTICATING?R.string.phase_authenticating:phase==DisplayClient.Phase.CONNECTING?R.string.phase_connecting:phase==DisplayClient.Phase.RETRYING?R.string.phase_retrying:phase==DisplayClient.Phase.ERROR?R.string.phase_error:R.string.phase_idle);
        phaseLabel.setTextColor(phase==DisplayClient.Phase.ERROR?0xfff99b9f:phase==DisplayClient.Phase.RETRYING?0xffecc48b:ACCENT);message.setText(lastMessage);
        videoDetails.setText(videoWidth>0?String.format(Locale.US,"%s · %d × %d · %d fps target\nWindows: %s\nAndroid: %s",codec.toUpperCase(Locale.US),videoWidth,videoHeight,targetFps,encoder,decoder):getString(R.string.video_details_waiting));
        if(!active)getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        renderActions();renderMetrics();applyImmersive();
    }
    private void renderActions(){if(primary==null||link==null)return;primary.setText(currentLink.length()==0?R.string.connect_display:R.string.reconnect);primary.setEnabled(!disconnecting&&(currentLink.length()>0||link.length()>0));stop.setEnabled(client!=null&&!disconnecting);link.setEnabled(client==null);}
    private void renderMetrics(){
        if(streamFps==null)return;
        boolean live=streaming&&latest!=null;
        streamFps.setText(live?String.format(Locale.US,"%.1f",latest.streamFps):"—");decoderFps.setText(live?String.format(Locale.US,"%.1f",latest.decoderFps):"—");
        ping.setText(live&&latest.roundTripMs>=0?String.format(Locale.US,"%.0f",latest.roundTripMs):"—");bandwidth.setText(live?String.format(Locale.US,"%.1f",latest.mbps):"—");
        sessionDetails.setText(latest!=null?String.format(Locale.US,"%s · %02d:%02d · %d frames",target,latest.uptimeSeconds/60,latest.uptimeSeconds%60,latest.receivedFrames):target);
        overlay.setVisibility(showOverlay&&streaming?View.VISIBLE:View.GONE);
        if(live)overlay.setText(String.format(Locale.US,"%.1f fps · %s ms ping · %.1f Mbps",latest.streamFps,latest.roundTripMs>=0?String.format(Locale.US,"%.0f",latest.roundTripMs):"—",latest.mbps));
    }
    private void copyDiagnostics(){
        String summary=String.format(Locale.US,"Dis Android 0.4.3\nDevice: %s (Android %s, API %d)\nConnection: %s\nState: %s\nVideo: %s %dx%d, target %d fps\nWindows encoder: %s\nAndroid decoder: %s\n",Build.MODEL,Build.VERSION.RELEASE,Build.VERSION.SDK_INT,target,phase==null?"idle":phase.name(),codec,videoWidth,videoHeight,targetFps,encoder,decoder);
        if(latest!=null)summary+=String.format(Locale.US,"Stream: %.1f fps\nDecoder: %.1f fps\nVideo bandwidth: %.2f Mbps\nPing RTT: %s ms\nReceived/decoded frames: %d/%d\nSession: %d s\n",latest.streamFps,latest.decoderFps,latest.mbps,latest.roundTripMs>=0?String.format(Locale.US,"%.1f",latest.roundTripMs):"unavailable",latest.receivedFrames,latest.decodedFrames,latest.uptimeSeconds);
        if(latest!=null)summary+="Decoder queue-to-release: "+timing(latest.decoderMs)+" ms\nDecoder input wait: "+timing(latest.inputWaitMs)+" ms\nACK write: "+timing(latest.ackWriteMs)+" ms\n";
        summary+="Status: "+lastMessage;
        ClipboardManager clipboard=(ClipboardManager)getSystemService(CLIPBOARD_SERVICE);clipboard.setPrimaryClip(ClipData.newPlainText("Dis diagnostics",summary));Toast.makeText(this,R.string.diagnostics_copied,Toast.LENGTH_SHORT).show();
    }
    private static String timing(double milliseconds){return milliseconds>=0?String.format(Locale.US,"%.1f",milliseconds):"unavailable";}
    private void showRevealControls(){
        if(reveal==null)return;
        reveal.removeCallbacks(hideReveal);
        if(!foreground||client==null||controls.isShowing())return;
        reveal.setVisibility(View.VISIBLE);reveal.postDelayed(hideReveal,5000);
    }
    @Override public boolean dispatchTouchEvent(MotionEvent event){
        // Observe the gesture without consuming it or changing remote pointer delivery.
        boolean handled=super.dispatchTouchEvent(event);
        if(event.getActionMasked()==MotionEvent.ACTION_DOWN)showRevealControls();
        return handled;
    }
    private void handleBack(){if(client!=null)showRevealControls();else finish();}
    @SuppressWarnings("deprecation") @Override public void onBackPressed(){handleBack();}
    private void showControls(){reveal.removeCallbacks(hideReveal);reveal.setVisibility(View.GONE);controls.show();Window window=controls.getWindow();if(window!=null){window.setBackgroundDrawable(new ColorDrawable(Color.TRANSPARENT));window.setGravity(Gravity.BOTTOM);window.setDimAmount(.3f);window.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE|WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_HIDDEN);}resizeControls();render();dashboardScroll.post(()->dashboardScroll.scrollTo(0,0));}
    private void resizeControls(){if(controls.isShowing()&&controls.getWindow()!=null)controls.getWindow().setLayout(-1,Math.min(dp(570),(int)(getResources().getDisplayMetrics().heightPixels*.90)));}
    private void applyOrientation(int value){setRequestedOrientation(value==1?ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE:value==2?ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT:ActivityInfo.SCREEN_ORIENTATION_FULL_SENSOR);}
    @SuppressWarnings("deprecation") private void applyImmersive(){getWindow().getDecorView().setSystemUiVisibility(streaming?View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY|View.SYSTEM_UI_FLAG_FULLSCREEN|View.SYSTEM_UI_FLAG_HIDE_NAVIGATION|View.SYSTEM_UI_FLAG_LAYOUT_STABLE|View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN|View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION:View.SYSTEM_UI_FLAG_LAYOUT_STABLE);display.post(display::updateVideoLayout);}
    @Override public void onWindowFocusChanged(boolean focused){super.onWindowFocusChanged(focused);if(focused&&display!=null)applyImmersive();}
    @Override public void onConfigurationChanged(Configuration config){super.onConfigurationChanged(config);root.post(()->{display.updateVideoLayout();resizeControls();});}
    @Override protected void onResume(){super.onResume();foreground=true;acceptUsbConnection();if(client==null&&pendingLink!=null){String next=pendingLink;pendingLink=null;connectTo(next);}showRevealControls();}
    @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);setIntent(intent);if(foreground)acceptUsbConnection();}
    private void acceptUsbConnection(){String uri=UsbConnectActivity.takeConnection();if(uri!=null){if(controls.isShowing())controls.dismiss();connectTo(uri);}}
    @Override protected void onStop(){foreground=false;reveal.removeCallbacks(hideReveal);reveal.setVisibility(View.GONE);if(client!=null&&!manualStop){lastMessage=getString(R.string.connection_paused);disconnect();}super.onStop();}
    @Override protected void onDestroy(){foreground=false;reveal.removeCallbacks(hideReveal);generation++;if(client!=null)client.close();if(controls!=null&&controls.isShowing())controls.dismiss();super.onDestroy();}

    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    private ImageButton iconButton(int icon,int label){
        ImageButton view=new ImageButton(this);view.setImageResource(icon);view.setImageTintList(android.content.res.ColorStateList.valueOf(TEXT));
        view.setContentDescription(getString(label));view.setTooltipText(getString(label));view.setScaleType(ImageView.ScaleType.CENTER_INSIDE);
        view.setBackground(shape(0xff29374a,10));view.setPadding(dp(12),dp(12),dp(12),dp(12));return view;
    }
    private void updateTouchLock(){
        boolean locked=display.isTouchLocked();int label=locked?R.string.unlock_touch:R.string.lock_touch;
        touchLock.setImageResource(locked?R.drawable.ic_touch_locked:R.drawable.ic_touch_unlocked);
        touchLock.setImageTintList(android.content.res.ColorStateList.valueOf(locked?ACCENT:TEXT));
        touchLock.setBackground(shape(locked?0xff19392f:0xff29374a,10));touchLock.setContentDescription(getString(label));touchLock.setTooltipText(getString(label));
    }
    private LinearLayout column(){LinearLayout view=new LinearLayout(this);view.setOrientation(LinearLayout.VERTICAL);return view;}
    private LinearLayout row(){LinearLayout view=new LinearLayout(this);view.setOrientation(LinearLayout.HORIZONTAL);return view;}
    private TextView text(String value,int size,int color){TextView view=new TextView(this);view.setText(value);view.setTextSize(size);view.setTextColor(color);return view;}
    private GradientDrawable shape(int color,int radius){GradientDrawable shape=new GradientDrawable();shape.setColor(color);shape.setCornerRadius(dp(radius));return shape;}
    private Button button(String value,boolean primary){Button view=new Button(this);view.setText(value);view.setTextSize(12);view.setAllCaps(false);view.setTextColor(primary?BG:TEXT);view.setBackground(shape(primary?ACCENT:0xff29374a,10));view.setPadding(dp(14),0,dp(14),0);view.setMinHeight(dp(44));view.setMinimumHeight(dp(44));return view;}
    private LinearLayout.LayoutParams weighted(int height,int leftMargin){LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(0,dp(height),1);p.leftMargin=dp(leftMargin);return p;}
    private void add(LinearLayout parent,View child,int top){LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.topMargin=dp(top);parent.addView(child,p);}
    private void add(LinearLayout parent,View child,int top,int height){LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,dp(height));p.topMargin=dp(top);parent.addView(child,p);}
    private TextView metric(LinearLayout row,String label,int id){LinearLayout card=column();card.setPadding(dp(7),dp(10),dp(7),dp(9));card.setBackground(shape(INSET,10));TextView value=text("—",18,TEXT);value.setId(id);value.setTypeface(null,Typeface.BOLD);card.addView(value);TextView title=text(label,9,MUTED);LinearLayout.LayoutParams t=new LinearLayout.LayoutParams(-1,-2);t.topMargin=dp(5);card.addView(title,t);row.addView(card,weighted(70,row.getChildCount()==0?0:6));return value;}
}
