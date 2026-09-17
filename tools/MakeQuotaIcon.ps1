Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
public static class QuotaIconArt {
    static GraphicsPath Round(float x,float y,float w,float h,float r) {
        var p=new GraphicsPath(); float d=r*2;
        p.AddArc(x,y,d,d,180,90); p.AddArc(x+w-d,y,d,d,270,90);
        p.AddArc(x+w-d,y+h-d,d,d,0,90); p.AddArc(x,y+h-d,d,d,90,90); p.CloseFigure(); return p;
    }
    static Bitmap Draw(int size) {
        int render=Math.Max(512,size*4);
        using(var master=new Bitmap(render,render,PixelFormat.Format32bppArgb)) {
            using(var g=Graphics.FromImage(master)) {
                g.SmoothingMode=SmoothingMode.AntiAlias; g.ScaleTransform(render/512f,render/512f);
                using(var path=Round(8,8,496,496,112))
                using(var bg=new LinearGradientBrush(new Point(50,0),new Point(460,512),Color.FromArgb(49,65,88),Color.FromArgb(15,23,37))) {
                    g.FillPath(bg,path);
                    using(var border=new Pen(Color.FromArgb(65,199,224,255),3)) g.DrawPath(border,path);
                }
                using(var track=new Pen(Color.FromArgb(35,190,215,235),32)) g.DrawEllipse(track,94,94,324,324);
                using(var track=new Pen(Color.FromArgb(27,190,215,235),23)) g.DrawEllipse(track,142,142,228,228);
                using(var gradient=new LinearGradientBrush(new Point(90,90),new Point(420,420),Color.FromArgb(155,255,219),Color.FromArgb(56,211,174)))
                using(var arc=new Pen(gradient,32)) { arc.StartCap=arc.EndCap=LineCap.Round; g.DrawArc(arc,94,94,324,324,-90,275); }
                using(var gradient=new LinearGradientBrush(new Point(130,130),new Point(380,380),Color.FromArgb(177,220,255),Color.FromArgb(96,159,252)))
                using(var arc=new Pen(gradient,23)) { arc.StartCap=arc.EndCap=LineCap.Round; g.DrawArc(arc,142,142,228,228,-90,210); }
                using(var ink=new Pen(Color.FromArgb(245,249,255),17)) {
                    ink.StartCap=ink.EndCap=LineCap.Round; ink.LineJoin=LineJoin.Round;
                    g.DrawLines(ink,new PointF[]{new PointF(213,235),new PointF(236,257),new PointF(213,279)});
                    g.DrawLine(ink,263,279,294,279);
                }
            }
            var output=new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(output)) { g.CompositingMode=CompositingMode.SourceCopy; g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.PixelOffsetMode=PixelOffsetMode.HighQuality; g.DrawImage(master,new Rectangle(0,0,size,size)); }
            return output;
        }
    }
    public static void Save(string folder) {
        using(var preview=Draw(512)) preview.Save(Path.Combine(folder,"CodexQuota-icon.png"),ImageFormat.Png);
        var sizes=new int[]{16,24,32,48,64,128,256}; var payloads=new List<byte[]>();
        foreach(var size in sizes) {
            using(var bmp=Draw(size)) using(var stream=new MemoryStream()) {
                if(size==256) bmp.Save(stream,ImageFormat.Png);
                else using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)) {
                    int maskStride=((size+31)/32)*4;
                    writer.Write(40); writer.Write(size); writer.Write(size*2); writer.Write((short)1); writer.Write((short)32);
                    writer.Write(0); writer.Write(size*size*4+maskStride*size); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                    for(int y=size-1;y>=0;y--) for(int x=0;x<size;x++) { var c=bmp.GetPixel(x,y); writer.Write(c.B); writer.Write(c.G); writer.Write(c.R); writer.Write(c.A); }
                    writer.Write(new byte[maskStride*size]);
                }
                payloads.Add(stream.ToArray());
            }
        }
        using(var file=File.Create(Path.Combine(folder,"CodexQuota.ico"))) using(var w=new BinaryWriter(file)) {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length); int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) { w.Write((byte)(sizes[i]==256?0:sizes[i])); w.Write((byte)(sizes[i]==256?0:sizes[i])); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32); w.Write(payloads[i].Length); w.Write(offset); offset+=payloads[i].Length; }
            foreach(var data in payloads) w.Write(data);
        }
        using(var icon=new Icon(Path.Combine(folder,"CodexQuota.ico"),32,32)) { if(icon.Handle==IntPtr.Zero) throw new Exception("Invalid icon"); }
    }
}
'@
[QuotaIconArt]::Save((Split-Path $PSScriptRoot -Parent))

