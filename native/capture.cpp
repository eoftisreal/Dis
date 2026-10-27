#define DIS_CODEC_BUILD
#include "encoder.h"
#include <windows.h>
#include <d3d11.h>
#include <d3d11_4.h>
#include <d3dcompiler.h>
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
    ComPtr<ID3D11Texture2D> sampleTexture, scaledTexture;
    ComPtr<ID3D11ShaderResourceView> sourceView;
    ComPtr<ID3D11RenderTargetView> targetView;
    ComPtr<ID3D11VertexShader> vertexShader;
    ComPtr<ID3D11PixelShader> pixelShader;
    ComPtr<ID3D11SamplerState> sampler;
    int sampleWidth = 0, sampleHeight = 0;
    IDirect3DDevice surfaceDevice{nullptr};
    GraphicsCaptureItem item{nullptr};
    Direct3D11CaptureFramePool pool{nullptr};
    GraphicsCaptureSession session{nullptr};
    int stagingWidth = 0, stagingHeight = 0;
public:
    void Scale(ID3D11Texture2D* texture, int width, int height) {
        if (!vertexShader) {
            const char* shader = R"(
                Texture2D image : register(t0); SamplerState linearClamp : register(s0);
                struct Vertex { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
                Vertex vs(uint id : SV_VertexID) {
                    Vertex v; v.uv = float2((id << 1) & 2, id & 2);
                    v.position = float4(v.uv * float2(2, -2) + float2(-1, 1), 0, 1); return v;
                }
                float4 ps(Vertex v) : SV_TARGET { return image.Sample(linearClamp, v.uv); }
            )";
            ComPtr<ID3DBlob> vs, ps;
            winrt::check_hresult(D3DCompile(shader, std::strlen(shader), nullptr, nullptr, nullptr, "vs", "vs_4_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &vs, nullptr));
            winrt::check_hresult(D3DCompile(shader, std::strlen(shader), nullptr, nullptr, nullptr, "ps", "ps_4_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &ps, nullptr));
            winrt::check_hresult(device->CreateVertexShader(vs->GetBufferPointer(), vs->GetBufferSize(), nullptr, &vertexShader));
            winrt::check_hresult(device->CreatePixelShader(ps->GetBufferPointer(), ps->GetBufferSize(), nullptr, &pixelShader));
            D3D11_SAMPLER_DESC settings{}; settings.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
            settings.AddressU = settings.AddressV = settings.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
            settings.MaxLOD = D3D11_FLOAT32_MAX;
            winrt::check_hresult(device->CreateSamplerState(&settings, &sampler));
        }
        D3D11_TEXTURE2D_DESC input{}; texture->GetDesc(&input);
        if (!sampleTexture || sampleWidth != static_cast<int>(input.Width) || sampleHeight != static_cast<int>(input.Height)) {
            sourceView.Reset(); sampleTexture.Reset();
            input.Usage = D3D11_USAGE_DEFAULT; input.BindFlags = D3D11_BIND_SHADER_RESOURCE;
            input.CPUAccessFlags = 0; input.MiscFlags = 0; input.ArraySize = 1; input.MipLevels = 1;
            winrt::check_hresult(device->CreateTexture2D(&input, nullptr, &sampleTexture));
            winrt::check_hresult(device->CreateShaderResourceView(sampleTexture.Get(), nullptr, &sourceView));
            sampleWidth = input.Width; sampleHeight = input.Height;
        }
        if (!scaledTexture || stagingWidth != width || stagingHeight != height) {
            targetView.Reset(); scaledTexture.Reset(); staging.Reset();
            D3D11_TEXTURE2D_DESC output{}; output.Width = width; output.Height = height;
            output.MipLevels = output.ArraySize = output.SampleDesc.Count = 1;
            output.Format = DXGI_FORMAT_B8G8R8A8_UNORM; output.Usage = D3D11_USAGE_DEFAULT; output.BindFlags = D3D11_BIND_RENDER_TARGET;
            winrt::check_hresult(device->CreateTexture2D(&output, nullptr, &scaledTexture));
            winrt::check_hresult(device->CreateRenderTargetView(scaledTexture.Get(), nullptr, &targetView));
        }
        context->CopyResource(sampleTexture.Get(), texture);
        D3D11_VIEWPORT viewport{0, 0, static_cast<float>(width), static_cast<float>(height), 0, 1};
        context->RSSetViewports(1, &viewport);
        ID3D11RenderTargetView* target = targetView.Get(); context->OMSetRenderTargets(1, &target, nullptr);
        context->IASetInputLayout(nullptr); context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vertexShader.Get(), nullptr, 0); context->PSSetShader(pixelShader.Get(), nullptr, 0);
        ID3D11ShaderResourceView* source = sourceView.Get(); context->PSSetShaderResources(0, 1, &source);
        ID3D11SamplerState* linear = sampler.Get(); context->PSSetSamplers(0, 1, &linear);
        context->Draw(3, 0);
        ID3D11ShaderResourceView* unbound = nullptr; context->PSSetShaderResources(0, 1, &unbound);
        context->OMSetRenderTargets(0, nullptr, nullptr);
    }
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
        if (!destination || width < 1 || height < 1 || width > 16384 || height > 16384 || stride < width*4) return E_INVALIDARG;
        auto frame = pool.TryGetNextFrame();
        if (!frame) return S_FALSE;
        // Discard only raw captures, before encoding. The newest compositor surface keeps pointer feedback current.
        for (;;) { auto next = pool.TryGetNextFrame(); if (!next) break; frame.Close(); frame = next; }
        auto size = frame.ContentSize();
        if (size.Width != item.Size().Width || size.Height != item.Size().Height) { frame.Close(); return HRESULT_FROM_WIN32(ERROR_RETRY); }
        auto access = frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
        ComPtr<ID3D11Texture2D> texture; winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&texture)));
        D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc);
        // Resize on the GPU before readback: 1080p -> 720p transfers 56% fewer pixels
        // and avoids a full-desktop GDI+ resize on every encoded frame.
        if (static_cast<int>(desc.Width) != width || static_cast<int>(desc.Height) != height) {
            Scale(texture.Get(), width, height); texture = scaledTexture; texture->GetDesc(&desc);
        }
        if (!staging || stagingWidth != width || stagingHeight != height) {
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
