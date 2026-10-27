package io.github.eoftisreal.dis;

import java.util.ArrayDeque;
import java.util.concurrent.TimeUnit;

/** Keep the latest pending movement without ever replacing a contact or button transition. */
final class InputQueue {
    private static final int CAPACITY=128;
    private static final int MOTION_BACKLOG=8;
    private final ArrayDeque<byte[]> packets=new ArrayDeque<>();

    synchronized boolean offer(byte[] packet) {
        if (packet.length!=24) throw new IllegalArgumentException("Invalid pointer packet");
        byte[] previous=packets.peekLast();
        if (packets.size()>=MOTION_BACKLOG && previous!=null && (packet[1]==1 || packet[1]==4)
            && packet[0]==previous[0] && packet[1]==previous[1]
            && packet[2]==previous[2] && packet[3]==previous[3]) {
            packets.removeLast();
        } else if (packets.size()>=CAPACITY) return false;
        packets.addLast(packet);
        notifyAll();
        return true;
    }

    synchronized byte[] take() throws InterruptedException {
        while (packets.isEmpty()) wait();
        return packets.removeFirst();
    }

    synchronized byte[] poll(long timeout,TimeUnit unit) throws InterruptedException {
        long remaining=unit.toNanos(timeout),deadline=System.nanoTime()+remaining;
        while (packets.isEmpty()) {
            if (remaining<=0) return null;
            TimeUnit.NANOSECONDS.timedWait(this,remaining);
            remaining=deadline-System.nanoTime();
        }
        return packets.removeFirst();
    }

    synchronized void clear() { packets.clear(); }
}

