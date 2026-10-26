package io.github.eoftisreal.dis;

import java.io.IOException;
import java.util.Arrays;

public final class H264ParameterSets {
    private H264ParameterSets() { }
    public static byte[][] parse(byte[] data) throws IOException {
        if (data.length>65536) throw new IOException("Codec configuration too large");
        byte[][] sets=new byte[2][];
        int start=find(data,0);
        while (start>=0) {
            int prefix=data[start+2]==1 ? 3 : 4;
            int begin=start+prefix, next=find(data,begin), end=next<0 ? data.length : next;
            if (begin<end) {
                int type=data[begin]&31;
                if (type==7 || type==8) {
                    byte[] nal=new byte[end-begin+4]; nal[3]=1;
                    System.arraycopy(data,begin,nal,4,end-begin); sets[type==7?0:1]=nal;
                }
            }
            start=next;
        }
        if (sets[0]==null || sets[1]==null) throw new IOException("Missing H.264 SPS/PPS");
        return sets;
    }
    private static int find(byte[] b,int offset) {
        for(int i=offset;i+2<b.length;i++) if(b[i]==0 && b[i+1]==0 && (b[i+2]==1 || (i+3<b.length && b[i+2]==0 && b[i+3]==1))) return i;
        return -1;
    }
}
