package io.github.eoftisreal.dis;

import java.util.concurrent.atomic.AtomicLong;

public final class StreamStatsTest {
    private static int checks;
    public static void main(String[] args) throws Exception {
        AtomicLong time=new AtomicLong(1_000_000_000L);
        StreamStats stats=new StreamStats(time::get);
        require(stats.sample()==null);
        for(int i=0;i<60;i++)stats.receivedFrame(1000);
        for(int i=0;i<58;i++)stats.decodedFrame();
        time.addAndGet(1_000_000_000L);
        StreamStats.Sample sample=stats.sample();
        require(sample.streamFps==60 && sample.decoderFps==58);
        require(Math.abs(sample.mbps-0.48)<0.000001 && sample.roundTripMs<0);
        require(sample.receivedFrames==60 && sample.decodedFrames==58 && sample.uptimeSeconds==1);
        require(sample.decoderMs<0 && sample.inputWaitMs<0 && sample.ackWriteMs<0);
        stats.decodedFrame(20_000_000L,2_000_000L);stats.decodedFrame(40_000_000L,4_000_000L);
        stats.ackWrite(6_000_000L);stats.ackWrite(8_000_000L);
        byte[] ping=stats.ping(7);
        require(!stats.pong(8,ping));
        require(!stats.pong(7,new byte[8]));
        require(!stats.pong(7,null));
        time.addAndGet(12_000_000L);
        require(stats.pong(7,ping));
        require(!stats.pong(7,ping));
        time.addAndGet(988_000_000L);sample=stats.sample();
        require(sample.roundTripMs==12 && sample.streamFps==0 && sample.mbps==0);
        require(sample.decoderMs==30 && sample.inputWaitMs==3 && sample.ackWriteMs==7);
        time.addAndGet(4_000_000_000L);stats.ping(9);sample=stats.sample();
        require(sample.roundTripMs<0); // sending a new ping must not make an old RTT look current
        require(sample.decoderMs<0 && sample.inputWaitMs<0 && sample.ackWriteMs<0);
        AtomicLong clock=new AtomicLong();StreamStats parallel=new StreamStats(clock::get);
        Thread[] workers=new Thread[4];
        for(int i=0;i<4;i++){workers[i]=new Thread(()->{for(int j=0;j<1000;j++){parallel.receivedFrame(10);parallel.decodedFrame();}});workers[i].start();}
        for(Thread worker:workers)worker.join();
        clock.set(1_000_000_000L);sample=parallel.sample();
        require(sample.receivedFrames==4000 && sample.decodedFrames==4000 && sample.streamFps==4000);
        boolean rejected=false;try{parallel.receivedFrame(-1);}catch(IllegalArgumentException expected){rejected=true;}require(rejected);
        System.out.println(checks+" Android live-diagnostics checks passed.");
    }
    private static void require(boolean value){if(!value)throw new AssertionError();checks++;}
}
