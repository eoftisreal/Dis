#include "encoder.h"
#define NOMINMAX
#include <windows.h>
#include <mmsystem.h>
#include <chrono>
#include <thread>
#include <vector>
#include <iostream>
#include <algorithm>
#include <string>
#include <fstream>

// Isolate the encoder from capture, USB, and Android at the host's 60 Hz call rate.
int main(int argc, char** argv) {
    constexpr int width = 1280, height = 720, fps = 60, calls = 360, warmup = 60;
    bool hardware = !(argc > 1 && std::string(argv[1]) == "--software");
    void* encoder = nullptr;
    int hr = dis_encoder_create(width, height, fps, 4608000, hardware ? 1 : 0, &encoder);
    if (hr < 0) { std::cerr << "Encoder init HRESULT: " << std::hex << hr << '\n'; return 1; }
    std::vector<uint8_t> pixels(width * height * 4, 255);
    std::ofstream bitstream;
    if (argc > 2) {
        bitstream.open(argv[2], std::ios::binary);
        if (!bitstream) { dis_encoder_destroy(encoder); return 3; }
    }
    for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) {
        auto p = &pixels[(y * width + x) * 4]; p[0] = x % 256; p[1] = y % 256; p[2] = (x + y) % 256;
    }
    timeBeginPeriod(1);
    using clock = std::chrono::steady_clock;
    auto start = clock::now(), measuredStart = start;
    int frames = 0, empty = 0, totalFrames = 0;
    int64_t previous = -1;
    double milliseconds = 0, longest = 0;
    for (int i = 0; i < calls; ++i) {
        if (i == warmup) measuredStart = clock::now();
        std::fill_n(pixels.begin() + ((i * 7) % height) * width * 4, width * 4, static_cast<uint8_t>(i));
        const uint8_t* data = nullptr; int size = 0, key = 0; int64_t pts = 0;
        auto before = clock::now();
        hr = dis_encoder_encode(encoder, pixels.data(), width * 4, int64_t(i) * 1000000 / fps, &data, &size, &pts, &key);
        double elapsed = std::chrono::duration<double, std::milli>(clock::now() - before).count();
        if (hr < 0 || (size && pts <= previous)) {
            std::cerr << "Encode failure or non-increasing timestamp: " << std::hex << hr << '\n';
            dis_encoder_destroy(encoder); timeEndPeriod(1); return 2;
        }
        if (size) {
            previous = pts;
            if (bitstream.is_open()) {
                if (!totalFrames) {
                    const uint8_t* config = nullptr; int length = 0;
                    dis_encoder_config(encoder, &config, &length);
                    if (length && config[0] == 0) bitstream.write(reinterpret_cast<const char*>(config), length);
                }
                bitstream.write(reinterpret_cast<const char*>(data), size);
            }
            ++totalFrames;
        }
        if (i >= warmup) { frames += size > 0; empty += size == 0; milliseconds += elapsed; longest = std::max(longest, elapsed); }
        std::this_thread::sleep_until(start + std::chrono::microseconds(int64_t(i + 1) * 1000000 / fps));
    }
    double seconds = std::chrono::duration<double>(clock::now() - measuredStart).count();
    std::cout << "{\"hardware\":" << (dis_encoder_hardware(encoder) ? "true" : "false")
        << ",\"width\":" << width << ",\"height\":" << height << ",\"targetFps\":" << fps
        << ",\"frames\":" << frames << ",\"totalFrames\":" << totalFrames << ",\"emptyCalls\":" << empty << ",\"seconds\":" << seconds
        << ",\"encoderFps\":" << frames / seconds << ",\"meanCallMs\":" << milliseconds / (calls - warmup)
        << ",\"maxCallMs\":" << longest << "}\n";
    dis_encoder_destroy(encoder); timeEndPeriod(1); return 0;
}
