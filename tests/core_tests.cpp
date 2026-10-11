#include "../native/core/types.h"
#include "../native/platform/software_bitmap.h"
#include "../native/core/notify_patch.h"
#include "../native/core/rule_engine.h"
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
    test("Protocol.ModifyIconPreservesOwnerCallbackVersion",[]{NativeIconRecord r{}; r.callbackMessage=0x8001; r.notifyVersion=4; r.tip=L"original"; r.identity.hasGuid=true; r.identity.guid.Data1=42; IconPatch p{}; p.operation=NIM_MODIFY; p.suppliedFlags=NIF_ICON; p.frame.fingerprint=99; p.notifyVersion=5000; REQUIRE(ApplyPatch(r,p).success); REQUIRE(r.callbackMessage==0x8001); REQUIRE(r.notifyVersion==4); REQUIRE(r.tip==L"original"); REQUIRE(r.identity.guid.Data1==42); REQUIRE(r.currentFrame.fingerprint==99);});
    test("Protocol.StateMaskOnlyTouchesSelectedBits",[]{NativeIconRecord r{}; r.sourceState=0x10; IconPatch p{}; p.operation=NIM_MODIFY; p.suppliedFlags=NIF_STATE; p.state=NIS_HIDDEN; p.stateMask=NIS_HIDDEN; REQUIRE(ApplyPatch(r,p).success); REQUIRE(r.sourceState==0x11); p.state=0; REQUIRE(ApplyPatch(r,p).success); REQUIRE(r.sourceState==0x10);});
    test("Protocol.SetVersionOnlyOnVersionOperation",[]{NativeIconRecord r{}; IconPatch p{}; p.operation=NIM_SETVERSION; p.notifyVersion=4; REQUIRE(ApplyPatch(r,p).success); REQUIRE(r.notifyVersion==4); p.operation=NIM_MODIFY; p.suppliedFlags=NIF_TIP; p.notifyVersion=10000; p.tip=L"updated"; REQUIRE(ApplyPatch(r,p).success); REQUIRE(r.notifyVersion==4); REQUIRE(r.tip==L"updated");});
    test("Protocol.Fixed32BitWirePreservesGuidAndModule",[]{std::vector<uint8_t> b(1484); auto d=[&](size_t n,uint32_t v){memcpy(b.data()+n,&v,4);}; d(0,0x34753423);d(4,NIM_MODIFY);d(8,956);d(12,0x12345);d(16,42);d(20,NIF_GUID|NIF_ICON);d(28,0x9876);d(0x3b0,55);const wchar_t tip[]=L"test";memcpy(b.data()+0x20,tip,sizeof(tip));const wchar_t path[]=L"C:\\Fake\\Example.exe";memcpy(b.data()+0x3c4,path,sizeof(path));auto e=DecodeWire(b);REQUIRE(e.supported);REQUIRE(e.identity.ownerHwnd==0x12345);REQUIRE(e.identity.uid==42);REQUIRE(e.identity.hasGuid);REQUIRE(e.identity.guid.Data1==55);REQUIRE(e.iconHandle==0x9876);REQUIRE(e.module==L"C:\\Fake\\Example.exe");});
    test("Protocol.TruncatedWireCannotBeDecoded",[]{std::vector<uint8_t> b(20);uint32_t magic=0x34753423;memcpy(b.data(),&magic,4);REQUIRE(!DecodeWire(b).supported);});
    test("Dynamic.StaticIdleCanBeCustom",[]{Frame f{};f.fingerprint=123;f.width=16;f.height=16;FrameBaseline b{true,123,16,16};REQUIRE(ChooseFrame(f,b,true)==FrameDecision::CustomIdle);});
    test("Dynamic.OffAndUnknownAnimationPreserved",[]{Frame f{};f.fingerprint=456;f.width=16;f.height=16;FrameBaseline b{true,123,16,16};REQUIRE(ChooseFrame(f,b,true)==FrameDecision::NativeDynamic);f.transparent=true;REQUIRE(ChooseFrame(f,b,true)==FrameDecision::NativeOff);});
    test("Dynamic.UncertainBaselineDoesNotFlatten",[]{Frame f{};FrameBaseline b{};REQUIRE(ChooseFrame(f,b,true)==FrameDecision::AwaitingBaseline);});
    test("Session.QueryAlwaysAcceptsAndStopsRecovery",[]{SessionMachine m{};m.state=SessionState::Ready;REQUIRE(HandleSessionQuery(m));REQUIRE(m.state==SessionState::SessionQuery);REQUIRE(!ShouldRecover(m));});
    test("Session.CancelledShutdownRecoversPriorState",[]{SessionMachine m{};m.state=SessionState::Ready;HandleSessionQuery(m);HandleSessionEnd(m,false);REQUIRE(m.state==SessionState::Ready);REQUIRE(ShouldRecover(m));});
    test("Session.UserStopAndEndNeverRecover",[]{SessionMachine m{};m.state=SessionState::UserStopping;REQUIRE(!ShouldRecover(m));HandleSessionQuery(m);HandleSessionEnd(m,false);REQUIRE(m.state==SessionState::UserStopping);m.state=SessionState::Ready;HandleSessionEnd(m,true);REQUIRE(m.state==SessionState::SessionEnding);REQUIRE(!ShouldRecover(m));});
    test("Catalog.PartialSnapshotCannotEraseLiveState",[]{IconCatalog c;SnapshotResult s;s.complete=true;s.records.resize(1);s.records[0].identity.ownerPid=42;REQUIRE(MergeSnapshot(c,s,100).added==1);s.complete=false;s.records.clear();REQUIRE(!MergeSnapshot(c,s,200).accepted);REQUIRE(c.items.size()==1);});
    test("Catalog.DeleteRemovesRuntimeOnly",[]{IconCatalog c;SnapshotResult s;s.complete=true;s.records.resize(1);s.records[0].executable=L"C:\\Example.exe";auto selector=SelectorFor(s.records[0]);auto key=PersistentKey(selector);MergeSnapshot(c,s,100);s.records.clear();REQUIRE(MergeSnapshot(c,s,200).removed==1);REQUIRE(c.items.empty());REQUIRE(PersistentKey(selector)==key);});
    test("Catalog.ThousandUpdatesKeepSelectionAndIdentity",[]{IconCatalog c;SnapshotResult s;s.complete=true;s.records.resize(2);auto& a=s.records[0];a.identity.ownerPid=42;a.identity.ownerCreatedAt=1;a.identity.ownerHwnd=100;a.identity.uid=7;auto& b=s.records[1];b=a;b.identity.hasGuid=true;b.identity.guid.Data1=55;MergeSnapshot(c,s,1);c.selected=IdentityKey(b.identity);for(int n=0;n<1000;++n){a.currentFrame.fingerprint=n;REQUIRE(MergeSnapshot(c,s,n+2).accepted);REQUIRE(c.items.size()==2);REQUIRE(c.selected==IdentityKey(b.identity));}REQUIRE(c.items.at(IdentityKey(a.identity)).activity.lastFrameChange==1001);});
    test("Rule.DistinguishesGuidFromLegacy",[]{NativeIconRecord r;r.executable=L"C:\\Example.exe";r.identity.uid=7;auto legacy=SelectorFor(r);r.identity.hasGuid=true;r.identity.guid.Data1=55;auto guid=SelectorFor(r);REQUIRE(PersistentKey(legacy)!=PersistentKey(guid));REQUIRE(!Matches(legacy,r));REQUIRE(Matches(guid,r));});
    test("Rule.MatchesRestartWithoutTooltipOrHandles",[]{NativeIconRecord r;r.executable=L"C:\\Example.exe";r.windowClass=L"App";r.identity.uid=7;auto s=SelectorFor(r);r.tip=L"new message";r.identity.ownerCreatedAt=999;r.identity.ownerHwnd=456;REQUIRE(Matches(s,r));r.executable=L"c:\\EXAMPLE.EXE";REQUIRE(Matches(s,r));r.identity.uid=8;REQUIRE(!Matches(s,r));});
    test("Activity.FrameAndAccessTimersAreSeparate",[]{IconRule r;r.inactiveSeconds=10;ActivityState a{1000,1000,1};REQUIRE(IsInactive(r,a,11000));RecordAccess(a,11000);REQUIRE(a.lastFrameChange==1000);REQUIRE(!IsInactive(r,a,11000));REQUIRE(IsInactive(r,a,21000));r.alwaysShow=true;REQUIRE(!IsInactive(r,a,999999));});
    printf("RESULT passed=%d failed=%d\n",passed,failed); return failed?1:0;
}
