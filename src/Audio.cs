using System;
using System.IO;
using System.Text;

using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.CoreAudioApi;

sealed class AudioRing {
    public readonly object Gate = new object();
    public readonly int Rate, Capacity;
    public long Written;
    public int CaptureBatchFrames;
    public float Peak;
    public volatile int Test;
    readonly float[] data;
    public AudioRing(int rate) { Rate=rate; Capacity=rate*10; data=new float[Capacity*2]; }
    public void Add(float l,float r) {
        long n=Written;
        if(Test!=0) {
            double t=(double)n/Rate, phase=t%1.0;
            float tone=phase<0.065 ? (float)(0.16*Math.Sin(2*Math.PI*900*t)*Math.Sin(Math.PI*phase/0.065)) : 0;
            l=tone; r=tone;
            if(Test==2) { if(((long)t%2)==0) r=0; else l=0; }
        }
        int p=(int)(n%Capacity)*2; data[p]=l; data[p+1]=r; Written++;
        Peak=Math.Max(Peak,Math.Max(Math.Abs(l),Math.Abs(r)));
    }
    public float At(long n,int ch) { return data[(int)(n%Capacity)*2+ch]; }
}
sealed class OutputFeed : IWaveProvider {
    [StructLayout(LayoutKind.Explicit)] struct FloatBits {
        [FieldOffset(0)] public float Value;
        [FieldOffset(0)] public uint Bits;
    }
    static void WriteFloat(byte[] b,int p,float value) {
        FloatBits f=new FloatBits();f.Value=value;uint v=f.Bits;
        b[p]=(byte)v;b[p+1]=(byte)(v>>8);b[p+2]=(byte)(v>>16);b[p+3]=(byte)(v>>24);
    }
    readonly AudioRing ring;
    public volatile int Delay, Mode;
    public int BaseBufferMs=150;
    public volatile float Volume=0.7f;
    public readonly SpectrumTap Meter=new SpectrumTap();
    public long Underruns;
    public double BufferedMs;
    double cursor=-1;
    int previousDelay=-1, fade;
    public WaveFormat WaveFormat { get; private set; }
    public OutputFeed(AudioRing r,int mode) { ring=r; Mode=mode; WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(r.Rate,2); }
    public int Read(byte[] buffer,int offset,int count) {
        Array.Clear(buffer,offset,count);
        int frames=count/8;
        lock(ring.Gate) {
            int delay=Delay;
            // A driver may deliver packets larger than the requested capture buffer.
            // Keep room for one observed capture batch, one output request and 5 ms jitter.
            double target=Math.Max(ring.Rate*BaseBufferMs/1000.0,frames+ring.CaptureBatchFrames+ring.Rate*0.005)+ring.Rate*delay/1000.0;
            if(previousDelay!=delay) { cursor=-1; previousDelay=delay; }
            if(cursor<0) {
                if(ring.Written<target+frames) return count;
                cursor=ring.Written-target; fade=0;
            }
            double available=ring.Written-cursor;
            if(available>ring.Capacity-frames || available>target+ring.Rate*0.25) {cursor=ring.Written-target;fade=0;}
            BufferedMs=(ring.Written-cursor)*1000/ring.Rate;
            // Small rate corrections compensate independent Bluetooth device clocks.
            double error=(ring.Written-cursor)-target;
            double step=1+Math.Max(-0.002,Math.Min(0.002,error/(ring.Rate*5)));
            for(int i=0;i<frames;i++) {
                long n=(long)cursor;
                if(n+1>=ring.Written) {Underruns++;cursor=-1;break;}
                double f=cursor-n;
                float l=(float)(ring.At(n,0)*(1-f)+ring.At(n+1,0)*f);
                float r=(float)(ring.At(n,1)*(1-f)+ring.At(n+1,1)*f);
                int mode=Mode;
                if(mode==0) r=l; else if(mode==1) l=r; else if(mode==2) l=r=(l+r)*0.5f;
                if(fade<ring.Rate)fade++;
                float gain=Volume*Math.Min(1f,(float)fade/(ring.Rate*0.015f));
                if(Meter.Enabled)Meter.Push((l+r)*0.5f*gain);
                WriteFloat(buffer,offset+i*8,l*gain);WriteFloat(buffer,offset+i*8+4,r*gain);
                cursor+=step;
            }
        }
        Meter.Publish();
        return count;
    }
}
sealed class Silence : IWaveProvider {
    public WaveFormat WaveFormat {get;private set;}
    public Silence(WaveFormat f) {WaveFormat=f;}
    public int Read(byte[] b,int o,int c) {Array.Clear(b,o,c);return c;}
}
sealed class FastLoopbackCapture : WasapiCapture {
    public FastLoopbackCapture(MMDevice device,int bufferMs):base(device,false,bufferMs){}
    protected override AudioClientStreamFlags GetAudioClientStreamFlags(){return AudioClientStreamFlags.Loopback;}
}
sealed class LatencyProfile {
    public readonly string Name;
    public readonly int RingMs,OutputMs,CaptureMs;
    public LatencyProfile(string name,int ring,int output,int capture){Name=name;RingMs=ring;OutputMs=output;CaptureMs=capture;}
    public override string ToString(){return Name;}
    public static readonly LatencyProfile[] All={
        new LatencyProfile("低延迟模式",25,10,10),
        new LatencyProfile("稳定模式",150,60,100)
    };
}
static class GainControl {
    public static float Effective(float trim,float master,bool follow,bool muted){return follow?(muted?0:trim*master):trim;}
}
sealed class Engine : IDisposable {
    public AudioRing Ring;
    public OutputFeed A,B;
    WasapiCapture capture;
    WasapiOut a,b,keepAlive;
    MMDevice source,left,right;
    public volatile string Error;
    bool closing;
    public Engine(string input,string outputA,string outputB):this(input,outputA,outputB,LatencyProfile.All[0]){}
    public Engine(string input,string outputA,string outputB,LatencyProfile profile) {
        if(input==outputA||input==outputB||outputA==outputB)throw new ArgumentException("请选择三个不同的设备，避免声音回授。");
        try {
            using(var e=new MMDeviceEnumerator()) {source=e.GetDevice(input);left=e.GetDevice(outputA);right=e.GetDevice(outputB);}
            capture=new FastLoopbackCapture(source,profile.CaptureMs);
            var f=capture.WaveFormat;
            bool floating=f.Encoding==WaveFormatEncoding.IeeeFloat;
            var ext=f as WaveFormatExtensible;
            if(ext!=null) floating=ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");
            if((floating && f.BitsPerSample!=32) || (!floating && f.BitsPerSample!=16 && f.BitsPerSample!=24 && f.BitsPerSample!=32)) throw new Exception("暂不支持输入格式："+f);
            Ring=new AudioRing(f.SampleRate); A=new OutputFeed(Ring,0); B=new OutputFeed(Ring,1);
            A.BaseBufferMs=B.BaseBufferMs=profile.RingMs;
            capture.DataAvailable+=(s,e)=> {
                lock(Ring.Gate) {
                    Ring.CaptureBatchFrames=Math.Max(Ring.CaptureBatchFrames,e.BytesRecorded/f.BlockAlign);
                    for(int p=0;p+f.BlockAlign<=e.BytesRecorded;p+=f.BlockAlign) {
                        float l=Decode(e.Buffer,p,f.BitsPerSample,floating);
                        float r=f.Channels>1 ? Decode(e.Buffer,p+f.BitsPerSample/8,f.BitsPerSample,floating) : l;
                        Ring.Add(l,r);
                    }
                }
            };
            capture.RecordingStopped+=(s,e)=>{if(!closing) Error=e.Exception==null?"输入设备已停止":e.Exception.Message;};
            a=new WasapiOut(left,AudioClientShareMode.Shared,true,profile.OutputMs); a.Init(A);
            b=new WasapiOut(right,AudioClientShareMode.Shared,true,profile.OutputMs); b.Init(B);
            a.PlaybackStopped+=(s,e)=>{if(!closing)Error=e.Exception==null?"音响 A 已停止":e.Exception.Message;};
            b.PlaybackStopped+=(s,e)=>{if(!closing)Error=e.Exception==null?"音响 B 已停止":e.Exception.Message;};
            keepAlive=new WasapiOut(source,AudioClientShareMode.Shared,true,profile.OutputMs); keepAlive.Init(new Silence(f));
        } catch {Dispose();throw;}
    }
    static float Decode(byte[] b,int p,int bits,bool floating) {
        if(floating)return BitConverter.ToSingle(b,p);
        if(bits==16)return BitConverter.ToInt16(b,p)/32768f;
        if(bits==24) {int v=b[p]|(b[p+1]<<8)|(b[p+2]<<16);if((v&0x800000)!=0)v|=unchecked((int)0xff000000);return v/8388608f;}
        return BitConverter.ToInt32(b,p)/2147483648f;
    }
    public void Start() {capture.StartRecording();keepAlive.Play();a.Play();b.Play();}
    public void Dispose() {
        closing=true;
        if(capture!=null){try{capture.StopRecording();}catch{} capture.Dispose();capture=null;}
        foreach(var p in new[]{a,b,keepAlive}) if(p!=null){try{p.Stop();}catch{}p.Dispose();}
        a=b=keepAlive=null;
        foreach(var d in new[]{source,left,right})if(d!=null)d.Dispose();
        source=left=right=null;
    }
}
