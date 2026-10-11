#include "../native/core/types.h"
#include "../native/platform/software_bitmap.h"
#include "../native/platform/bitmap_bytes.h"
#include <winrt/windows.graphics.imaging.h>
#include <winrt/windows.foundation.h>
#include <cstdio>
#include <cstdlib>
#include <functional>
#include <stdexcept>

using namespace tray;
static void FillBitmap(const winrt::Windows::Graphics::Imaging::SoftwareBitmap& bitmap,uint32_t color) {
    using namespace winrt::Windows::Graphics::Imaging;
    auto locked=bitmap.LockBuffer(BitmapBufferAccessMode::Write);
    auto reference=locked.CreateReference(); auto access=reference.as<BitmapBytes>();
    uint8_t* bytes{}; uint32_t capacity{}; winrt::check_hresult(access->GetBuffer(&bytes,&capacity));
    auto plane=locked.GetPlaneDescription(0);
    for(int y=0;y<bitmap.PixelHeight();++y) for(int x=0;x<bitmap.PixelWidth();++x)
        reinterpret_cast<uint32_t*>(bytes+plane.StartIndex+y*plane.Stride)[x]=color;
}
#define REQUIRE(x) do { if (!(x)) throw std::runtime_error(#x); } while (0)
static HICON ColoredIcon(bool visible) {
    BITMAPV5HEADER hdr{}; hdr.bV5Size=sizeof(hdr); hdr.bV5Width=16; hdr.bV5Height=-16;
    hdr.bV5Planes=1; hdr.bV5BitCount=32; hdr.bV5Compression=BI_BITFIELDS;
    hdr.bV5RedMask=0x00ff0000; hdr.bV5GreenMask=0x0000ff00; hdr.bV5BlueMask=0x000000ff; hdr.bV5AlphaMask=0xff000000;
    void* bits{}; HDC dc=GetDC(nullptr);
    auto color=CreateDIBSection(dc,reinterpret_cast<BITMAPINFO*>(&hdr),DIB_RGB_COLORS,&bits,nullptr,0);
    ReleaseDC(nullptr,dc);
    auto values=static_cast<uint32_t*>(bits); for(int i=0;i<256;++i) values[i]=visible?0xff258954:0;
    unsigned char maskBytes[32]; memset(maskBytes,visible?0:0xff,sizeof(maskBytes));
    auto mask=CreateBitmap(16,16,1,1,maskBytes);
    ICONINFO ii{}; ii.fIcon=TRUE; ii.hbmColor=color; ii.hbmMask=mask;
    auto icon=CreateIconIndirect(&ii); DeleteObject(color); DeleteObject(mask); return icon;
}
int main(int argc,char** argv) {
    winrt::init_apartment();
    std::string filter=argc>1?argv[1]:""; int passed=0,failed=0;
    auto test=[&](const char* name,std::function<void()> body) {
        if(!filter.empty() && std::string(name).find(filter)==std::string::npos) return;
        try { body(); ++passed; printf("PASS %s\n",name); }
        catch(const std::exception& e) { ++failed; printf("FAIL %s: %s\n",name,e.what()); }
        catch(const winrt::hresult_error& e) { ++failed; printf("ERROR %s: HRESULT=%08X\n",name,static_cast<unsigned>(e.code().value)); }
    };
    test("NativeSnapshot.ValidUserRange",[]{REQUIRE(ValidReadRange(0x10000,32));});
    test("NativeSnapshot.RejectNullAndOverflow",[]{REQUIRE(!ValidReadRange(0,8)); REQUIRE(!ValidReadRange(0x7fffffffffff,8)); REQUIRE(!ValidReadRange(0x10000,65537));});
    test("NativeSnapshot.AcceptBoundedVector",[]{REQUIRE(ValidVectorBounds(0x10000,0x10018,4));});
    test("NativeSnapshot.RejectMalformedVector",[]{REQUIRE(!ValidVectorBounds(0x10000,0xffff,4)); REQUIRE(!ValidVectorBounds(0x10000,0x10011,4)); REQUIRE(!ValidVectorBounds(0x10000,0x10028,4));});
    test("NativeSnapshot.PdbIdentityMustMatch",[]{ImageIdentity a{}; a.pdb.Data1=123; a.age=1; a.imageSize=8192; ImageIdentity b=a; REQUIRE(SameImage(a,b)); b.age=2; REQUIRE(!SameImage(a,b)); b=a; b.pdb.Data1=124; REQUIRE(!SameImage(a,b)); b=a; b.imageSize=4096; REQUIRE(!SameImage(a,b));});
    test("Identity.GuidAndLegacyCannotMerge",[]{IconIdentity a{}; a.ownerHwnd=0x101; a.uid=3; a.shellEpoch=1; a.ownerPid=42; a.ownerCreatedAt=99; auto b=a; b.hasGuid=true; b.guid.Data1=55; REQUIRE(!SameIcon(a,b)); REQUIRE(IdentityKey(a)!=IdentityKey(b));});
    test("Identity.OwnerGenerationCannotReuse",[]{IconIdentity a{}; a.ownerHwnd=0x101; a.uid=3; a.shellEpoch=1; a.ownerPid=42; a.ownerCreatedAt=99; auto b=a; REQUIRE(SameIcon(a,b)); b.ownerCreatedAt=100; REQUIRE(!SameIcon(a,b)); b=a; b.shellEpoch=2; REQUIRE(!SameIcon(a,b));});
    test("Frame.CachedIconIsNotLive",[]{auto icon=ColoredIcon(true); auto f=RasterizeIcon(icon,FrameOrigin::CachedOriginal); DestroyIcon(icon); REQUIRE(f.outcome.success); REQUIRE(!f.isLive); REQUIRE(f.frame.width==16); REQUIRE(!f.frame.transparent);});
    test("Frame.SourcePixelContentIsLive",[]{auto a=ColoredIcon(true),b=ColoredIcon(true); auto fa=RasterizeIcon(a,FrameOrigin::SourceLive),fb=RasterizeIcon(b,FrameOrigin::SourceLive); DestroyIcon(a); DestroyIcon(b); REQUIRE(fa.outcome.success); REQUIRE(fa.isLive); REQUIRE(fa.frame.fingerprint==fb.frame.fingerprint); REQUIRE(fa.frame.pixels.size()==256);});
    test("Frame.TransparentOffFrameStaysOff",[]{auto icon=ColoredIcon(false); auto f=RasterizeIcon(icon,FrameOrigin::SourceLive); DestroyIcon(icon); REQUIRE(f.outcome.success); REQUIRE(f.frame.transparent);});
    test("Frame.InvalidWinrtPointerIsRejected",[]{auto f=RasterizeIcon(reinterpret_cast<HICON>(0x123456789000ULL),FrameOrigin::CachedOriginal); REQUIRE(!f.outcome.success); REQUIRE(!f.isLive);});
    test("CurrentFrame.SoftwareBitmapPixelsAreCurrent",[]{using namespace winrt::Windows::Graphics::Imaging; SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8,16,16,BitmapAlphaMode::Premultiplied); FillBitmap(bitmap,0xff258954); auto a=RasterizeSoftwareBitmap(winrt::get_abi(bitmap)); REQUIRE(a.outcome.success); REQUIRE(a.isLive); REQUIRE(a.frame.pixels[0]==0xff258954); FillBitmap(bitmap,0xff1955e9); auto b=RasterizeSoftwareBitmap(winrt::get_abi(bitmap)); REQUIRE(b.outcome.success); REQUIRE(b.frame.pixels[0]==0xff1955e9); REQUIRE(a.frame.fingerprint!=b.frame.fingerprint);});
    test("CurrentFrame.SoftwareBitmapOffFramePreserved",[]{using namespace winrt::Windows::Graphics::Imaging; SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8,16,16,BitmapAlphaMode::Premultiplied); FillBitmap(bitmap,0); auto frame=RasterizeSoftwareBitmap(winrt::get_abi(bitmap)); REQUIRE(frame.outcome.success); REQUIRE(frame.frame.transparent);});
    printf("RESULT passed=%d failed=%d\n",passed,failed); return failed?1:0;
}
