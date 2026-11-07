param([string]$PreviewDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$project = Split-Path -Parent $PSScriptRoot
$source = Join-Path $project 'src'
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The .NET Framework x64 C# compiler is required.' }
if (-not $PreviewDirectory) { $PreviewDirectory = Join-Path $project 'build\icon-review' }
$PreviewDirectory = [IO.Path]::GetFullPath($PreviewDirectory)
New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null

# The app and generator share BrandArtwork.Draw, so their geometry cannot drift.
$helper = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using OpenTrackpadUi;
class IconGenerator {
  static readonly int[] Sizes={16,20,24,32,40,48,64,128,256};
  static void UInt32(Stream stream,uint value){stream.WriteByte((byte)(value>>24));stream.WriteByte((byte)(value>>16));stream.WriteByte((byte)(value>>8));stream.WriteByte((byte)value);}
  static void Chunk(Stream stream,string kind,byte[] data){
    UInt32(stream,(uint)data.Length);byte[] type=System.Text.Encoding.ASCII.GetBytes(kind);stream.Write(type,0,type.Length);stream.Write(data,0,data.Length);
    uint crc=0xffffffff;foreach(byte[] bytes in new[]{type,data})foreach(byte value in bytes){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)==0?0:0xedb88320);}
    UInt32(stream,crc^0xffffffff);
  }
  static int Paeth(int left,int above,int corner){int p=left+above-corner,a=Math.Abs(p-left),b=Math.Abs(p-above),c=Math.Abs(p-corner);return a<=b && a<=c?left:b<=c?above:corner;}
  // Lossless adaptive PNG filters keep the nine native icon sizes small.
  static byte[] Png(Bitmap bitmap){
    int stride=bitmap.Width*4;byte[] previous=new byte[stride];
    using(var filtered=new MemoryStream()) {
      for(int y=0;y<bitmap.Height;y++) {
        byte[] row=new byte[stride];for(int x=0;x<bitmap.Width;x++){Color color=bitmap.GetPixel(x,y);int offset=x*4;row[offset]=color.R;row[offset+1]=color.G;row[offset+2]=color.B;row[offset+3]=color.A;}
        int bestScore=int.MaxValue,bestFilter=0;byte[] best=null;
        for(int filter=0;filter<5;filter++) {
          int score=0;byte[] candidate=new byte[stride];
          for(int index=0;index<stride;index++){int left=index<4?0:row[index-4],above=previous[index],corner=index<4?0:previous[index-4];int predictor=filter==0?0:filter==1?left:filter==2?above:filter==3?(left+above)/2:Paeth(left,above,corner);byte residual=unchecked((byte)(row[index]-predictor));candidate[index]=residual;score+=Math.Abs((int)unchecked((sbyte)residual));}
          if(score<bestScore){bestScore=score;best=candidate;bestFilter=filter;}
        }
        filtered.WriteByte((byte)bestFilter);filtered.Write(best,0,best.Length);previous=row;
      }
      byte[] raw=filtered.ToArray(),compressed;
      using(var zlib=new MemoryStream()) {
        zlib.WriteByte(0x78);zlib.WriteByte(0xda);
        using(var deflate=new DeflateStream(zlib,CompressionLevel.Optimal,true))deflate.Write(raw,0,raw.Length);
        uint a=1,b=0;foreach(byte value in raw){a=(a+value)%65521;b=(b+a)%65521;}UInt32(zlib,(b<<16)|a);compressed=zlib.ToArray();
      }
      using(var png=new MemoryStream())using(var header=new MemoryStream()) {
        byte[] signature={137,80,78,71,13,10,26,10};png.Write(signature,0,signature.Length);
        UInt32(header,(uint)bitmap.Width);UInt32(header,(uint)bitmap.Height);byte[] format={8,6,0,0,0};header.Write(format,0,format.Length);
        Chunk(png,"IHDR",header.ToArray());Chunk(png,"IDAT",compressed);Chunk(png,"IEND",new byte[0]);return png.ToArray();
      }
    }
  }
  static Bitmap Render(int size) {
    const int samples=4;
    using(var large=new Bitmap(size*samples,size*samples,PixelFormat.Format32bppArgb)) {
      using(var graphics=Graphics.FromImage(large)) {
        graphics.Clear(Color.Transparent);
        BrandArtwork.Draw(graphics,new RectangleF(0,0,large.Width,large.Height));
      }
      var result=new Bitmap(size,size,PixelFormat.Format32bppArgb);
      using(var graphics=Graphics.FromImage(result))using(var attributes=new ImageAttributes()) {
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode=CompositingMode.SourceCopy;
        graphics.CompositingQuality=CompositingQuality.HighQuality;
        graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(large,new Rectangle(0,0,size,size),0,0,large.Width,large.Height,GraphicsUnit.Pixel,attributes);
      }
      return result;
    }
  }
  static void Sheet(string directory,bool dark) {
    using(var sheet=new Bitmap(600,336,PixelFormat.Format32bppArgb))using(var graphics=Graphics.FromImage(sheet))using(var font=new Font("Segoe UI",10))using(var ink=new SolidBrush(dark?Color.FromArgb(232,236,241):Color.FromArgb(48,56,66))) {
      graphics.Clear(dark?Color.FromArgb(29,33,40):Color.FromArgb(246,248,250));
      int[] sizes={16,24,48,256},centers={48,116,204,432};
      for(int index=0;index<sizes.Length;index++) {
        int size=sizes[index];
        using(var icon=Render(size))graphics.DrawImageUnscaled(icon,centers[index]-size/2,164-size/2);
        string label=size+" px";SizeF text=graphics.MeasureString(label,font);graphics.DrawString(label,font,ink,centers[index]-text.Width/2,304);
      }
      sheet.Save(Path.Combine(directory,dark?"preview-dark.png":"preview-light.png"),ImageFormat.Png);
    }
  }
  static int Main(string[] args) {
    try {
      var images=new byte[Sizes.Length][];
      for(int index=0;index<Sizes.Length;index++)using(var bitmap=Render(Sizes[index])) {
        images[index]=Png(bitmap);
        File.WriteAllBytes(Path.Combine(args[1],"icon-"+Sizes[index]+".png"),images[index]);
      }
      using(var stream=File.Create(args[0]))using(var writer=new BinaryWriter(stream)) {
        writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)Sizes.Length);
        uint offset=(uint)(6+16*Sizes.Length);
        for(int index=0;index<Sizes.Length;index++) {
          writer.Write((byte)(Sizes[index]==256?0:Sizes[index]));writer.Write((byte)(Sizes[index]==256?0:Sizes[index]));
          writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);
          writer.Write((uint)images[index].Length);writer.Write(offset);offset+=(uint)images[index].Length;
        }
        foreach(byte[] image in images)writer.Write(image);
      }
      Sheet(args[1],false);Sheet(args[1],true);return 0;
    }catch(Exception error){Console.Error.WriteLine(error);return 1;}
  }
}
'@
$svg = @'
<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-labelledby="title">
  <title id="title">Open Trackpad</title>
  <defs><linearGradient id="mint" x1="0" y1="0" x2="64" y2="64" gradientUnits="userSpaceOnUse"><stop stop-color="#5fCdb1"/><stop offset="1" stop-color="#258f80"/></linearGradient></defs>
  <rect x=".5" y=".5" width="63" height="63" rx="18" fill="url(#mint)"/>
  <rect x="1" y="1" width="62" height="62" rx="17.5" fill="none" stroke="#fff" stroke-opacity=".149"/>
  <g fill="none" stroke="#fff" stroke-width="3.25" stroke-linecap="round" stroke-linejoin="round">
    <rect x="15" y="17" width="34" height="30" rx="5.5"/>
    <path d="M24 24v8 M32 24v8 M40 24v8 M24 40h16"/>
  </g>
</svg>
'@
$prefix = Join-Path ([IO.Path]::GetTempPath()) ('OpenTrackpad.Icon.' + [Guid]::NewGuid().ToString('N'))
$helperSource = $prefix + '.cs'
$helperBinary = $prefix + '.exe'
try {
    [IO.File]::WriteAllText($helperSource, $helper, [Text.UTF8Encoding]::new($false))
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:Accessibility.dll ('/out:' + $helperBinary) $helperSource (Join-Path $source 'UiKit.cs') (Join-Path $source 'UiPreferences.cs') (Join-Path $source 'AppPaths.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Icon generator compilation failed.' }
    $icon = Join-Path $source 'OpenTrackpad.ico'
    & $helperBinary $icon $PreviewDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
    [IO.File]::WriteAllText((Join-Path $source 'OpenTrackpad.svg'), $svg + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    [pscustomobject]@{ icon=$icon; bytes=(Get-Item -LiteralPath $icon).Length; sizes=@(16,20,24,32,40,48,64,128,256); previews=$PreviewDirectory } | ConvertTo-Json
} finally {
    foreach ($file in @($helperSource,$helperBinary)) { if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file -Force } }
}
