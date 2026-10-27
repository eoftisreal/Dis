package io.github.eoftisreal.dis;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;

/** Read-only USB preference, restricted to authorized shell/system callers by DUMP. */
public final class UsbPreferencesProvider extends ContentProvider {
    @Override public boolean onCreate() { return true; }
    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] args, String sort) {
        if (!"/preferences".equals(uri.getPath())) throw new IllegalArgumentException("Unknown path");
        MatrixCursor cursor = new MatrixCursor(new String[]{"autostart"});
        cursor.addRow(new Object[]{getContext().getSharedPreferences("usb", 0).getBoolean("autostart", false) ? 1 : 0});
        return cursor;
    }
    @Override public String getType(Uri uri) { return "vnd.android.cursor.item/vnd.dis.usb"; }
    @Override public Uri insert(Uri uri, ContentValues values) { throw new UnsupportedOperationException(); }
    @Override public int update(Uri uri, ContentValues values, String where, String[] args) { throw new UnsupportedOperationException(); }
    @Override public int delete(Uri uri, String where, String[] args) { throw new UnsupportedOperationException(); }
}
