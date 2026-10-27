package io.github.eoftisreal.dis;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

public final class InputQueueTest {
    public static void main(String[] args) throws Exception {
        InputQueue queue=new InputQueue();
        require(queue.offer(pointer(1,0,0,0)));
        for (int n=1;n<=100_000;n++) require(queue.offer(pointer(1,1,0,n)));
        require(queue.offer(pointer(1,2,0,100_001)));
        List<byte[]> received=new ArrayList<>();
        byte[] packet;
        do { packet=queue.take(); received.add(packet); } while (packet[1]!=2);
        require(received.size()==9); // Contact start, seven retained movement samples, contact end.
        require(received.get(0)[1]==0);
        require(value(received.get(received.size()-2))==100_000);
        require(value(received.get(received.size()-1))==100_001);

        queue.clear();
        for (int n=0;n<8;n++) require(queue.offer(pointer(1,1,0,n)));
        require(queue.offer(pointer(1,1,1,8))); // Barrel-button transition must survive coalescing.
        require(queue.offer(pointer(1,1,1,9)));
        require(queue.offer(pointer(1,4,1,10))); // Hover is separate from contact movement.
        require(queue.offer(pointer(0,1,0,11))); // Tool changes are separate too.
        for (int n=0;n<8;n++) require(value(queue.take())==n);
        require(value(queue.take())==9);
        require(value(queue.take())==10);
        require(value(queue.take())==11);

        queue.clear();
        require(queue.offer(pointer(0,0,0,1)));
        require(queue.offer(pointer(0,3,0,2))); // Cancel must precede a replacement stylus contact.
        require(queue.offer(pointer(1,0,0,3)));
        require(queue.offer(pointer(1,2,0,4)));
        for (int n=1;n<=4;n++) require(value(queue.take())==n);
        for (int n=0;n<128;n++) require(queue.offer(pointer(1,n%2==0 ? 0 : 2,0,n)));
        require(!queue.offer(pointer(1,0,0,129))); // Fail closed if contact transitions alone fill the queue.
        queue.clear();

        require(queue.poll(1,TimeUnit.MILLISECONDS)==null);
        require(queue.offer(pointer(2,0,0,1)));
        for(int n=0;n<20;n++)require(queue.offer(pointer(0,5,0,n)));
        require(queue.offer(pointer(0,6,0,21)));
        require(queue.offer(pointer(2,3,0,22)));
        require(queue.poll(1,TimeUnit.SECONDS)[0]==2);
        for(int n=0;n<20;n++)require(value(queue.poll(1,TimeUnit.SECONDS))==n);
        require(queue.poll(1,TimeUnit.SECONDS)[1]==6);
        require(queue.poll(1,TimeUnit.SECONDS)[1]==3);

        AtomicBoolean cancelled=new AtomicBoolean();
        CountDownLatch entered=new CountDownLatch(1);
        Thread consumer=new Thread(() -> {
            entered.countDown();
            try { queue.take(); }
            catch (InterruptedException expected) { cancelled.set(true); }
        });
        consumer.start();
        require(entered.await(2,TimeUnit.SECONDS));
        consumer.interrupt(); consumer.join(2000);
        require(!consumer.isAlive() && cancelled.get());
        System.out.println("Input queue tests passed: bounded motion backlog, retained samples, button/tool/contact ordering and interruptible shutdown.");
    }

    private static byte[] pointer(int kind,int action,int buttons,int value) {
        byte[] packet=Wire.pointer(kind,action,buttons,.5f,.5f,.5f,0,0);
        java.nio.ByteBuffer.wrap(packet).putInt(20,value);
        return packet;
    }
    private static int value(byte[] packet) { return java.nio.ByteBuffer.wrap(packet).getInt(20); }
    private static void require(boolean valid) { if (!valid) throw new AssertionError(); }
}

