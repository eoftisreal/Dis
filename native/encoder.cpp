#define DIS_CODEC_BUILD
#include "encoder.h"
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mftransform.h>
#include <mferror.h>
#include <codecapi.h>
#include <wmcodecdsp.h>
#include <avrt.h>
#include <wrl/client.h>
#include <vector>
#include <algorithm>
#include <memory>

using Microsoft::WRL::ComPtr;
#define CHECK_HR(expr) do { HRESULT checkHr = (expr); if (FAILED(checkHr)) return checkHr; } while (false)

class Encoder {
public:
    int width = 0, height = 0, fps = 0, bitrate = 0;
    bool hardware = false, asynchronous = false, comInitialized = false, mfStarted = false;
    HANDLE mmcss = nullptr;
    DWORD inputId = 0, outputId = 0;
    unsigned inputReady = 0, outputReady = 0, pending = 0;
    ComPtr<IMFTransform> transform;
    ComPtr<IMFMediaEventGenerator> events;
    std::vector<uint8_t> result, configuration;

    ~Encoder() {
        Reset();
        if (mfStarted) MFShutdown();
        if (mmcss) AvRevertMmThreadCharacteristics(mmcss);
        if (comInitialized) CoUninitialize();
    }
    void Reset() {
        if (transform) {
            transform->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
            ComPtr<IMFShutdown> shutdown;
            if (SUCCEEDED(transform.As(&shutdown))) shutdown->Shutdown();
        }
        events.Reset(); transform.Reset(); inputReady = outputReady = pending = 0;
    }
    void CodecValue(const GUID& property, ULONG value, bool boolean = false) {
        ComPtr<ICodecAPI> codec;
        if (SUCCEEDED(transform.As(&codec))) {
            VARIANT v; VariantInit(&v);
            if (boolean) { v.vt = VT_BOOL; v.boolVal = value ? VARIANT_TRUE : VARIANT_FALSE; }
            else { v.vt = VT_UI4; v.ulVal = value; }
            codec->SetValue(&property, &v);
        }
    }
    HRESULT Configure() {
        ComPtr<IMFAttributes> attributes;
        UINT32 async = 0;
        if (SUCCEEDED(transform->GetAttributes(&attributes))) {
            attributes->GetUINT32(MF_TRANSFORM_ASYNC, &async);
            attributes->SetUINT32(MF_LOW_LATENCY, TRUE);
            if (async) CHECK_HR(attributes->SetUINT32(MF_TRANSFORM_ASYNC_UNLOCK, TRUE));
        }
        asynchronous = async != 0;
        if (asynchronous) CHECK_HR(transform.As(&events));
        HRESULT ids = transform->GetStreamIDs(1, &inputId, 1, &outputId);
        if (FAILED(ids) && ids != E_NOTIMPL) return ids;
        if (ids == E_NOTIMPL) inputId = outputId = 0;
        CodecValue(CODECAPI_AVLowLatencyMode, 1, true);
        CodecValue(CODECAPI_AVEncCommonRateControlMode, eAVEncCommonRateControlMode_CBR);
        CodecValue(CODECAPI_AVEncCommonMeanBitRate, bitrate);
        CodecValue(CODECAPI_AVEncMPVDefaultBPictureCount, 0);
        CodecValue(CODECAPI_AVEncMPVGOPSize, fps);

        ComPtr<IMFMediaType> output;
        CHECK_HR(MFCreateMediaType(&output));
        CHECK_HR(output->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
        CHECK_HR(output->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264));
        CHECK_HR(output->SetUINT32(MF_MT_AVG_BITRATE, bitrate));
        CHECK_HR(output->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
        CHECK_HR(output->SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_Base));
        CHECK_HR(MFSetAttributeSize(output.Get(), MF_MT_FRAME_SIZE, width, height));
        CHECK_HR(MFSetAttributeRatio(output.Get(), MF_MT_FRAME_RATE, fps, 1));
        MFSetAttributeRatio(output.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
        CHECK_HR(transform->SetOutputType(outputId, output.Get(), 0));

        ComPtr<IMFMediaType> input;
        CHECK_HR(MFCreateMediaType(&input));
        input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        input->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
        input->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
        input->SetUINT32(MF_MT_DEFAULT_STRIDE, width);
        input->SetUINT32(MF_MT_VIDEO_PRIMARIES, MFVideoPrimaries_BT709);
        input->SetUINT32(MF_MT_TRANSFER_FUNCTION, MFVideoTransFunc_709);
        input->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT709);
        input->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235);
        MFSetAttributeSize(input.Get(), MF_MT_FRAME_SIZE, width, height);
        MFSetAttributeRatio(input.Get(), MF_MT_FRAME_RATE, fps, 1);
        MFSetAttributeRatio(input.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
        CHECK_HR(transform->SetInputType(inputId, input.Get(), 0));
        UpdateConfig();
        CHECK_HR(transform->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0));
        CHECK_HR(transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0));
        return S_OK;
    }
    HRESULT Enumerate(bool wantHardware) {
        IMFActivate** activations = nullptr;
        UINT32 count = 0;
        MFT_REGISTER_TYPE_INFO input{MFMediaType_Video, MFVideoFormat_NV12};
        MFT_REGISTER_TYPE_INFO output{MFMediaType_Video, MFVideoFormat_H264};
        UINT32 flags = MFT_ENUM_FLAG_SORTANDFILTER | (wantHardware ? MFT_ENUM_FLAG_HARDWARE : MFT_ENUM_FLAG_SYNCMFT);
        HRESULT hr = MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, flags, &input, &output, &activations, &count);
        if (FAILED(hr)) return hr;
        HRESULT last = MF_E_TOPO_CODEC_NOT_FOUND;
        for (UINT32 i = 0; i < count; ++i) {
            if (FAILED(last) && SUCCEEDED(activations[i]->ActivateObject(IID_PPV_ARGS(&transform)))) {
                hardware = wantHardware;
                last = Configure();
                if (FAILED(last)) { Reset(); activations[i]->ShutdownObject(); }
            }
            activations[i]->Release();
        }
        CoTaskMemFree(activations);
        return last;
    }
    HRESULT Initialize(int w, int h, int rate, int bits, bool preferHardware) {
        if (w < 2 || h < 2 || w > 1920 || h > 1920 || w % 2 || h % 2 || int64_t(w)*h > 1920*1080 || rate < 1 || rate > 60)
            return E_INVALIDARG;
        width = w; height = h; fps = rate; bitrate = bits;
        HRESULT co = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (FAILED(co)) return co;
        comInitialized = true;
        CHECK_HR(MFStartup(MF_VERSION, MFSTARTUP_FULL)); mfStarted = true;
        DWORD taskIndex = 0; mmcss = AvSetMmThreadCharacteristicsW(L"Playback", &taskIndex);
        HRESULT hr = preferHardware ? Enumerate(true) : E_FAIL;
        if (FAILED(hr)) hr = Enumerate(false);
        return hr;
    }
    void UpdateConfig() {
        ComPtr<IMFMediaType> type;
        if (SUCCEEDED(transform->GetOutputCurrentType(outputId, &type))) {
            UINT32 size = 0;
            if (SUCCEEDED(type->GetBlobSize(MF_MT_MPEG_SEQUENCE_HEADER, &size)) && size <= 65536) {
                configuration.resize(size);
                type->GetBlob(MF_MT_MPEG_SEQUENCE_HEADER, configuration.data(), size, &size);
            }
        }
    }
    HRESULT Pump() {
        if (!asynchronous) return S_OK;
        for (;;) {
            ComPtr<IMFMediaEvent> event;
            HRESULT hr = events->GetEvent(MF_EVENT_FLAG_NO_WAIT, &event);
            if (hr == MF_E_NO_EVENTS_AVAILABLE) return S_OK;
            if (FAILED(hr)) return hr;
            HRESULT status; event->GetStatus(&status); if (FAILED(status)) return status;
            MediaEventType type; event->GetType(&type);
            if (type == METransformNeedInput) ++inputReady;
            if (type == METransformHaveOutput) ++outputReady;
        }
    }
    static uint8_t Clip(int n) { return static_cast<uint8_t>(std::clamp(n, 0, 255)); }
    HRESULT Input(const uint8_t* bgra, int stride, int64_t pts) {
        ComPtr<IMFMediaBuffer> buffer;
        DWORD bytes = width*height*3/2;
        CHECK_HR(MFCreateAlignedMemoryBuffer(bytes, MF_16_BYTE_ALIGNMENT, &buffer));
        BYTE* nv12 = nullptr;
        CHECK_HR(buffer->Lock(&nv12, nullptr, nullptr));
        for (int y = 0; y < height; ++y) {
            const uint8_t* row = bgra + int64_t(y)*stride;
            for (int x = 0; x < width; ++x) {
                int b = row[x*4], g = row[x*4+1], r = row[x*4+2];
                nv12[y*width+x] = Clip(16 + ((47*r+157*g+16*b+128)>>8));
            }
        }
        uint8_t* uv = nv12 + width*height;
        for (int y = 0; y < height; y += 2) {
            const uint8_t* a = bgra + int64_t(y)*stride;
            const uint8_t* b = a + stride;
            for (int x = 0; x < width; x += 2) {
                int red=(a[x*4+2]+a[x*4+6]+b[x*4+2]+b[x*4+6]+2)/4;
                int green=(a[x*4+1]+a[x*4+5]+b[x*4+1]+b[x*4+5]+2)/4;
                int blue=(a[x*4]+a[x*4+4]+b[x*4]+b[x*4+4]+2)/4;
                uv[(y/2)*width+x] = Clip(128 + ((-26*red-87*green+112*blue+128)>>8));
                uv[(y/2)*width+x+1] = Clip(128 + ((112*red-102*green-10*blue+128)>>8));
            }
        }
        buffer->Unlock(); buffer->SetCurrentLength(bytes);
        ComPtr<IMFSample> sample;
        CHECK_HR(MFCreateSample(&sample));
        sample->AddBuffer(buffer.Get()); sample->SetSampleTime(pts*10); sample->SetSampleDuration(10000000/fps);
        HRESULT hr = transform->ProcessInput(inputId, sample.Get(), 0);
        if (SUCCEEDED(hr)) { ++pending; if (asynchronous) --inputReady; }
        return hr;
    }
    HRESULT Output(int64_t* pts, int* key) {
        if (asynchronous && outputReady == 0) return S_FALSE;
        MFT_OUTPUT_STREAM_INFO info{};
        CHECK_HR(transform->GetOutputStreamInfo(outputId, &info));
        ComPtr<IMFSample> allocated;
        if (!(info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES)) {
            ComPtr<IMFMediaBuffer> buffer;
            CHECK_HR(MFCreateSample(&allocated));
            CHECK_HR(MFCreateMemoryBuffer(std::max<DWORD>(info.cbSize, width*height*4), &buffer));
            allocated->AddBuffer(buffer.Get());
        }
        MFT_OUTPUT_DATA_BUFFER output{}; output.dwStreamID = outputId; output.pSample = allocated.Get();
        DWORD status = 0;
        HRESULT hr = transform->ProcessOutput(0, 1, &output, &status);
        if (output.pEvents) output.pEvents->Release();
        ComPtr<IMFSample> provided;
        if (!allocated && output.pSample) provided.Attach(output.pSample);
        if (asynchronous) --outputReady;
        if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT) return S_FALSE;
        if (hr == MF_E_TRANSFORM_STREAM_CHANGE) {
            ComPtr<IMFMediaType> type;
            CHECK_HR(transform->GetOutputAvailableType(outputId, 0, &type));
            CHECK_HR(transform->SetOutputType(outputId, type.Get(), 0)); UpdateConfig(); return S_FALSE;
        }
        if (FAILED(hr)) return hr;
        IMFSample* sample = allocated ? allocated.Get() : provided.Get();
        if (!sample) return E_UNEXPECTED;
        ComPtr<IMFMediaBuffer> buffer;
        CHECK_HR(sample->ConvertToContiguousBuffer(&buffer));
        BYTE* data = nullptr; DWORD size = 0;
        CHECK_HR(buffer->Lock(&data, nullptr, &size));
        if (size > 8*1024*1024) { buffer->Unlock(); return E_OUTOFMEMORY; }
        result.assign(data, data+size); buffer->Unlock();
        LONGLONG time = 0; sample->GetSampleTime(&time); *pts = time/10;
        UINT32 clean = 0; sample->GetUINT32(MFSampleExtension_CleanPoint, &clean); *key = clean != 0;
        if (pending) --pending;
        UpdateConfig(); return S_OK;
    }
    HRESULT Encode(const uint8_t* bgra, int stride, int64_t pts, int64_t* outputPts, int* key) {
        result.clear();
        if (!bgra || int64_t(abs(stride)) < int64_t(width)*4) return E_INVALIDARG;
        CHECK_HR(Pump());
        if (asynchronous && outputReady) return Output(outputPts, key);
        if (!asynchronous || (inputReady && pending < 4)) {
            HRESULT hr = Input(bgra, stride, pts);
            if (FAILED(hr) && hr != MF_E_NOTACCEPTING) return hr;
        }
        // Give an asynchronous GPU one small scheduling interval, not a complete frame round trip.
        ULONGLONG until = GetTickCount64() + 4;
        do {
            CHECK_HR(Pump());
            HRESULT hr = Output(outputPts, key);
            if (hr != S_FALSE) return hr;
            if (!asynchronous) break;
            Sleep(1);
        } while (GetTickCount64() < until);
        return S_FALSE;
    }
};

DIS_API int __cdecl dis_encoder_create(int w, int h, int fps, int bitrate, int preferHardware, void** context) {
    if (!context) return E_POINTER; *context = nullptr;
    try {
        auto encoder = std::make_unique<Encoder>();
        HRESULT hr = encoder->Initialize(w,h,fps,bitrate,preferHardware != 0);
        if (FAILED(hr)) return hr; *context = encoder.release(); return S_OK;
    } catch (...) { return E_OUTOFMEMORY; }
}
DIS_API int __cdecl dis_encoder_encode(void* context, const uint8_t* data, int stride, int64_t pts,
    const uint8_t** output, int* bytes, int64_t* outputPts, int* key) {
    if (!context || !output || !bytes || !outputPts || !key) return E_POINTER;
    *output = nullptr; *bytes = 0;
    try {
        auto& encoder = *static_cast<Encoder*>(context);
        HRESULT hr = encoder.Encode(data,stride,pts,outputPts,key);
        if (SUCCEEDED(hr) && !encoder.result.empty()) { *output = encoder.result.data(); *bytes = static_cast<int>(encoder.result.size()); }
        return hr;
    } catch (...) { return E_OUTOFMEMORY; }
}
DIS_API int __cdecl dis_encoder_config(void* context, const uint8_t** output, int* bytes) {
    if (!context || !output || !bytes) return E_POINTER;
    auto& value = static_cast<Encoder*>(context)->configuration;
    *output = value.data(); *bytes = static_cast<int>(value.size()); return 0;
}
DIS_API int __cdecl dis_encoder_hardware(void* context) { return context && static_cast<Encoder*>(context)->hardware; }
DIS_API void __cdecl dis_encoder_destroy(void* context) { delete static_cast<Encoder*>(context); }
