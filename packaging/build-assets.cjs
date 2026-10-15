// SPDX-License-Identifier: GPL-3.0-only
// Each size is rendered directly from the approved SVG, never from another PNG.
const fs=require('node:fs'),path=require('node:path'),sharp=require('sharp');
const root=path.resolve(__dirname,'..'),masters=path.join(root,'assets/branding');
const destinations=['ui/ReTray.App/Assets','ui/ReTray.Wpf/src/LanDeviceMonitor.App/Assets'].map(p=>path.join(root,p));
const sizes=[16,20,24,30,32,36,40,48,60,64,72,80,96,128,256];
const cache=new Map();
async function render(name,width,height=width){
 const key=[name,width,height].join(':');if(cache.has(key))return cache.get(key);
 const side=Math.min(width,height),source=path.join(masters,name+'.svg');
 const foreground=await sharp(source,{density:72*side/1024}).resize(side,side).png().toBuffer();
 const png=width===height?foreground:await sharp({create:{width,height,channels:4,background:'#00000000'}}).composite([{input:foreground,left:Math.floor((width-side)/2),top:Math.floor((height-side)/2)}]).png().toBuffer();
 cache.set(key,png);return png;
}
async function ico(name){
 const frames=await Promise.all(sizes.map(size=>render(name,size))),header=Buffer.alloc(6+16*sizes.length);
 header.writeUInt16LE(1,2);header.writeUInt16LE(sizes.length,4);let offset=header.length;
 frames.forEach((frame,i)=>{const p=6+16*i,size=sizes[i]===256?0:sizes[i];header[p]=size;header[p+1]=size;header.writeUInt16LE(1,p+4);header.writeUInt16LE(32,p+6);header.writeUInt32LE(frame.length,p+8);header.writeUInt32LE(offset,p+12);offset+=frame.length;});
 return Buffer.concat([header,...frames]);
}
async function main(){
 for(const d of destinations)fs.mkdirSync(d,{recursive:true});
 async function write(file,bytes){for(const d of destinations)fs.writeFileSync(path.join(d,file),bytes);}
 for(const name of ['color','black','white','macos-white'])fs.writeFileSync(path.join(masters,'retray-'+name+'.png'),await render('retray-'+name,1024));
 await write('AppIcon.png',await render('retray-macos-white',1024));
 await write('WindowIcon.png',await render('retray-color',1024));
 for(const [file,name]of [['AppIcon','macos-white'],['WindowIcon','color'],['TrayBlack','black'],['TrayWhite','white']])await write(file+'.ico',await ico('retray-'+name));
 for(const [base,w,h]of [['Square44x44Logo',44,44],['Square150x150Logo',150,150],['Wide310x150Logo',310,150],['SplashScreen',620,300]]){
  await write(base+'.png',await render('retray-macos-white',w,h));
  for(const scale of [100,125,150,200,250,300,400])await write(base+'.scale-'+scale+'.png',await render('retray-macos-white',Math.round(w*scale/100),Math.round(h*scale/100)));
 }
 await write('StoreLogo.png',await render('retray-macos-white',50));
 await write('LockScreenLogo.scale-200.png',await render('retray-macos-white',48));
 // Start/app list use the plate. TaskbarBranding supplies WindowIcon.ico to the running window.
 for(const size of sizes)for(const suffix of ['', '_altform-unplated','_altform-lightunplated'])await write('Square44x44Logo.targetsize-'+size+suffix+'.png',await render('retray-macos-white',size));
 console.log(JSON.stringify({source:'approved SVG masters',icoSizes:sizes,variants:4,locations:destinations}));
}
main().catch(e=>{console.error(e);process.exitCode=1;});
