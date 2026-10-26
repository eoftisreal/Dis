#define DIS_CODEC_BUILD
#include "encoder.h"
#include <windows.h>
#include <d3d11.h>
#include <d3d11_4.h>
#include <dxgi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <wrl/client.h>
#include <memory>
#include <cstring>

using Microsoft::WRL::ComPtr;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice;

class Capture {
    bool apartment = false;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11Texture2D> staging;
    IDirect3DDevice surfaceDevice{nullptr};
    GraphicsCaptureItem item{nullptr};
    Direct3D11CaptureFramePool pool{nullptr};
    GraphicsCaptureSession session{nullptr};
    int stagingWidth = 0, stagingHeight = 0;
public:
    ~Capture() {
        try { if (session) session.Close(); } catch (...) { }
        try { if (pool) pool.Close(); } catch (...) { }
        staging.Reset(); context.Reset(); device.Reset();
        session = nullptr; pool = nullptr; item = nullptr; surfaceDevice = nullptr;
        if (apartment) winrt::uninit_apartment();
    }
    void Initialize(int x, int y) {
        winrt::init_apartment(winrt::apartment_type::multi_threaded); apartment = true;
        if (!GraphicsCaptureSession::IsSupported()) winrt::throw_hresult(E_NOTIMPL);
        HMONITOR monitor = MonitorFromPoint(POINT{x,y}, MONITOR_DEFAULTTONULL);
        if (!monitor) winrt::throw_hresult(E_INVALIDARG);
        auto interop = winrt::get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
        winrt::check_hresult(interop->CreateForMonitor(monitor, winrt::guid_of<IGraphicsCaptureItem>(), winrt::put_abi(item)));
        HRESULT hr = D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);
        if (FAILED(hr)) hr = D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);
        winrt::check_hresult(hr);
        ComPtr<ID3D11Multithread> protectedContext;
        if (SUCCEEDED(context.As(&protectedContext))) protectedContext->SetMultithreadProtected(TRUE);
        ComPtr<IDXGIDevice> dxgi; winrt::check_hresult(device.As(&dxgi));
        ComPtr<IInspectable> inspectable;
        winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.Get(), &inspectable));
        winrt::check_hresult(inspectable->QueryInterface(winrt::guid_of<IDirect3DDevice>(), winrt::put_abi(surfaceDevice)));
        pool = Direct3D11CaptureFramePool::CreateFreeThreaded(surfaceDevice,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,item.Size());
        session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled(true);
        session.StartCapture();
    }
    HRESULT Copy(uint8_t* destination,int stride,int width,int height) {
        if (!destination || stride < width*4) return E_INVALIDARG;
        auto frame = pool.TryGetNextFrame();
        if (!frame) return S_FALSE;
        // Discard only raw captures, before encoding. The newest compositor surface keeps pointer feedback current.
        for (;;) { auto next = pool.TryGetNextFrame(); if (!next) break; frame.Close(); frame = next; }
        auto size = frame.ContentSize();
        if (size.Width != width || size.Height != height) { frame.Close(); return HRESULT_FROM_WIN32(ERROR_RETRY); }
        auto access = frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
        ComPtr<ID3D11Texture2D> texture; winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&texture)));
        D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc);
        if (!staging || stagingWidth != static_cast<int>(desc.Width) || stagingHeight != static_cast<int>(desc.Height)) {
            staging.Reset();
            desc.Usage=D3D11_USAGE_STAGING; desc.BindFlags=0; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ; desc.MiscFlags=0;
            desc.ArraySize=1; desc.MipLevels=1;
            winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&staging));
            stagingWidth=desc.Width; stagingHeight=desc.Height;
        }
        context->CopyResource(staging.Get(),texture.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        winrt::check_hresult(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped));
        for(int y=0;y<height;y++) std::memcpy(destination+int64_t(y)*stride,static_cast<uint8_t*>(mapped.pData)+uint64_t(y)*mapped.RowPitch,width*4);
        context->Unmap(staging.Get(),0); frame.Close(); return S_OK;
    }
};

DIS_API int __cdecl dis_capture_create(int x,int y,void** context) {
    if (!context) return E_POINTER; *context=nullptr;
    try { auto capture=std::make_unique<Capture>(); capture->Initialize(x,y); *context=capture.release(); return S_OK; }
    catch (const winrt::hresult_error& e) { return e.code(); }
    catch (...) { return E_FAIL; }
}
DIS_API int __cdecl dis_capture_copy(void* context,uint8_t* destination,int stride,int width,int height) {
    if (!context) return E_POINTER;
    try { return static_cast<Capture*>(context)->Copy(destination,stride,width,height); }
    catch (const winrt::hresult_error& e) { return e.code(); }
    catch (...) { return E_FAIL; }
}
DIS_API void __cdecl dis_capture_destroy(void* context) { delete static_cast<Capture*>(context); }
