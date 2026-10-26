package io.github.eoftisreal.dis;

import android.app.Activity;
import android.graphics.Color;
import android.os.Bundle;
import android.text.InputType;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;

public final class MainActivity extends Activity {
    private DisplayClient client;
    private DisplayView display;
    private EditText link;
    private TextView status;
    private Button connect;
    private LinearLayout controls;
    private boolean fullscreen;
    private int generation;

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
        Button full=new Button(this); full.setText(R.string.full_screen); full.setOnClickListener(v -> {
            fullscreen=!fullscreen; controls.setVisibility(fullscreen ? View.GONE : View.VISIBLE); full.setText(fullscreen ? R.string.controls : R.string.full_screen);
        }); bar.addView(full); root.addView(bar);
        display=new DisplayView(this); root.addView(display,new LinearLayout.LayoutParams(-1,0,1));
        setContentView(root);
        connect.setOnClickListener(v -> {
            if (client!=null) { disconnect(); status.setText(R.string.disconnected); return; }
            try {
                ConnectionSpec spec=ConnectionSpec.parse(link.getText().toString());
                final int epoch=++generation;
                client=new DisplayClient(spec,display,new DisplayClient.Listener() {
                    @Override public void status(String text) { runOnUiThread(() -> { if (epoch==generation) status.setText(text); }); }
                    @Override public void ended() { runOnUiThread(() -> { if (epoch==generation) { client=null; connect.setEnabled(true); setConnected(false); } }); }
                });
                setConnected(true); client.start();
            } catch (IllegalArgumentException e) { status.setText(e.getMessage()); }
        });
    }

    private void setConnected(boolean connected) {
        connect.setText(connected ? R.string.disconnect : R.string.connect_display); link.setEnabled(!connected);
        if (connected) getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        else getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    }
    private void disconnect() {
        if (client!=null) { connect.setEnabled(false); client.close(); }
        else setConnected(false);
    }
    private int dp(int n) { return Math.round(n*getResources().getDisplayMetrics().density); }
    @Override protected void onStop() { disconnect(); super.onStop(); }
}
