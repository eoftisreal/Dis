package io.github.eoftisreal.dis;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.util.Base64;
import java.nio.charset.StandardCharsets;

/** The manifest restricts this USB handoff to callers such as authorized ADB shell, which hold DUMP. */
public final class UsbConnectActivity extends Activity {
    private static String pending;
    public static synchronized String takeConnection() { String value=pending; pending=null; return value; }
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        String encoded=getIntent().getStringExtra("dis_pairing_b64");
        try {
            if (encoded==null || encoded.length()>4096) throw new IllegalArgumentException();
            String uri=new String(Base64.decode(encoded,Base64.URL_SAFE | Base64.NO_WRAP),StandardCharsets.UTF_8);
            ConnectionSpec.parse(uri);
            synchronized(UsbConnectActivity.class) { pending=uri; }
            startActivity(new Intent(this,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP));
        } catch (IllegalArgumentException ignored) { /* Never accept an arbitrary unvalidated connection from an Intent. */ }
        finish();
    }
}
