package io.github.eoftisreal.dis;

import android.app.Activity;
import android.content.pm.ActivityInfo;
import android.graphics.Color;
import android.os.Bundle;
import android.text.InputType;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.FrameLayout;
import android.widget.Spinner;
import android.widget.ArrayAdapter;
import android.widget.AdapterView;

public final class MainActivity extends Activity {
    private DisplayClient client;
    private DisplayView display;
    private EditText link;
    private TextView status;
    private Button connect;
    private LinearLayout controls;
    private boolean fullscreen;
    private int generation;
    private boolean foreground;
    private boolean disconnecting;
    private String currentLink="",pendingLink;
    private Button full;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        LinearLayout root=new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL); root.setBackgroundColor(Color.rgb(16,20,28));
        controls=new LinearLayout(this); controls.setOrientation(LinearLayout.VERTICAL); controls.setPadding(dp(16),dp(16),dp(16),dp(8));
        TextView heading=new TextView(this); heading.setText(R.string.app_heading); heading.setTextSize(22); heading.setTextColor(Color.WHITE); controls.addView(heading);
        TextView help=new TextView(this); help.setText(R.string.setup_help); help.setTextColor(Color.LTGRAY); controls.addView(help);
        link=new EditText(this); link.setTextColor(Color.WHITE); link.setHintTextColor(Color.GRAY); link.setHint(R.string.connection_hint);
        link.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS); link.setSingleLine(true);
        link.setAutofillHints((String[])null); link.setSaveEnabled(false); controls.addView(link);
        connect=new Button(this); connect.setText(R.string.connect_display); controls.addView(connect);
        root.addView(controls,new LinearLayout.LayoutParams(-1,-2));
        LinearLayout bar=new LinearLayout(this); bar.setOrientation(LinearLayout.HORIZONTAL); bar.setPadding(dp(12),0,dp(8),0);
        status=new TextView(this); status.setText(R.string.ready); status.setTextColor(Color.LTGRAY); status.setTextSize(12);
        bar.addView(status,new LinearLayout.LayoutParams(0,-2,1));
        full=new Button(this); full.setText(R.string.full_screen); full.setOnClickListener(v -> {
            fullscreen=!fullscreen; controls.setVisibility(fullscreen ? View.GONE : View.VISIBLE); full.setText(fullscreen ? R.string.controls : R.string.full_screen);
        }); bar.addView(full);
        Spinner orientation=new Spinner(this);
        ArrayAdapter<CharSequence> orientationChoices=ArrayAdapter.createFromResource(this,R.array.orientation_options,android.R.layout.simple_spinner_dropdown_item);
        orientation.setAdapter(orientationChoices);
        orientation.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onNothingSelected(AdapterView<?> parent) { }
            @Override public void onItemSelected(AdapterView<?> parent,View view,int position,long id) {
                setRequestedOrientation(position==1 ? ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                    : position==2 ? ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT : ActivityInfo.SCREEN_ORIENTATION_FULL_SENSOR);
            }
        }); bar.addView(orientation); root.addView(bar);
        FrameLayout surfaceArea=new FrameLayout(this); surfaceArea.setBackgroundColor(Color.BLACK);
        display=new DisplayView(this); surfaceArea.addView(display,new FrameLayout.LayoutParams(-1,-1));
        surfaceArea.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob) -> display.updateVideoLayout());
        root.addView(surfaceArea,new LinearLayout.LayoutParams(-1,0,1));
        setContentView(root);
        connect.setOnClickListener(v -> {
            if (client!=null) { disconnect(); status.setText(R.string.disconnected); return; }
            connectTo(link.getText().toString());
        });
    }

    private void connectTo(String uri) {
        if (client!=null) {
            if (!currentLink.equals(uri) || disconnecting) { pendingLink=uri; disconnect(); }
            return;
        }
        try {
                ConnectionSpec spec=ConnectionSpec.parse(uri);
                currentLink=uri;
                final int epoch=++generation;
                client=new DisplayClient(spec,display,new DisplayClient.Listener() {
                    @Override public void status(String text) { runOnUiThread(() -> { if (epoch==generation) status.setText(text); }); }
                    @Override public void ended() { runOnUiThread(() -> {
                        if (epoch!=generation) return;
                        client=null; disconnecting=false; connect.setEnabled(true); setConnected(false);
                        String next=pendingLink; pendingLink=null;
                        if (next!=null && foreground) connectTo(next);
                    }); }
                });
                setConnected(true); client.start();
        } catch (IllegalArgumentException e) { status.setText(e.getMessage()); }
    }

    private void setConnected(boolean connected) {
        connect.setText(connected ? R.string.disconnect : R.string.connect_display); link.setEnabled(!connected);
        if (connected) getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        else getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    }
    private void disconnect() {
        if (client!=null) { disconnecting=true; connect.setEnabled(false); client.close(); }
        else setConnected(false);
    }
    private int dp(int n) { return Math.round(n*getResources().getDisplayMetrics().density); }
    @Override protected void onResume() {
        super.onResume(); foreground=true;
        String usb=UsbConnectActivity.takeConnection();
        if (usb!=null) {
            link.setText(usb); controls.setVisibility(View.GONE); fullscreen=true; full.setText(R.string.controls);
            connectTo(usb);
        }
    }
    @Override protected void onStop() { foreground=false; pendingLink=null; disconnect(); super.onStop(); }
}
