$ErrorActionPreference='Stop'
$assets=Join-Path (Split-Path $PSScriptRoot -Parent) 'ui\TrayAssistant.App\Assets'
Add-Type -AssemblyName System.Drawing
if(!('TrayBrand' -as [type])){Add-Type -ReferencedAssemblies System.Drawing.Common,System.Drawing.Primitives,System.Private.Windows.GdiPlus,System.Private.Windows.Core -TypeDefinition @'
using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Drawing.Imaging;using System.IO;
public static class TrayBrand {
static GraphicsPath Rounded(float x,float y,float w,float h,float r){var p=new GraphicsPath();p.AddArc(x,y,r*2,r*2,180,90);p.AddArc(x+w-r*2,y,r*2,r*2,270,90);p.AddArc(x+w-r*2,y+h-r*2,r*2,r*2,0,90);p.AddArc(x,y+h-r*2,r*2,r*2,90,90);p.CloseFigure();return p;}
public static byte[] Png(int width,int height){using(var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(bitmap)){g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);float side=Math.Min(width,height)*0.8f;g.TranslateTransform((width-side)/2,(height-side)/2);g.ScaleTransform(side/64,side/64);using(var shape=Rounded(0,0,64,64,14))using(var fill=new LinearGradientBrush(new Rectangle(0,0,64,64),Color.FromArgb(49,190,176),Color.FromArgb(13,101,98),90f)){g.FillPath(fill,shape);}using(var white=new SolidBrush(Color.White))using(var accent=new SolidBrush(Color.FromArgb(178,245,221))){using(var a=Rounded(13,22,9,15,3))g.FillPath(white,a);using(var b=Rounded(27,15,10,22,3))g.FillPath(white,b);using(var c=Rounded(42,25,9,12,3))g.FillPath(accent,c);using(var tray=Rounded(10,43,44,7,3.5f))g.FillPath(white,tray);}using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);return stream.ToArray();}}}
}
'@}
$sizes=@{'Square150x150Logo.scale-200.png'=@(300,300);'Square44x44Logo.scale-200.png'=@(88,88);'Square44x44Logo.targetsize-24_altform-unplated.png'=@(24,24);'Square44x44Logo.targetsize-48_altform-lightunplated.png'=@(48,48);'StoreLogo.png'=@(50,50);'LockScreenLogo.scale-200.png'=@(48,48);'Wide310x150Logo.scale-200.png'=@(620,300);'SplashScreen.scale-200.png'=@(1240,600)}
foreach($name in $sizes.Keys){[IO.File]::WriteAllBytes((Join-Path $assets $name),[TrayBrand]::Png($sizes[$name][0],$sizes[$name][1]))}
$frames=@(16,20,24,32,48,64,128,256)|ForEach-Object { [pscustomobject]@{Size=$_;Bytes=[TrayBrand]::Png($_,$_)} }
$file=[IO.File]::Create((Join-Path $assets 'AppIcon.ico'));$writer=[IO.BinaryWriter]::new($file)
try{$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count);$offset=6+16*$frames.Count;foreach($frame in $frames){$size=if($frame.Size -eq 256){0}else{$frame.Size};$writer.Write([byte]$size);$writer.Write([byte]$size);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$frame.Bytes.Length};foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}}finally{$writer.Dispose();$file.Dispose()}

