using System;
using System.Threading;
using NAudio.Dsp;

// One audio-thread producer; UI-only FFT. No locks, allocations, or FFT in Push.
sealed class SpectrumTap {
    readonly float[] samples=new float[8192];
    long written,published;
    public volatile bool Enabled=true;
    public void Push(float value){samples[(int)(written++&8191)]=value;}
    public void Publish(){Interlocked.Exchange(ref published,written);}
    public bool CopyLatest(float[] target){
        long end=Interlocked.Read(ref published);
        if(!Enabled||end<target.Length)return false;
        for(int i=0;i<target.Length;i++)target[i]=samples[(int)((end-target.Length+i)&8191)];
        return true;
    }
}
sealed class SpectrumView {
    const int N=1024;
    readonly float[] samples=new float[N];
    readonly Complex[] fft=new Complex[N];
    public readonly double[] Bands=new double[12];
    public double Level;
    internal static double PerceivedLevel(double rms){return Math.Max(0,Math.Min(1,(20*Math.Log10(rms+1e-9)+70)/62));}
    public void Update(SpectrumTap tap,int rate){
        bool ready=tap!=null&&tap.CopyLatest(samples);
        double sum=0;
        for(int i=0;i<N;i++){
            float v=ready?samples[i]:0;sum+=v*v;
            fft[i].X=(float)(v*(.5-.5*Math.Cos(2*Math.PI*i/(N-1))));fft[i].Y=0;
        }
        FastFourierTransform.FFT(true,10,fft);
        double target=ready?PerceivedLevel(Math.Sqrt(sum/N)):0;
        double response=target>Level?.70:.16;
        Level+=(target-Level)*response;
        for(int b=0;b<12;b++){
            double lo=45*Math.Pow(18000.0/45,b/12.0),hi=45*Math.Pow(18000.0/45,(b+1)/12.0);
            int first=Math.Max(1,(int)(lo*N/rate)),last=Math.Min(N/2-1,Math.Max(first,(int)(hi*N/rate)));
            double power=0;
            for(int k=first;k<=last;k++)power=Math.Max(power,fft[k].X*fft[k].X+fft[k].Y*fft[k].Y);
            double db=20*Math.Log10(Math.Sqrt(power)+1e-9);
            double value=ready?Math.Max(0,Math.Min(1,(db+70)/65)):0;
            Bands[b]=Bands[b]*.65+value*.35;
        }
    }
}
