#include "types.h"
#include <algorithm>
#include <cstring>
#include <sstream>
#include <iomanip>
namespace tray {
bool SameIcon(const IconIdentity& a,const IconIdentity& b) {
    if(a.shellEpoch!=b.shellEpoch || a.ownerPid!=b.ownerPid || a.ownerCreatedAt!=b.ownerCreatedAt || a.hasGuid!=b.hasGuid) return false;
    return a.hasGuid ? IsEqualGUID(a.guid,b.guid) : a.ownerHwnd==b.ownerHwnd && a.uid==b.uid;
}
std::string IdentityKey(const IconIdentity& id) {
    std::ostringstream out; out<<std::hex<<id.shellEpoch<<':'<<id.ownerPid<<':'<<id.ownerCreatedAt<<':';
    if(id.hasGuid) {
        out<<'g'<<std::setfill('0')<<std::setw(8)<<id.guid.Data1<<std::setw(4)<<id.guid.Data2<<std::setw(4)<<id.guid.Data3;
        for(auto b:id.guid.Data4) out<<std::setw(2)<<static_cast<unsigned>(b);
    } else out<<'u'<<id.ownerHwnd<<':'<<id.uid;
    return out.str();
}
bool ValidReadRange(uint64_t address,size_t length) {
    constexpr uint64_t ceiling=0x0000800000000000ULL;
    return address>=0x10000 && address<ceiling && length<=65536 && length<ceiling-address;
}
bool ValidVectorBounds(uint64_t begin,uint64_t end,size_t maximumCount) {
    if(begin==0 && end==0) return true;
    return end>=begin && (end-begin)%8==0 && (end-begin)/8<=maximumCount && ValidReadRange(begin,static_cast<size_t>(end-begin));
}
bool SameImage(const ImageIdentity& a,const ImageIdentity& b) {
    return IsEqualGUID(a.pdb,b.pdb) && a.age==b.age && a.timestamp==b.timestamp && a.imageSize==b.imageSize;
}
FrameResult RasterizeIcon(HICON borrowed,FrameOrigin origin) {
    FrameResult result; result.outcome.detail="Invalid or unreadable icon";
    auto owned=CopyIcon(borrowed); if(!owned) { result.outcome.errorCode=GetLastError(); return result; }
    ICONINFO info{};
    if(!GetIconInfo(owned,&info)) { DestroyIcon(owned); return result; }
    BITMAP bitmap{}; GetObjectW(info.hbmColor?info.hbmColor:info.hbmMask,sizeof(bitmap),&bitmap);
    int width=bitmap.bmWidth,height=info.hbmColor?bitmap.bmHeight:bitmap.bmHeight/2;
    if(info.hbmColor) DeleteObject(info.hbmColor); if(info.hbmMask) DeleteObject(info.hbmMask);
    if(width<=0 || height<=0 || width>256 || height>256) { DestroyIcon(owned); return result; }
    const size_t count=static_cast<size_t>(width)*height;
    BITMAPINFO bmi{}; bmi.bmiHeader.biSize=sizeof(BITMAPINFOHEADER); bmi.bmiHeader.biWidth=width;
    bmi.bmiHeader.biHeight=-height; bmi.bmiHeader.biPlanes=1; bmi.bmiHeader.biBitCount=32; bmi.bmiHeader.biCompression=BI_RGB;
    HDC dc=CreateCompatibleDC(nullptr); void* bytes{};
    HBITMAP dib=CreateDIBSection(dc,&bmi,DIB_RGB_COLORS,&bytes,nullptr,0);
    if(!dc || !dib || !bytes) { if(dib) DeleteObject(dib); if(dc) DeleteDC(dc); DestroyIcon(owned); return result; }
    auto old=SelectObject(dc,dib); auto pixels=static_cast<uint32_t*>(bytes);
    std::fill(pixels,pixels+count,0xff000000u);
    BOOL drawn=DrawIconEx(dc,0,0,owned,width,height,0,nullptr,DI_NORMAL|DI_NOMIRROR); GdiFlush();
    std::vector<uint32_t> black(pixels,pixels+count);
    std::fill(pixels,pixels+count,0xffffffffu);
    drawn=drawn && DrawIconEx(dc,0,0,owned,width,height,0,nullptr,DI_NORMAL|DI_NOMIRROR); GdiFlush();
    result.frame.pixels.resize(count); bool empty=true; uint64_t hash=1469598103934665603ULL;
    for(size_t i=0;i<count;++i) {
        int difference=0;
        for(int shift:{0,8,16}) difference+=static_cast<int>((pixels[i]>>shift)&255)-static_cast<int>((black[i]>>shift)&255);
        int alpha=255-std::clamp(difference/3,0,255); if(alpha) empty=false;
        uint32_t color=black[i]&0x00ffffffu;
        if(alpha>0 && alpha<255) {
            color=0; for(int shift:{0,8,16}) color|=static_cast<uint32_t>(std::min(255,static_cast<int>((black[i]>>shift)&255)*255/alpha))<<shift;
        }
        result.frame.pixels[i]=(static_cast<uint32_t>(alpha)<<24)|color;
        hash^=result.frame.pixels[i]; hash*=1099511628211ULL;
    }
    SelectObject(dc,old); DeleteObject(dib); DeleteDC(dc); DestroyIcon(owned);
    result.frame.width=width; result.frame.height=height; result.frame.fingerprint=hash;
    result.frame.transparent=empty; result.frame.origin=origin;
    result.isLive=origin==FrameOrigin::SourceLive; result.outcome.success=drawn!=FALSE;
    result.outcome.detail=drawn?"":"Icon rendering failed";
    return result;
}
}
