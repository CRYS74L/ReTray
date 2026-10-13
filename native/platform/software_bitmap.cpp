#include "software_bitmap.h"
#include "bitmap_bytes.h"
#include <algorithm>
#include <cstring>
#include <winrt/windows.foundation.h>
#include <winrt/windows.graphics.imaging.h>
namespace tray {
FrameResult RasterizeSoftwareBitmap(void* source) {
    FrameResult result;
    result.outcome.detail = "Current SoftwareBitmap is not available";
    if (!source) {
        result.frame.readState = FrameReadState::Absent;
        result.frame.origin = FrameOrigin::SourceLive;
        result.frame.transparent = true;
        result.isLive = true;
        result.outcome.success = true;
        result.outcome.detail.clear();
        return result;
    }
    result.frame.readState = FrameReadState::DecodeError;
    try {
        using namespace winrt::Windows::Graphics::Imaging;
        winrt::Windows::Foundation::IInspectable inspectable;
        winrt::copy_from_abi(inspectable, source);
        try {
            result.frame.runtimeType = winrt::get_class_name(inspectable).c_str();
        } catch (const winrt::hresult_error&) {
            // Runtime type is diagnostic metadata, not a requirement for decoding.
        }
        auto bitmap = inspectable.try_as<SoftwareBitmap>();
        if (!bitmap) {
            result.outcome.errorCode = E_NOINTERFACE;
            result.frame.readError = result.outcome.errorCode;
            return result;
        }
        int width = bitmap.PixelWidth(), height = bitmap.PixelHeight();
        result.frame.nativeWidth = std::max(0, width);
        result.frame.nativeHeight = std::max(0, height);
        result.frame.nativePixelFormat = static_cast<uint32_t>(bitmap.BitmapPixelFormat());
        result.frame.nativeAlphaMode = static_cast<uint32_t>(bitmap.BitmapAlphaMode());
        if (width <= 0 || height <= 0 || static_cast<uint32_t>(width) > MaximumSourceDimension ||
            static_cast<uint32_t>(height) > MaximumSourceDimension ||
            static_cast<uint64_t>(width) * height > MaximumSourcePixels) {
            result.outcome.errorCode = E_INVALIDARG;
            result.frame.readError = result.outcome.errorCode;
            result.outcome.detail = "Native SoftwareBitmap dimensions exceed the supported limit";
            return result;
        }
        if (bitmap.BitmapPixelFormat() != BitmapPixelFormat::Bgra8 ||
            bitmap.BitmapAlphaMode() != BitmapAlphaMode::Straight)
            bitmap = SoftwareBitmap::Convert(bitmap, BitmapPixelFormat::Bgra8,
                                             BitmapAlphaMode::Straight);
        auto locked = bitmap.LockBuffer(BitmapBufferAccessMode::Read);
        auto reference = locked.CreateReference();
        auto access = reference.as<BitmapBytes>();
        uint8_t* bytes{};
        uint32_t capacity{};
        winrt::check_hresult(access->GetBuffer(&bytes, &capacity));
        auto plane = locked.GetPlaneDescription(0);
        const uint32_t rasterWidth = std::min(64, width), rasterHeight = std::min(64, height);
        result.frame.pixels.resize(static_cast<size_t>(rasterWidth) * rasterHeight);
        bool transparent = true;
        uint64_t hash = 1469598103934665603ULL;
        uint32_t previewY{};
        for (int y = 0; y < height; ++y) {
            const int64_t position =
                static_cast<int64_t>(plane.StartIndex) + static_cast<int64_t>(y) * plane.Stride;
            if (position < 0 || position + static_cast<int64_t>(width) * 4 > capacity) {
                result.outcome.errorCode = E_BOUNDS;
                result.frame.readError = result.outcome.errorCode;
                result.frame.pixels.clear();
                return result;
            }
            // Hash every native pixel, including pixels omitted from the bounded preview.
            for (int x = 0; x < width; ++x) {
                uint32_t pixel{};
                memcpy(&pixel, bytes + position + static_cast<size_t>(x) * 4, 4);
                if (pixel >> 24)
                    transparent = false;
                hash ^= pixel;
                hash *= 1099511628211ULL;
            }
            if (previewY < rasterHeight &&
                y == static_cast<int>(static_cast<uint64_t>(previewY) * height / rasterHeight)) {
                for (uint32_t x = 0; x < rasterWidth; ++x) {
                    const size_t nativeX = static_cast<uint64_t>(x) * width / rasterWidth;
                    memcpy(&result.frame.pixels[static_cast<size_t>(previewY) * rasterWidth + x],
                           bytes + position + nativeX * 4, 4);
                }
                ++previewY;
            }
        }
        reference.Close();
        locked.Close();
        result.frame.width = width;
        result.frame.height = height;
        result.frame.rasterWidth = rasterWidth;
        result.frame.rasterHeight = rasterHeight;
        result.frame.fingerprint = hash;
        result.frame.transparent = transparent;
        result.frame.origin = FrameOrigin::SourceLive;
        result.frame.readState = FrameReadState::Ready;
        result.isLive = true;
        result.outcome.success = true;
        result.outcome.detail.clear();
    } catch (const winrt::hresult_error& error) {
        result.outcome.errorCode = static_cast<uint32_t>(error.code().value);
    } catch (...) {
        result.outcome.errorCode = E_FAIL;
    }
    result.frame.readError = result.outcome.errorCode;
    return result;
}
} // namespace tray
