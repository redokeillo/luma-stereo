using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

sealed partial class Controller : IDisposable {
    public readonly Window Window;
    readonly Settings settings;
    readonly bool preview;
    readonly DispatcherTimer timer=new DispatcherTimer();
    readonly SpectrumView leftSpectrum=new SpectrumView(),rightSpectrum=new SpectrumView();
    readonly Stopwatch clock=Stopwatch.StartNew();
    MMDevice masterDevice;
    Engine engine;
    bool ready,updating,muted,disposed;
    float master=.5f;
    double nextMasterRefresh;
    string lastNotice;
    DateTime leftHide=DateTime.MinValue,rightHide=DateTime.MinValue;
    public Controller(bool isPreview){
        preview=isPreview;settings=preview?new Settings():Settings.Load();
        using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml"))Window=(Window)XamlReader.Load(s);
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("psz-signature.png")){
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();C<Image>("PszSignature").Source=bitmap;
        }
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("redo-luma.ico")){var icon=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);icon.Freeze();Window.Icon=icon;}
        C<ComboBox>("Profile").ItemsSource=LatencyProfile.All;C<ComboBox>("Profile").SelectedIndex=settings.Profile;
        foreach(string name in new[]{"LeftMode","RightMode"})C<ComboBox>(name).ItemsSource=new[]{"左声道","右声道","左右混合","原始立体声"};
        C<ComboBox>("LeftMode").SelectedIndex=settings.LeftMode;C<ComboBox>("RightMode").SelectedIndex=settings.RightMode;
        C<ComboBox>("TestTone").ItemsSource=new[]{"关闭校准音","同步短音","左右交替"};C<ComboBox>("TestTone").SelectedIndex=0;
        C<Slider>("LeftTrim").Value=settings.LeftTrim;C<Slider>("RightTrim").Value=settings.RightTrim;
        C<TextBox>("LeftDelay").Text=settings.LeftDelay.ToString("0");C<TextBox>("RightDelay").Text=settings.RightDelay.ToString("0");
        C<CheckBox>("FollowSystem").IsChecked=settings.FollowSystem;C<CheckBox>("Visuals").IsChecked=settings.Visuals;
        master=(float)settings.LocalMaster/100;
        LoadDevices();Wire();ready=true;InitializeDesktop();ReadMaster();Apply();
        if(preview){SetText("Status","界面预览");SetText("LeftName","Bluetooth Speaker · L");SetText("RightName","Bluetooth Speaker · R");SetText("LeftBuffer","24 ms");SetText("RightBuffer","25 ms");master=.48f;UpdateMasterUI();Liquid("LeftLiquid",leftSpectrum,false,true);Liquid("RightLiquid",rightSpectrum,true,true);}
        else {timer.Interval=TimeSpan.FromMilliseconds(50);timer.Tick+=(s,e)=>Tick();timer.Start();}
        Window.Closing+=(s,e)=>{Save();Dispose();};
        Window.KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape)C<Grid>("Dashboard").Visibility=Visibility.Collapsed;};
    }
    public T C<T>(string name) where T:class{return (T)Window.FindName(name);}
    void SetText(string name,string text){if(!inTray)C<TextBlock>(name).Text=text;}
    bool On(string name){return C<CheckBox>(name).IsChecked==true;}
    void Wire(){
        C<Button>("DashboardButton").Click+=(s,e)=>OpenDashboard();C<Button>("CloseDashboard").Click+=(s,e)=>C<Grid>("Dashboard").Visibility=Visibility.Collapsed;
        C<Button>("RefreshDevices").Click+=(s,e)=>LoadDevices();
        C<CheckBox>("RunSwitch").Checked+=(s,e)=>{if(ready&&!updating)Start();};C<CheckBox>("RunSwitch").Unchecked+=(s,e)=>{if(ready&&!updating){autoPending=false;Stop();}};
        foreach(string n in new[]{"SourceDevice","LeftDevice","RightDevice"})C<ComboBox>(n).SelectionChanged+=(s,e)=>{if(ready){UpdateNames();Save();}};
        foreach(string n in new[]{"LeftMode","RightMode","TestTone","Profile"})C<ComboBox>(n).SelectionChanged+=(s,e)=>{if(ready){Apply();Save();}};
        foreach(string n in new[]{"FollowSystem","Visuals"}){C<CheckBox>(n).Checked+=(s,e)=>PreferenceChanged();C<CheckBox>(n).Unchecked+=(s,e)=>PreferenceChanged();}
        C<Button>("LeftTrimButton").Click+=(s,e)=>ShowTrim(true);C<Button>("RightTrimButton").Click+=(s,e)=>ShowTrim(false);
        C<Slider>("MasterDisplay").ValueChanged+=(s,e)=>ChangeMaster(e.NewValue);
        C<Slider>("LeftTrim").ValueChanged+=(s,e)=>{leftHide=DateTime.UtcNow.AddSeconds(4);Apply();};
        C<Slider>("RightTrim").ValueChanged+=(s,e)=>{rightHide=DateTime.UtcNow.AddSeconds(4);Apply();};
        C<Button>("ApplyDelay").Click+=(s,e)=>ApplyDelay(false);C<Button>("NormalizeDelay").Click+=(s,e)=>ApplyDelay(true);
        C<Button>("BluetoothSettings").Click+=(s,e)=>Open("ms-settings:bluetooth");C<Button>("SoundSettings").Click+=(s,e)=>Open("ms-settings:sound");C<Button>("CableSite").Click+=(s,e)=>Open("https://vb-audio.com/Cable/");
    }
    void PreferenceChanged(){if(!ready)return;ReadMaster();Apply();Save();if(!On("Visuals")){leftSpectrum.Level=rightSpectrum.Level=0;Array.Clear(leftSpectrum.Bands,0,12);Array.Clear(rightSpectrum.Bands,0,12);Liquid("LeftLiquid",leftSpectrum,false,false);Liquid("RightLiquid",rightSpectrum,true,false);}}
    void Open(string uri){try{Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});}catch(Exception ex){Notice(ex.Message,true);}}
    public void OpenDashboard(){if(inTray)return;SetIdle(false);C<Grid>("Dashboard").Visibility=Visibility.Visible;}
    void Notice(string message,bool open){lastNotice=message;SetText("Notice",message);if(open)OpenDashboard();}
    DeviceItem Selected(string name){return C<ComboBox>(name).SelectedItem as DeviceItem;}
    string Id(string name){var d=Selected(name);return d==null?"":d.Id;}
    void LoadDevices(){
        if(engine!=null)return;
        string[] wanted={Id("SourceDevice"),Id("LeftDevice"),Id("RightDevice")};
        if(wanted[0]=="")wanted[0]=settings.SourceId;if(wanted[1]=="")wanted[1]=settings.LeftId;if(wanted[2]=="")wanted[2]=settings.RightId;
        try{
            var devices=new List<DeviceItem>();
            if(!preview)using(var e=new MMDeviceEnumerator())foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){devices.Add(new DeviceItem{Id=d.ID,Name=d.FriendlyName});d.Dispose();}
            bool prior=ready;ready=false;
            string[] names={"SourceDevice","LeftDevice","RightDevice"};
            for(int i=0;i<3;i++){var c=C<ComboBox>(names[i]);c.ItemsSource=devices;c.SelectedItem=devices.FirstOrDefault(d=>d.Id==wanted[i]);}
            if(Selected("SourceDevice")==null)C<ComboBox>("SourceDevice").SelectedItem=devices.FirstOrDefault(d=>d.Name.StartsWith("CABLE Input",StringComparison.OrdinalIgnoreCase));
            ready=prior;UpdateNames();
            if(devices.Count==0&&!preview)Notice("Windows 暂无可用播放设备。请先在蓝牙设置连接音箱，再刷新。",false);
        }catch(Exception ex){Notice("读取音频设备失败："+ex.Message,false);}
    }
    void UpdateNames(){
        var a=Selected("LeftDevice");var b=Selected("RightDevice");var src=Selected("SourceDevice");
        SetText("LeftName",a==null?"尚未选择设备":a.Name);SetText("RightName",b==null?"尚未选择设备":b.Name);
        SetText("DeviceDetails","来源："+(src==null?"未选择":src.Detail)+"\n左："+(a==null?"未选择":a.Detail)+"\n右："+(b==null?"未选择":b.Detail));
    }
    bool syncingMaster;
    void ChangeMaster(double value){
        if(syncingMaster||!ready)return;
        float requested=(float)Math.Max(0,Math.Min(1,value/100));
        try{
            if(On("FollowSystem")&&!preview){
                using(var enumerator=new MMDeviceEnumerator())using(var device=enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia)){
                    device.AudioEndpointVolume.MasterVolumeLevelScalar=requested;
                    if(requested>0)device.AudioEndpointVolume.Mute=false;
                }
            }else settings.LocalMaster=requested*100;
            master=requested;muted=false;UpdateMasterUI();Apply();Save();
        }catch(Exception ex){Notice("总音量调整失败："+ex.Message,true);ReadMaster();}
    }
    void ReadMaster(){
        if(preview){master=.48f;UpdateMasterUI();return;}
        if(!On("FollowSystem")){master=(float)settings.LocalMaster/100;muted=false;UpdateMasterUI();return;}
        try{
            if(masterDevice==null||clock.Elapsed.TotalSeconds>=nextMasterRefresh){
                using(var e=new MMDeviceEnumerator()){
                    var d=e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
                    if(masterDevice!=null&&masterDevice.ID==d.ID)d.Dispose();else{if(masterDevice!=null)masterDevice.Dispose();masterDevice=d;}
                }
                nextMasterRefresh=clock.Elapsed.TotalSeconds+1;
            }
            master=masterDevice.AudioEndpointVolume.MasterVolumeLevelScalar;muted=masterDevice.AudioEndpointVolume.Mute;
        }catch{master=0;muted=true;if(masterDevice!=null){masterDevice.Dispose();masterDevice=null;}Notice("系统默认播放设备暂时不可用，已静音；正在重试。",false);}
        UpdateMasterUI();
    }
    void UpdateMasterUI(){if(inTray)return;syncingMaster=true;C<Slider>("MasterDisplay").Value=muted?0:master*100;syncingMaster=false;C<ProgressBar>("IdleMasterBar").Value=muted?0:master*100;SetText("MasterValue",muted?"静音":Math.Round(master*100)+"%");SetText("MasterMode",On("FollowSystem")?"跟随系统":"应用音量");}
    void Apply(){
        if(!ready)return;
        float a=(float)C<Slider>("LeftTrim").Value/100,b=(float)C<Slider>("RightTrim").Value/100;
        SetText("LeftTrimValue",Math.Round(a*100)+"%");SetText("RightTrimValue",Math.Round(b*100)+"%");
        SetText("ModeCaption",C<ComboBox>("Profile").SelectedIndex==1?"稳定模式":"低延迟模式");
        if(engine==null)return;
        engine.A.Volume=muted?0:a*master;engine.B.Volume=muted?0:b*master;
        engine.A.Delay=(int)settings.LeftDelay;engine.B.Delay=(int)settings.RightDelay;
        engine.A.Mode=Math.Max(0,C<ComboBox>("LeftMode").SelectedIndex);engine.B.Mode=Math.Max(0,C<ComboBox>("RightMode").SelectedIndex);
        engine.Ring.Test=Math.Max(0,C<ComboBox>("TestTone").SelectedIndex);
        bool visualize=On("Visuals")&&!inTray&&Window.WindowState!=WindowState.Minimized;
        engine.A.Meter.Enabled=engine.B.Meter.Enabled=visualize;
    }
    bool ApplyDelay(bool normalize){
        double a,b;
        if(!double.TryParse(C<TextBox>("LeftDelay").Text,out a)||!double.TryParse(C<TextBox>("RightDelay").Text,out b)||double.IsNaN(a)||double.IsNaN(b)||a<0||b<0||a>2000||b>2000){Notice("额外延迟请输入 0–2000 ms 的数值。",true);return false;}
        a=Math.Round(a);b=Math.Round(b);if(normalize){double common=Math.Min(a,b);a-=common;b-=common;}
        settings.LeftDelay=a;settings.RightDelay=b;C<TextBox>("LeftDelay").Text=a.ToString("0");C<TextBox>("RightDelay").Text=b.ToString("0");Apply();Save();return true;
    }
    void Start(){
        if(preview)return;
        if(Id("SourceDevice")==""||Id("LeftDevice")==""||Id("RightDevice")==""){ResetRun();Notice("请选择声音来源和两只音箱。设备可以是任意 Windows 已连接的播放设备。",true);return;}
        if(!ApplyDelay(false)){ResetRun();return;}
        try{
            engine=new Engine(Id("SourceDevice"),Id("LeftDevice"),Id("RightDevice"),LatencyProfile.All[Math.Max(0,C<ComboBox>("Profile").SelectedIndex)]);
            ReadMaster();Apply();engine.Start();EnableSetup(false);SetText("Status","正在聆听");C<Ellipse>("StatusDot").Fill=new SolidColorBrush(Color.FromRgb(113,224,198));updating=true;C<CheckBox>("RunSwitch").IsChecked=true;updating=false;autoPending=false;Save();
            Notice("已连接。音频应输出到所选声音来源；如无声音，请检查系统 / 播放器输出。",false);
        }catch(Exception ex){Stop();Notice("启动失败："+ex.Message+"\n请检查设备连接，或改用稳定模式。",true);}
    }
    void ResetRun(){updating=true;C<CheckBox>("RunSwitch").IsChecked=false;updating=false;}
    void EnableSetup(bool value){foreach(string n in new[]{"SourceDevice","LeftDevice","RightDevice","RefreshDevices","Profile"})C<Control>(n).IsEnabled=value;}
    void Stop(){
        if(engine!=null){engine.Dispose();engine=null;}ResetRun();EnableSetup(true);SetText("Status","已停止");C<Ellipse>("StatusDot").Fill=new SolidColorBrush(Color.FromRgb(130,146,169));SetText("LeftBuffer","— ms");SetText("RightBuffer","— ms");SetText("Diagnostics","尚未开始转发");
    }
    void RefreshAudioControls(){
        if(disposed)return;ReadMaster();Apply();
        if(engine!=null){
            if(engine.Error!=null){string error=engine.Error;Stop();Notice("音频连接中断："+error+"\n重新连接后刷新设备，再开启播放。",true);return;}
            if(!inTray){
                SetText("LeftBuffer",engine.A.BufferedMs.ToString("0")+" ms");SetText("RightBuffer",engine.B.BufferedMs.ToString("0")+" ms");

                SetText("Diagnostics",string.Format("软件欠载：左 {0} / 右 {1}\n有效软件音量：左 {2:0.#}% / 右 {3:0.#}%\n总音量设备：{4}",engine.A.Underruns,engine.B.Underruns,engine.A.Volume*100,engine.B.Volume*100,On("FollowSystem")&&masterDevice!=null?masterDevice.FriendlyName:"相对音量直通"));
            }
        }
    }
    void Tick(){
        if(disposed||inTray||Window.WindowState==WindowState.Minimized)return;
        HideTrim("LeftTrimEditor",leftHide);HideTrim("RightTrimEditor",rightHide);CheckIdle();
        if(On("Visuals")){
            int rate=engine==null?48000:engine.Ring.Rate;leftSpectrum.Update(engine==null?null:engine.A.Meter,rate);rightSpectrum.Update(engine==null?null:engine.B.Meter,rate);
            Liquid("LeftLiquid",leftSpectrum,false,false);Liquid("RightLiquid",rightSpectrum,true,false);
        }
    }
    void Liquid(string name,SpectrumView view,bool right,bool demo){
        LiquidVisual.Update(C<System.Windows.Shapes.Path>(name),clock.Elapsed.TotalSeconds,view,right,demo);
        PositionAmbient();
    }
    void Save(){
        if(!ready||preview)return;
        if(Id("SourceDevice")!="")settings.SourceId=Id("SourceDevice");if(Id("LeftDevice")!="")settings.LeftId=Id("LeftDevice");if(Id("RightDevice")!="")settings.RightId=Id("RightDevice");
        settings.LeftTrim=C<Slider>("LeftTrim").Value;settings.RightTrim=C<Slider>("RightTrim").Value;
        settings.Profile=C<ComboBox>("Profile").SelectedIndex;settings.LeftMode=C<ComboBox>("LeftMode").SelectedIndex;settings.RightMode=C<ComboBox>("RightMode").SelectedIndex;settings.FollowSystem=On("FollowSystem");settings.Visuals=On("Visuals");settings.Transparency=On("Transparency");settings.MinimizeToTray=On("MinimizeToTray");settings.AutoStart=On("AutoStart");
        try{settings.Save();}catch(Exception ex){Notice("设置未保存："+ex.Message,false);}
    }
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();DisposeDesktop();if(engine!=null){engine.Dispose();engine=null;}if(masterDevice!=null){masterDevice.Dispose();masterDevice=null;}}
}
static class Program {
    internal static EventWaitHandle ActivateSignal;
    internal static bool StartupLaunch;
    [STAThread] static int Main(string[] args){
        if(args.Length>0&&args[0]=="--render"){
            var c=new Controller(true);c.Window.Show();c.Window.UpdateLayout();Render(c.Window,args.Length>1?args[1]:"preview.png");c.OpenDashboard();c.Window.UpdateLayout();Render(c.Window,args.Length>2?args[2]:"dashboard.png");c.Dispose();c.Window.Close();return 0;
        }
        bool owns;using(var mutex=new Mutex(true,"Local\\redokeillo.LumaStereo.v1.1.2",out owns)){
            if(!owns){try{using(var signal=EventWaitHandle.OpenExisting("Local\\redokeillo.LumaStereo.v1.1.2.activate"))signal.Set();}catch{}return 0;}
            StartupLaunch=args.Contains("--startup");ActivateSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\redokeillo.LumaStereo.v1.1.2.activate");
            var app=new Application();app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show("出现错误："+e.Exception.Message,"Luma Stereo");e.Handled=true;};
            try{var c=new Controller(false);app.Run(c.Window);ActivateSignal.Dispose();return 0;}catch(Exception ex){MessageBox.Show("无法启动："+ex.Message,"Luma Stereo");return 1;}
        }
    }
    static void Render(Window window,string path){
        var root=(FrameworkElement)window.Content;int w=(int)root.ActualWidth,h=(int)root.ActualHeight;
        var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var s=File.Create(path))encoder.Save(s);
    }
}
