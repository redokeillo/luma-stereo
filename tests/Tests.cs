using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
static class Tests {
    static void Assert(bool test,string name){if(!test)throw new Exception(name);Console.WriteLine("PASS "+name);}
    [STAThread] static int Main(string[] args){
        try{
            if(args.Length==4&&args[0]=="--hardware"){
                foreach(var profile in LatencyProfile.All)using(var e=new Engine(args[1],args[2],args[3],profile)){
                    e.A.Volume=e.B.Volume=0;e.Start();
                    for(int i=0;i<100;i++){Thread.Sleep(100);AssertQuiet(e.Error==null,e.Error);}
                    Assert(e.Ring.Written>0,"capture frames");
                    Console.WriteLine(profile.Name+": underruns="+e.A.Underruns+"/"+e.B.Underruns+", ring ms="+e.A.BufferedMs+"/"+e.B.BufferedMs+", largest capture batch ms="+e.Ring.CaptureBatchFrames*1000.0/e.Ring.Rate);
                }return 0;
            }
            Assert(LatencyProfile.All.Length==2&&LatencyProfile.All[0].RingMs==25&&LatencyProfile.All[1].RingMs==150,"two requested profiles");
            Assert(Math.Abs(GainControl.Effective(.8f,.5f,true,false)-.4f)<.00001,"relative gain");
            Assert(GainControl.Effective(.8f,.5f,true,true)==0,"system mute");
            var ring=new AudioRing(48000);for(int i=0;i<48000;i++)ring.Add(.25f,-.5f);
            var left=new OutputFeed(ring,0);var right=new OutputFeed(ring,1);left.BaseBufferMs=right.BaseBufferMs=25;right.Delay=100;
            byte[] bytes=new byte[480*8];left.Read(bytes,0,bytes.Length);float l=BitConverter.ToSingle(bytes,479*8);Assert(l>0&&l==BitConverter.ToSingle(bytes,479*8+4),"left routed to both speaker channels");
            right.Read(bytes,0,bytes.Length);float r=BitConverter.ToSingle(bytes,479*8);Assert(r<0&&r==BitConverter.ToSingle(bytes,479*8+4),"right routed to both speaker channels");
            Assert(Math.Abs(right.BufferedMs-left.BufferedMs-100)<.1,"independent delay");
            ring.CaptureBatchFrames=2400;var guarded=new OutputFeed(ring,0);guarded.BaseBufferMs=25;guarded.Read(bytes,0,bytes.Length);Assert(guarded.BufferedMs>=65,"driver batch guard exceeds nominal target when necessary");
            left.Delay=2000;left.Read(bytes,0,bytes.Length);Assert(bytes.All(b=>b==0),"long-delay prefill silence");
            var tap=new SpectrumTap();var spectrum=new SpectrumView();
            for(int i=0;i<8192;i++)tap.Push((float)(.4*Math.Sin(2*Math.PI*1000*i/48000)));tap.Publish();spectrum.Update(tap,48000);
            int peak=Array.IndexOf(spectrum.Bands,spectrum.Bands.Max());Assert(peak>=5&&peak<=7,"FFT detects 1 kHz band");
            tap.Enabled=false;Assert(!tap.CopyLatest(new float[1024]),"visualization opt-out");
            Assert(SpectrumView.PerceivedLevel(0)==0&&SpectrumView.PerceivedLevel(.001)<SpectrumView.PerceivedLevel(.1),"visual energy follows absolute audio level");
            Assert(LiquidVisual.MotionSpeed(0)*10<LiquidVisual.MotionSpeed(1),"quiet motion much slower than loud motion");
            using(var icon=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("redo-luma.ico")){Assert(icon!=null&&icon.Length>1000,"embedded brand tray icon");}
            var settings=new Settings();Assert(settings.FollowSystem,"system master default");settings.LeftTrim=double.NaN;settings.Profile=12;settings.Validate();Assert(settings.LeftTrim==0&&settings.Profile==0,"invalid settings bounded");
            Assert(!settings.AutoStart&&settings.MinimizeToTray&&settings.Transparency,"desktop feature defaults, autostart opt-in");
            Assert(StartupRegistration.Command(@"C:\Apps With Spaces\LumaStereo.exe")=="\"C:\\Apps With Spaces\\LumaStereo.exe\" --startup","quoted startup executable");
            Assert(Settings.FilePath.Contains("LumaStereo-v1.1.2"),"isolated v1.1.2 settings");
            bool rejected=false;try{new Engine("same","same","other");}catch(ArgumentException){rejected=true;}Assert(rejected,"feedback prevention before device initialization");
            using(var ui=new Controller(true)){
                ui.Window.Show();ui.Window.UpdateLayout();Pump();
                Assert(ui.C<CheckBox>("FollowSystem").IsChecked==true,"default follow switch");
                var device=new DeviceItem{Id="test-generic-device",Name="Any brand / Bluetooth speaker"};ui.C<ComboBox>("LeftDevice").ItemsSource=new[]{device};ui.C<ComboBox>("LeftDevice").SelectedItem=device;
                Assert(ui.C<TextBlock>("LeftName").Text==device.Name,"generic device name, no brand filter");
                ui.C<ComboBox>("LeftDevice").ItemsSource=new DeviceItem[0];ui.C<TextBlock>("LeftName").Text="Bluetooth Speaker · L";ui.C<TextBlock>("RightName").Text="Bluetooth Speaker · R";
                ui.C<Slider>("LeftTrim").Value=80;ui.C<Slider>("RightTrim").Value=60;
                Assert(ui.C<Slider>("LeftTrim").IsEnabled&&ui.C<Slider>("RightTrim").Value==60,"independent editable trims");
                Assert(ui.C<Slider>("MasterDisplay")!=null&&ui.C<Slider>("MasterDisplay").IsHitTestVisible,"master volume is editable");
                ui.C<Slider>("MasterDisplay").Value=37;Assert(ui.C<TextBlock>("MasterValue").Text=="37%"&&ui.C<Slider>("LeftTrim").Value==80&&ui.C<Slider>("RightTrim").Value==60,"master editing preserves relative trims without system writes in preview");
                double collapsed=ui.C<Border>("LeftCard").ActualHeight;
                ui.ShowTrim(true);Assert(Until(()=>ui.C<Grid>("LeftTrimEditor").ActualHeight>=51&&ui.C<Border>("LeftCard").ActualHeight>collapsed+45),"volume expands card smoothly");
                ui.ShowTrim(true);Assert(Until(()=>ui.C<Grid>("LeftTrimEditor").ActualHeight<1&&Math.Abs(ui.C<Border>("LeftCard").ActualHeight-collapsed)<1),"volume collapses without empty slot");
                ui.SetIdle(true);Assert(Until(()=>ui.IsIdle&&ui.C<Border>("LeftCard").Opacity<.01&&ui.C<Grid>("IdleBars").Opacity>.99),"idle slides cards to edges");
                Assert(Until(()=>ui.C<Border>("MasterPanel").Opacity<.01),"idle master panel animates away");
                Assert(ui.C<ProgressBar>("IdleMasterBar").Value==ui.C<Slider>("MasterDisplay").Value,"idle bar preserves system volume");
                if(args.Length>0){Directory.CreateDirectory(args[0]);Render(ui.Window,Path.Combine(args[0],"idle.png"));}
                ui.SetIdle(false);Assert(Until(()=>ui.C<Border>("LeftCard").Opacity>.99),"idle animation restores cards");
                var visualTimer=(System.Windows.Threading.DispatcherTimer)typeof(Controller).GetField("timer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(ui);visualTimer.Start();
                ui.ToTray();Assert(ui.IsInTray&&!ui.Window.IsVisible&&!ui.VisualTimerRunning,"tray stops visual timer and hides window");ui.RestoreFromTray();Pump();Assert(!ui.IsInTray&&ui.Window.IsVisible,"restore from tray");
                ui.C<Button>("MaximizeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Assert(ui.Window.WindowState==WindowState.Maximized&&ui.C<Border>("WindowOutline").CornerRadius.TopLeft==0,"maximized window has square outer corners");
                var wallpaper=ui.C<Image>("MaximizedWallpaper");Console.WriteLine("Maximized wallpaper loaded: "+(wallpaper.Source!=null));
                if(wallpaper.Source!=null)Assert(wallpaper.Visibility==Visibility.Visible,"maximized wallpaper is visible");
                ui.C<CheckBox>("Transparency").IsChecked=false;Assert(wallpaper.Visibility==Visibility.Collapsed,"disable transparency hides maximized wallpaper");ui.C<CheckBox>("Transparency").IsChecked=true;

                ui.C<Button>("MaximizeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Assert(ui.Window.WindowState==WindowState.Normal&&ui.C<Border>("WindowOutline").CornerRadius.TopLeft==32,"restored window has 32 DIP outer radius");
                Assert(ui.C<Image>("MaximizedWallpaper").Visibility==Visibility.Collapsed,"wallpaper layer hidden after restoring window");
                Assert(!ui.C<Image>("PszSignature").IsVisible,"signature absent from main page");
                ui.OpenDashboard();Pump();Assert(ui.C<Image>("PszSignature").IsVisible,"signature belongs to dashboard");ui.C<Grid>("Dashboard").Visibility=Visibility.Collapsed;
                bool startupBefore=StartupRegistration.IsEnabled();ui.C<CheckBox>("AutoStart").IsChecked=true;ui.C<CheckBox>("AutoStart").IsChecked=false;Assert(startupBefore==StartupRegistration.IsEnabled(),"preview never changes startup registration");
                ui.C<CheckBox>("Transparency").IsChecked=false;Assert(((SolidColorBrush)ui.C<Grid>("Root").Background).Color.A==255,"opaque performance fallback");ui.C<CheckBox>("Transparency").IsChecked=true;
                Console.WriteLine("Native backdrop: "+ui.NativeBackdropStatus);
                ui.C<Slider>("LeftTrim").Value=100;ui.C<Slider>("RightTrim").Value=100;
                if(args.Length>0){Directory.CreateDirectory(args[0]);Render(ui.Window,Path.Combine(args[0],"preview.png"));ui.OpenDashboard();ui.Window.UpdateLayout();Pump();Render(ui.Window,Path.Combine(args[0],"dashboard.png"));}
                ui.Window.Close();
            }
            Console.WriteLine("All tests passed.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void AssertQuiet(bool value,string message){if(!value)throw new Exception(message);}
    static bool Until(Func<bool> condition){var clock=System.Diagnostics.Stopwatch.StartNew();while(!condition()&&clock.ElapsedMilliseconds<3000)Pump(50);return condition();}
    static void Pump(int ms=500){var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);}
    static void Render(Window w,string path){var root=(FrameworkElement)w.Content;var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(path))encoder.Save(f);}
}
