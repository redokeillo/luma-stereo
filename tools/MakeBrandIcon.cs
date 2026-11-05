using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Collections.Generic;
class MakeBrandIcon {
    static void Main(string[] args){
        Directory.CreateDirectory(args[0]);int[] sizes={16,20,24,32,48,64,128,256};var frames=new List<byte[]>();
        foreach(int size in sizes)using(var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb)){
            using(var g=Graphics.FromImage(bitmap)){
                g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);float corner=size*.16f;
                using(var path=new GraphicsPath()){
                    float d=corner*2;path.AddArc(0,0,d,d,180,90);path.AddArc(size-d,0,d,d,270,90);path.AddArc(size-d,size-d,d,d,0,90);path.AddArc(0,size-d,d,d,90,90);path.CloseFigure();
                    using(var fill=new LinearGradientBrush(new Rectangle(0,0,size,size),Color.FromArgb(37,60,90),Color.FromArgb(13,23,40),90))g.FillPath(fill,path);
                }
                g.TextRenderingHint=size<=24?TextRenderingHint.SingleBitPerPixelGridFit:TextRenderingHint.AntiAliasGridFit;
                using(var format=new StringFormat(StringFormat.GenericTypographic)){format.Alignment=StringAlignment.Center;format.LineAlignment=StringAlignment.Center;
                    Draw(g,"REDO'",new RectangleF(0,size*.10f,size,size*.39f),size*.30f,Color.FromArgb(241,247,255),format);
                    Draw(g,"LUMA",new RectangleF(0,size*.48f,size,size*.40f),size*.31f,Color.FromArgb(138,239,235),format);
                }
            }
            using(var m=new MemoryStream()){bitmap.Save(m,ImageFormat.Png);frames.Add(m.ToArray());}
            if(size==256)bitmap.Save(Path.Combine(args[0],"redo-luma-icon.png"),ImageFormat.Png);
        }
        using(var w=new BinaryWriter(File.Create(Path.Combine(args[0],"redo-luma.ico")))){
            w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+sizes.Length*16;
            for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(frames[i].Length);w.Write(offset);offset+=frames[i].Length;}
            foreach(var frame in frames)w.Write(frame);
        }
    }
    static void Draw(Graphics g,string text,RectangleF box,float pixels,Color color,StringFormat format){
        using(var font=new Font("Arial",pixels,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(color))g.DrawString(text,font,brush,box,format);
    }
}
