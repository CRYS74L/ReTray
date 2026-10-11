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
struct Frame {
    uint32_t width{}, height{};
    uint64_t fingerprint{}, revision{};
    bool transparent{};
    FrameOrigin origin{FrameOrigin::Unknown};
    std::vector<uint32_t> pixels;
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
