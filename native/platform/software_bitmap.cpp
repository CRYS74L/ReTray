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
    if (!source)
        return result;
    try {
        using namespace winrt::Windows::Graphics::Imaging;
        winrt::Windows::Foundation::IInspectable inspectable;
        winrt::copy_from_abi(inspectable, source);
        auto bitmap = inspectable.try_as<SoftwareBitmap>();
        if (!bitmap) {
            result.outcome.errorCode = E_NOINTERFACE;
            return result;
        }
        int width = bitmap.PixelWidth(), height = bitmap.PixelHeight();
        if (width <= 0 || height <= 0 || width > 256 || height > 256) {
            result.outcome.errorCode = E_INVALIDARG;
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
        result.frame.pixels.resize(static_cast<size_t>(width) * height);
        bool transparent = true;
        uint64_t hash = 1469598103934665603ULL;
        for (int y = 0; y < height; ++y) {
            const int64_t position =
                static_cast<int64_t>(plane.StartIndex) + static_cast<int64_t>(y) * plane.Stride;
            if (position < 0 || position + static_cast<int64_t>(width) * 4 > capacity) {
                result.outcome.errorCode = E_BOUNDS;
                return result;
            }
            memcpy(result.frame.pixels.data() + static_cast<size_t>(y) * width, bytes + position,
                   static_cast<size_t>(width) * 4);
        }
        for (auto pixel : result.frame.pixels) {
            if (pixel >> 24)
                transparent = false;
            hash ^= pixel;
            hash *= 1099511628211ULL;
        }
        reference.Close();
        locked.Close();
        result.frame.width = width;
        result.frame.height = height;
        result.frame.fingerprint = hash;
        result.frame.transparent = transparent;
        result.frame.origin = FrameOrigin::SourceLive;
        result.isLive = true;
        result.outcome.success = true;
        result.outcome.detail.clear();
    } catch (const winrt::hresult_error& error) {
        result.outcome.errorCode = static_cast<uint32_t>(error.code().value);
    } catch (...) {
        result.outcome.errorCode = E_FAIL;
    }
    return result;
}
} // namespace tray
