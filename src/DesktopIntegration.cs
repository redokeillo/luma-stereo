using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

static class StartupRegistration {
    const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName="redokeillo.LumaStereo.1.1.2";
    public static string Command(string executable){return "\""+executable+"\" --startup";}
    public static bool IsEnabled(){using(var key=Registry.CurrentUser.OpenSubKey(KeyPath))return key!=null&&string.Equals(key.GetValue(ValueName) as string,Command(Assembly.GetExecutingAssembly().Location),StringComparison.OrdinalIgnoreCase);}
    public static void SetEnabled(bool enabled){using(var key=Registry.CurrentUser.CreateSubKey(KeyPath)){if(enabled)key.SetValue(ValueName,Command(Assembly.GetExecutingAssembly().Location),RegistryValueKind.String);else key.DeleteValue(ValueName,false);}}
}

// Native backdrop is composited by Windows, not a captured wallpaper image.
sealed class DesktopFrame {
    readonly Window window;
    IntPtr hwnd;
    int regionWidth,regionHeight,regionDiameter;
    bool cornerUpdateQueued;
    public string BackdropStatus="尚未初始化";
    public const double OuterRadius=32;
    public DesktopFrame(Window value){window=value;}
    public void Initialize(){
        hwnd=new WindowInteropHelper(window).Handle;
        HwndSource source=HwndSource.FromHwnd(hwnd);
        source.CompositionTarget.BackgroundColor=Colors.Transparent;source.AddHook(Hook);
        // Own the entire client area. WindowChrome and DWM automatic rounding were
        // applying a second, smaller native frame outside the 32-DIP content clip.
        SetWindowLong(hwnd,-16,GetWindowLong(hwnd,-16)&~0x00C40000);
        int noRounding=1;DwmSetWindowAttribute(hwnd,33,ref noRounding,4);
        SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x0027); // FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER
        int dark=1;DwmSetWindowAttribute(hwnd,20,ref dark,4);
        int noColor=unchecked((int)0xFFFFFFFE);DwmSetWindowAttribute(hwnd,35,ref noColor,4);DwmSetWindowAttribute(hwnd,34,ref noColor,4);
        UpdateCorners();
    }
    public void SetBackdrop(bool enabled){
        if(hwnd==IntPtr.Zero)return;
        int backdrop=enabled?3:1;
        var margins=new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1};DwmExtendFrameIntoClientArea(hwnd,ref margins);
        int result=DwmSetWindowAttribute(hwnd,38,ref backdrop,4);
        if(enabled&&result>=0){BackdropStatus="Windows 桌面亚克力";return;}
        // Windows 10 fallback. If unsupported, keep an opaque, readable surface.
        var accent=new Accent{State=enabled?4:0,Flags=2,Color=unchecked((int)0xB8261C12)};
        IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Accent)));
        try{Marshal.StructureToPtr(accent,memory,false);var data=new CompositionData{Attribute=19,Data=memory,Size=Marshal.SizeOf(typeof(Accent))};bool ok=SetWindowCompositionAttribute(hwnd,ref data);BackdropStatus=!enabled?"已关闭透明效果":ok?"Windows 亚克力兼容模式":"系统不支持透明，使用深色背景";}
        catch(EntryPointNotFoundException){BackdropStatus="系统不支持透明，使用深色背景";}
        finally{Marshal.FreeHGlobal(memory);}
    }
    public void UpdateCorners(){
        if(hwnd==IntPtr.Zero||window.WindowState==WindowState.Minimized)return;
        bool maximized=window.WindowState==WindowState.Maximized;
        var root=window.Content as FrameworkElement;
        double radius=maximized?0:OuterRadius;
        if(root!=null)root.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(1,root.ActualWidth),Math.Max(1,root.ActualHeight)),radius,radius);
        int preference=1;DwmSetWindowAttribute(hwnd,33,ref preference,4);
        if(maximized){if(regionDiameter!=0){regionDiameter=0;regionWidth=regionHeight=0;SetWindowRgn(hwnd,IntPtr.Zero,true);}return;}
        RectNative rect;GetWindowRect(hwnd,out rect);
        var source=HwndSource.FromHwnd(hwnd);double scale=source.CompositionTarget.TransformToDevice.M11;
        int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top,diameter=(int)Math.Round(radius*2*scale);
        if(width==regionWidth&&height==regionHeight&&diameter==regionDiameter)return;
        regionWidth=width;regionHeight=height;regionDiameter=diameter;
        IntPtr region=CreateRoundRectRgn(0,0,width,height,diameter,diameter);
        if(SetWindowRgn(hwnd,region,true)==0)DeleteObject(region);
    }
    IntPtr Hook(IntPtr handle,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
        if(message==0x83){handled=true;return IntPtr.Zero;} // WM_NCCALCSIZE: no native frame
        if(message==0x84&&window.WindowState==WindowState.Normal){
            RectNative rect;GetWindowRect(handle,out rect);long packed=lParam.ToInt64();int x=(short)(packed&0xFFFF)-rect.Left,y=(short)((packed>>16)&0xFFFF)-rect.Top;
            int grip=(int)Math.Ceiling(7*HwndSource.FromHwnd(hwnd).CompositionTarget.TransformToDevice.M11);
            bool l=x<grip,r=x>=rect.Right-rect.Left-grip,t=y<grip,b=y>=rect.Bottom-rect.Top-grip;
            int hit=t?(l?13:r?14:12):b?(l?16:r?17:15):l?10:r?11:1;
            handled=true;return new IntPtr(hit);
        }
        if(message==0x47&&!cornerUpdateQueued){cornerUpdateQueued=true;window.Dispatcher.BeginInvoke(new Action(()=>{cornerUpdateQueued=false;UpdateCorners();}),System.Windows.Threading.DispatcherPriority.Loaded);}
        if(message==0x2E0)window.Dispatcher.BeginInvoke(new Action(UpdateCorners),System.Windows.Threading.DispatcherPriority.Loaded);
        if(message==0x24){
            var info=(MinMaxInfo)Marshal.PtrToStructure(lParam,typeof(MinMaxInfo));
            IntPtr monitor=MonitorFromWindow(handle,2);var mi=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};
            if(GetMonitorInfo(monitor,ref mi)){info.MaxPosition.X=mi.Work.Left-mi.Monitor.Left;info.MaxPosition.Y=mi.Work.Top-mi.Monitor.Top;info.MaxSize.X=mi.Work.Right-mi.Work.Left;info.MaxSize.Y=mi.Work.Bottom-mi.Work.Top;Marshal.StructureToPtr(info,lParam,false);handled=true;}
        }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] struct Margins{public int Left,Right,Top,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct Accent{public int State,Flags,Color,Animation;}
    [StructLayout(LayoutKind.Sequential)] struct CompositionData{public int Attribute;public IntPtr Data;public int Size;}
    [StructLayout(LayoutKind.Sequential)] struct PointNative{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct RectNative{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo{public PointNative Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo{public int Size;public RectNative Monitor,Work;public int Flags;}
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
    [DllImport("user32.dll")] static extern bool SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,int flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l,int t,int r,int b,int ew,int eh);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out RectNative rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd,int flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
}
