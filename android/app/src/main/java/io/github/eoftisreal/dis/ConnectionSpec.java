package io.github.eoftisreal.dis;

import java.net.URI;
import java.net.URISyntaxException;
import java.util.HashMap;
import java.util.Map;

public final class ConnectionSpec {
    public final String host, token;
    public final int port;
    public final byte[] fingerprint;

    private ConnectionSpec(String host, int port, String token, byte[] fingerprint) {
        this.host=host; this.port=port; this.token=token; this.fingerprint=fingerprint;
    }

    public static ConnectionSpec parse(String text) {
        try {
            URI uri=new URI(text.trim());
            if (!"dis".equals(uri.getScheme()) || uri.getHost()==null || uri.getHost().isEmpty()
                || uri.getPort()<1024 || uri.getPort()>65535 || uri.getRawUserInfo()!=null
                || uri.getRawFragment()!=null || (uri.getRawPath()!=null && !uri.getRawPath().isEmpty())
                || uri.getRawQuery()==null) throw new IllegalArgumentException("Invalid connection link");
            Map<String,String> query=new HashMap<>();
            for (String pair:uri.getRawQuery().split("&",-1)) {
                String[] entry=pair.split("=",-1);
                if (entry.length!=2 || query.put(entry[0],entry[1])!=null) throw new IllegalArgumentException("Duplicate or invalid connection parameter");
            }
            String fp=query.get("fp"), token=query.get("token");
            if (query.size()!=2 || fp==null || token==null || !fp.matches("[0-9a-fA-F]{64}") || !token.matches("[0-9a-fA-F]{64}"))
                throw new IllegalArgumentException("Copy the complete connection link from Dis on Windows");
            byte[] pin=new byte[32];
            for (int i=0;i<32;i++) pin[i]=(byte)Integer.parseInt(fp.substring(i*2,i*2+2),16);
            return new ConnectionSpec(uri.getHost(),uri.getPort(),token.toLowerCase(java.util.Locale.ROOT),pin);
        } catch (URISyntaxException e) { throw new IllegalArgumentException("Invalid connection link",e); }
    }
}
