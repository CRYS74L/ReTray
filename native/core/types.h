#pragma once
#include <windows.h>
#include <array>
#include <cstdint>
#include <shellapi.h>
#include <string>
#include <vector>
#include <windows.h>

namespace tray {
struct ImageIdentity {
    GUID pdb{};
    uint32_t age{}, timestamp{}, imageSize{};
};
struct IconIdentity {
    uint64_t shellEpoch{}, ownerCreatedAt{}, ownerHwnd{};
    uint32_t ownerPid{}, uid{};
    bool hasGuid{};
    GUID guid{};
};
enum class Placement { Promoted, Overflow, Hidden };
enum class FrameOrigin { Unknown, CachedOriginal, SourceLive, Custom };
enum class FrameReadState : uint32_t { Unknown, Absent, Ready, DecodeError };
inline constexpr uint32_t MaximumSourceDimension = 4096;
inline constexpr uint64_t MaximumSourcePixels = 4 * 1024 * 1024;
struct Frame {
    uint32_t width{}, height{};
    // Native dimensions drive frame policy; raster storage is bounded independently.
    uint32_t rasterWidth{}, rasterHeight{};
    uint64_t fingerprint{}, revision{};
    bool transparent{};
    FrameOrigin origin{FrameOrigin::Unknown};
    FrameReadState readState{FrameReadState::Unknown};
    uint32_t readError{}, nativeWidth{}, nativeHeight{}, nativePixelFormat{}, nativeAlphaMode{};
    std::wstring runtimeType;
    std::vector<uint32_t> pixels;
    uint32_t PixelWidth() const { return rasterWidth ? rasterWidth : width; }
    uint32_t PixelHeight() const { return rasterHeight ? rasterHeight : height; }
};
struct OperationResult {
    bool success{};
    uint32_t errorCode{};
    std::string detail;
    uint64_t appliedRevision{};
};
struct FrameResult {
    OperationResult outcome;
    Frame frame;
    bool isLive{};
};
struct NativeIconRecord {
    IconIdentity identity;
    bool managed{};
    uint64_t registrationSerial{};
    uint32_t callbackMessage{}, notifyVersion{}, sourceState{}, nativeIndex{};
    Placement placement{Placement::Overflow};
    Frame cachedOriginal, currentFrame;
    std::wstring tip, executable, windowClass;
    uint64_t recordAddress{}, imageObject{}, cachedIcon{}, sourceRevision{};
};
struct SnapshotResult {
    OperationResult outcome;
    bool complete{};
    uint64_t shellEpoch{};
    std::vector<NativeIconRecord> records;
};
bool SameIcon(const IconIdentity&, const IconIdentity&);
std::string IdentityKey(const IconIdentity&);
bool ValidReadRange(uint64_t address, size_t length);
bool ValidVectorBounds(uint64_t begin, uint64_t end, size_t maximumCount = 4096);
bool SameImage(const ImageIdentity&, const ImageIdentity&);
FrameResult RasterizeIcon(HICON icon, FrameOrigin origin);
} // namespace tray
