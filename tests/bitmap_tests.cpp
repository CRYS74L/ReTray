#include "../native/shell/shared.h"
#include "../native/platform/bitmap_bytes.h"
#include <cstdio>
#include <stdexcept>
#include <inspectable.h>
#include <winrt/windows.foundation.h>
#include <winrt/windows.graphics.imaging.h>
tray::SnapshotResult bitmapFixture;
namespace tray {
SnapshotResult BitmapTestSnapshot(const SymbolAdapter&, uint32_t, uint64_t, bool = true) {
    return bitmapFixture;
}
}
#define ReadSnapshot BitmapTestSnapshot
#include "../native/shell/bridge_entry.cpp"
#undef ReadSnapshot
#define CHECK(x) do { if (!(x)) throw std::runtime_error(#x); } while (0)
using namespace winrt::Windows::Graphics::Imaging;
void Fill(const SoftwareBitmap& bitmap, uint32_t color) {
    auto buffer = bitmap.LockBuffer(BitmapBufferAccessMode::Write);
    auto reference = buffer.CreateReference();
    uint8_t* bytes{}; uint32_t capacity{};
    winrt::check_hresult(reference.as<BitmapBytes>()->GetBuffer(&bytes, &capacity));
    auto plane = buffer.GetPlaneDescription(0);
    for (int y = 0; y < bitmap.PixelHeight(); ++y)
        for (int x = 0; x < bitmap.PixelWidth(); ++x)
            reinterpret_cast<uint32_t*>(bytes + plane.StartIndex + y * plane.Stride)[x] = color;
}
IUnknown* getterBitmap{};
HRESULT getterResult{S_OK};
HRESULT __stdcall BitmapGetter(void*, void** result) {
    *result = getterBitmap;
    if (getterBitmap) getterBitmap->AddRef();
    return getterResult;
}
void MissingBitmapIsAnExplicitOffFrame() {
    auto result = RasterizeSoftwareBitmap(nullptr);
    CHECK(result.outcome.success);
    CHECK(result.frame.readState == FrameReadState::Absent);
    CHECK(result.frame.origin == FrameOrigin::SourceLive);
    CHECK(result.frame.transparent);
}
void RejectedDimensionsRemainDiagnosable() {
    SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, 8192, 1, BitmapAlphaMode::Straight);
    auto result = RasterizeSoftwareBitmap(winrt::get_abi(bitmap));
    CHECK(!result.outcome.success);
    CHECK(result.frame.nativeWidth == 8192 && result.frame.nativeHeight == 1);
    CHECK(result.frame.readState == FrameReadState::DecodeError);
    CHECK(result.frame.readError == static_cast<uint32_t>(E_INVALIDARG));
    CHECK(result.frame.runtimeType == L"Windows.Graphics.Imaging.SoftwareBitmap");
    CHECK(result.frame.origin == FrameOrigin::Unknown && !result.frame.transparent);
}
void LargeNativeBitmapIsDecodedWithBoundedStorage() {
    for (int size : {512, 1024, 2048}) {
        SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, size, size, BitmapAlphaMode::Premultiplied);
        Fill(bitmap, 0xff1955e9);
        auto result = RasterizeSoftwareBitmap(winrt::get_abi(bitmap));
        CHECK(result.outcome.success && result.isLive);
        CHECK(result.frame.width == static_cast<uint32_t>(size));
        CHECK(result.frame.height == static_cast<uint32_t>(size));
        CHECK(result.frame.pixels.size() == 4096 && result.frame.pixels[0] == 0xff1955e9);
        BridgeIcon row; WriteFrame(row, result.frame);
        CHECK(row.width == 64 && row.height == 64 && row.pixels[4095] == 0xff1955e9);
        FrameBaseline baseline{true, result.frame.fingerprint, static_cast<uint32_t>(size), static_cast<uint32_t>(size)};
        CHECK(ChooseFrame(result.frame, baseline, true) == FrameDecision::CustomIdle);
        baseline.width = 16; baseline.height = 16;
        CHECK(ChooseFrame(result.frame, baseline, true) == FrameDecision::NativeDynamic);
    }
}
void NativeFingerprintDetectsChangesOutsidePreviewSamples() {
    SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, 512, 512, BitmapAlphaMode::Straight);
    Fill(bitmap, 0xff1955e9);
    auto first = RasterizeSoftwareBitmap(winrt::get_abi(bitmap));
    CHECK(first.outcome.success);
    {
        auto buffer = bitmap.LockBuffer(BitmapBufferAccessMode::Write);
        auto reference = buffer.CreateReference();
        uint8_t* bytes{}; uint32_t capacity{};
        winrt::check_hresult(reference.as<BitmapBytes>()->GetBuffer(&bytes, &capacity));
        auto plane = buffer.GetPlaneDescription(0);
        reinterpret_cast<uint32_t*>(bytes + plane.StartIndex + plane.Stride)[1] = 0xffe95519;
    }
    auto changed = RasterizeSoftwareBitmap(winrt::get_abi(bitmap));
    CHECK(changed.outcome.success);
    CHECK(first.frame.pixels == changed.frame.pixels);
    CHECK(first.frame.fingerprint != changed.frame.fingerprint);
    FrameBaseline baseline{true, first.frame.fingerprint, 512, 512};
    CHECK(ChooseFrame(changed.frame, baseline, true) == FrameDecision::NativeDynamic);
}
void LargeTransparentSourceRemainsNativeOff() {
    SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, 1024, 1024, BitmapAlphaMode::Premultiplied);
    Fill(bitmap, 0);
    auto result = RasterizeSoftwareBitmap(winrt::get_abi(bitmap));
    CHECK(result.outcome.success && result.frame.transparent && result.isLive);
    CHECK(result.frame.pixels.size() == 4096);
    CHECK(ChooseFrame(result.frame, {true, result.frame.fingerprint, 1024, 1024}, true) == FrameDecision::NativeOff);
}
struct UnnamedBitmap : ::IInspectable {
    IUnknown* bitmap{};
    explicit UnnamedBitmap(IUnknown* value) : bitmap(value) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (IsEqualIID(iid, __uuidof(IUnknown)) || IsEqualIID(iid, __uuidof(::IInspectable))) {
            *value = this; AddRef(); return S_OK;
        }
        return bitmap->QueryInterface(iid, value);
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return 2; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE GetIids(ULONG*, IID**) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetRuntimeClassName(HSTRING*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetTrustLevel(TrustLevel* value) override { *value = BaseTrust; return S_OK; }
};
void OptionalRuntimeTypeDoesNotBlockDecoding() {
    SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, 16, 16, BitmapAlphaMode::Straight);
    Fill(bitmap, 0xff1955e9);
    UnnamedBitmap object(reinterpret_cast<IUnknown*>(winrt::get_abi(bitmap)));
    auto result = RasterizeSoftwareBitmap(&object);
    CHECK(result.outcome.success && result.frame.pixels[0] == 0xff1955e9);
}
void TransparentBitmapRemainsALiveOffFrame() {
    SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, 16, 16, BitmapAlphaMode::Straight);
    Fill(bitmap, 0);
    auto result = RasterizeSoftwareBitmap(winrt::get_abi(bitmap));
    CHECK(result.outcome.success && result.isLive);
    CHECK(result.frame.readState == FrameReadState::Ready && result.frame.transparent);
    CHECK(result.frame.width == 16 && result.frame.pixels.size() == 256);
}
void MembershipRefreshRecoversInitialRead(HRESULT initialResult) {
    entries.clear(); auto shared = std::make_unique<BridgeState>(); state = shared.get();
    state->epoch = 1; adapter = {}; moduleBase = reinterpret_cast<uint64_t>(BitmapGetter);
    uint64_t table[9]{}; table[8] = moduleBase;
    uint64_t nativeRecord[4]{}; nativeRecord[2] = reinterpret_cast<uint64_t>(table);
    NativeIconRecord record;
    record.identity = {1, 11, 100, 1000, 11, false, {}};
    record.recordAddress = reinterpret_cast<uint64_t>(nativeRecord);
    bitmapFixture = {}; bitmapFixture.complete = true; bitmapFixture.records = {record};
    getterResult = initialResult; getterBitmap = nullptr;
    Publish(false); CHECK(state->count == 1 && state->icons[0].width == 0);
    SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, 16, 16, BitmapAlphaMode::Straight);
    Fill(bitmap, 0xff1955e9); getterBitmap = reinterpret_cast<IUnknown*>(winrt::get_abi(bitmap));
    getterResult = S_OK; Publish(false);
    CHECK(state->icons[0].live && state->icons[0].width == 16);
    CHECK(state->icons[0].pixels[0] == 0xff1955e9);
    entries.clear(); state = nullptr; getterBitmap = nullptr;
}
void MembershipRefreshRecoversFailedInitialRead() { MembershipRefreshRecoversInitialRead(E_PENDING); }
void MembershipRefreshRecoversAbsentInitialRead() { MembershipRefreshRecoversInitialRead(S_OK); }
void NonBitmapIsADecodeErrorRatherThanOffFrame() {
    auto object = winrt::Windows::Foundation::PropertyValue::CreateString(L"unexpected image object");
    auto frame = SourceFrame(reinterpret_cast<IUnknown*>(winrt::get_abi(object)));
    CHECK(frame.readState == FrameReadState::DecodeError);
    CHECK(frame.readError == static_cast<uint32_t>(E_NOINTERFACE));
    CHECK(!frame.transparent && frame.origin == FrameOrigin::Unknown);
    CHECK(frame.pixels.empty());
}
int wmain() {
    winrt::init_apartment(); int failed{};
    for (const auto& test : {
        std::pair{"missing native bitmap is explicit live off frame", MissingBitmapIsAnExplicitOffFrame},
        std::pair{"rejected source keeps native dimensions and error", RejectedDimensionsRemainDiagnosable},
        std::pair{"large native dimensions decode into bounded preview storage", LargeNativeBitmapIsDecodedWithBoundedStorage},
        std::pair{"fingerprint detects unsampled native animation pixels", NativeFingerprintDetectsChangesOutsidePreviewSamples},
        std::pair{"large transparent bitmap preserves native blink off", LargeTransparentSourceRemainsNativeOff},
        std::pair{"optional runtime type does not prevent bitmap decoding", OptionalRuntimeTypeDoesNotBlockDecoding},
        std::pair{"transparent pixels preserve native off frame", TransparentBitmapRemainsALiveOffFrame},
        std::pair{"membership refresh recovers delayed native bitmap", MembershipRefreshRecoversFailedInitialRead},
        std::pair{"membership refresh recovers initially absent native bitmap", MembershipRefreshRecoversAbsentInitialRead},
        std::pair{"non-bitmap decoding error is different from native off frame", NonBitmapIsADecodeErrorRatherThanOffFrame}}) {
        try { test.second(); std::printf("PASS %s\n", test.first); }
        catch (const std::exception& error) { ++failed; std::fprintf(stderr, "FAIL %s: %s\n", test.first, error.what()); }
        entries.clear(); state = nullptr;
    }
    return failed ? 1 : 0;
}
