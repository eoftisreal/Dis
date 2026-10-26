#pragma once
#include <stdint.h>
#ifdef DIS_CODEC_BUILD
#define DIS_API extern "C" __declspec(dllexport)
#else
#define DIS_API extern "C" __declspec(dllimport)
#endif
// All calls for a context occur on the same MTA worker thread. Output belongs to the context until its next call.
DIS_API int __cdecl dis_encoder_create(int width, int height, int fps, int bitrate, int preferHardware, void** context);
DIS_API int __cdecl dis_encoder_encode(void* context, const uint8_t* bgra, int stride, int64_t ptsUs,
    const uint8_t** output, int* bytes, int64_t* outputPtsUs, int* keyframe);
DIS_API int __cdecl dis_encoder_config(void* context, const uint8_t** output, int* bytes);
DIS_API int __cdecl dis_encoder_hardware(void* context);
DIS_API void __cdecl dis_encoder_destroy(void* context);
