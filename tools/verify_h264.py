"""Independently decode the native Media Foundation smoke stream using PyAV/libavcodec."""
import sys
import av

with av.open(sys.argv[1]) as source:
    count = 0
    for picture in source.decode(video=0):
        if (picture.width, picture.height) != (320, 180):
            raise SystemExit("Unexpected decoded picture geometry")
        count += 1
if count != 24:
    raise SystemExit(f"Expected 24 independently decoded pictures, got {count}")
print(f"Decoded all {count} native H.264 pictures with libavcodec")
