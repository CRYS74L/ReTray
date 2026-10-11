#pragma once
#include <unknwn.h>
struct BitmapBytes : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE GetBuffer(uint8_t**,uint32_t*)=0;
};
__CRT_UUID_DECL(BitmapBytes,0x5B0D3235,0x4DBA,0x4D44,0x86,0x5E,0x8F,0x1D,0x0E,0x4F,0xD0,0x4D)
