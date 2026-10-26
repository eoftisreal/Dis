#include "encoder.h"
#include <vector>
#include <fstream>
#include <iostream>

int main(int argc, char** argv) {
    void* encoder = nullptr;
    int hr = dis_encoder_create(320,180,30,2000000,0,&encoder);
    if (hr < 0) { std::cerr << "Encoder init HRESULT: " << std::hex << hr << '\n'; return 1; }
    std::ofstream file(argc>1 ? argv[1] : "native-smoke.h264",std::ios::binary);
    std::vector<uint8_t> pixels(320*180*4);
    int frames = 0; int64_t previousPts = -1;
    for (int i = 0; i < 100 && frames < 24; ++i) {
        for(int y=0;y<180;y++) for(int x=0;x<320;x++) {
            auto p = &pixels[(y*320+x)*4]; p[0]=static_cast<uint8_t>(x+i*3); p[1]=static_cast<uint8_t>(y*2); p[2]=static_cast<uint8_t>(i*7); p[3]=255;
        }
        const uint8_t* data = nullptr; int size = 0, key = 0; int64_t pts = 0;
        hr = dis_encoder_encode(encoder,pixels.data(),320*4,int64_t(i)*33333,&data,&size,&pts,&key);
        if (hr < 0) { std::cerr << "Encode HRESULT: " << std::hex << hr << '\n'; dis_encoder_destroy(encoder); return 2; }
        if (size) {
            if (pts<=previousPts) { std::cerr << "Encoder did not preserve increasing capture timestamps\n"; dis_encoder_destroy(encoder); return 4; }
            previousPts=pts;
            if (!frames) {
                const uint8_t* config = nullptr; int length = 0;
                dis_encoder_config(encoder,&config,&length);
                if (length && config[0] == 0) file.write(reinterpret_cast<const char*>(config),length);
            }
            file.write(reinterpret_cast<const char*>(data),size); frames++;
        }
    }
    dis_encoder_destroy(encoder);
    std::cout << "Encoded " << frames << " real H.264 access units\n";
    return frames==24 ? 0 : 3;
}
