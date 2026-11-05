using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

sealed partial class Controller {
    DesktopFrame desktopFrame;
    Forms.NotifyIcon tray;
    readonly DispatcherTimer controlTimer=new DispatcherTimer();
    bool inTray,idle,leftExpanded,rightExpanded,autoPending,settingStartup,initialRendered;
    DateTime pointerLeft=DateTime.UtcNow,autoDeadline,nextAutoAttempt;
    public bool IsInTray {get{return inTray;}}
    public bool IsIdle {get{return idle;}}
    public bool VisualTimerRunning {get{return timer.IsEnabled;}}
    public string NativeBackdropStatus {get{return desktopFrame==null?"":desktopFrame.BackdropStatus;}}
    internal static double DelayBarHeight(double ms){return 24+Math.Min(120,Math.Max(0,ms)*.35);}
    TimeSpan lastBarFrame;
    static double SmoothBar(double current,double target,double dt){return current+(target-current)*(1-Math.Exp(-dt/0.075));}
    void RenderBufferBars(object sender,EventArgs args){
        var e=(RenderingEventArgs)args;double dt=Math.Min(.1,Math.Max(0,(e.RenderingTime-lastBarFrame).TotalSeconds));lastBarFrame=e.RenderingTime;
        if(disposed||inTray||!Window.IsVisible||Window.WindowState==WindowState.Minimized||!idle)return;
        double left=engine==null?0:engine.A.BufferedMs,right=engine==null?0:engine.B.BufferedMs;
        var a=C<Border>("LeftDelayBar");var b=C<Border>("RightDelayBar");a.Height=SmoothBar(a.Height,DelayBarHeight(left),dt);b.Height=SmoothBar(b.Height,DelayBarHeight(right),dt);
    }
    void InitializeDesktop(){
        if(!preview)CompositionTarget.Rendering+=RenderBufferBars;
        desktopFrame=new DesktopFrame(Window);
        C<CheckBox>("Transparency").IsChecked=settings.Transparency;C<CheckBox>("MinimizeToTray").IsChecked=settings.MinimizeToTray;
        bool registered=false;try{registered=!preview&&StartupRegistration.IsEnabled();}catch{}
        C<CheckBox>("AutoStart").IsChecked=registered;
        Window.SourceInitialized+=(s,e)=>{desktopFrame.Initialize();ApplyBackdrop();};
        Window.SizeChanged+=(s,e)=>{desktopFrame.UpdateCorners();PositionAmbient();ResizeArtwork();C<Border>("WindowOutline").CornerRadius=new CornerRadius(Window.WindowState==WindowState.Maximized?0:DesktopFrame.OuterRadius);};
        C<Button>("MinimizeButton").Click+=(s,e)=>{if(On("MinimizeToTray"))ToTray();else Window.WindowState=WindowState.Minimized;};
        C<Button>("MaximizeButton").Click+=(s,e)=>ToggleMaximize();C<Button>("CloseButton").Click+=(s,e)=>Window.Close();
        C<Grid>("TitleBar").MouseLeftButtonDown+=(s,e)=>{
            if(e.OriginalSource!=C<Grid>("TitleBar"))return;
            if(e.ClickCount==2)ToggleMaximize();else if(Window.WindowState!=WindowState.Maximized)Window.DragMove();
        };
        Window.StateChanged+=(s,e)=>{
            if(Window.WindowState==WindowState.Minimized){if(On("MinimizeToTray"))ToTray();else timer.Stop();}
            else if(!inTray&&!preview){timer.Start();desktopFrame.UpdateCorners();}
            ApplyBackdrop();Apply();
        };
        if(!preview){
            Window.MouseLeave+=(s,e)=>pointerLeft=DateTime.UtcNow;
            Window.MouseEnter+=(s,e)=>{pointerLeft=DateTime.MaxValue;SetIdle(false);};
            Window.PreviewKeyDown+=(s,e)=>{pointerLeft=DateTime.UtcNow;SetIdle(false);};
        }
        C<CheckBox>("Transparency").Checked+=(s,e)=>{ApplyBackdrop();Save();};C<CheckBox>("Transparency").Unchecked+=(s,e)=>{ApplyBackdrop();Save();};
        C<CheckBox>("MinimizeToTray").Checked+=(s,e)=>Save();C<CheckBox>("MinimizeToTray").Unchecked+=(s,e)=>Save();
        C<CheckBox>("AutoStart").Checked+=(s,e)=>SetStartup(true);C<CheckBox>("AutoStart").Unchecked+=(s,e)=>SetStartup(false);
        if(!preview){CreateTray();controlTimer.Interval=TimeSpan.FromMilliseconds(200);controlTimer.Tick+=(s,e)=>{
            RefreshAudioControls();PollAutoStart();if(Program.ActivateSignal!=null&&Program.ActivateSignal.WaitOne(0))RestoreFromTray();
        };controlTimer.Start();}
        Window.ContentRendered+=(s,e)=>{
            if(initialRendered)return;initialRendered=true;
            desktopFrame.UpdateCorners();
            if(!Window.IsMouseOver)pointerLeft=DateTime.UtcNow;
            if(!preview&&On("AutoStart")){autoPending=true;autoDeadline=DateTime.UtcNow.AddSeconds(60);nextAutoAttempt=DateTime.MinValue;PollAutoStart();}
            if(!preview&&Program.StartupLaunch&&On("MinimizeToTray"))ToTray();
        };
    }
    void ApplyBackdrop(){
        bool enabled=On("Transparency");desktopFrame.SetBackdrop(enabled);
        bool supported=desktopFrame.BackdropStatus.IndexOf("不支持",StringComparison.Ordinal)>=0;
        C<Grid>("Root").Background=new SolidColorBrush(enabled&&!supported?Color.FromArgb(125,12,19,32):Color.FromRgb(12,19,32));
        UpdateWallpaper(enabled);
        SetText("DesktopStatus",desktopFrame.BackdropStatus+" · 托盘中停止频谱与界面动画");
    }
    void PositionAmbient(){
        double w=Math.Max(880,Window.ActualWidth),h=Math.Max(720,Window.ActualHeight);
        Canvas.SetLeft(C<System.Windows.Shapes.Path>("LeftLiquid"),w*.25-170);Canvas.SetLeft(C<System.Windows.Shapes.Path>("RightLiquid"),w*.75-190);
        Canvas.SetTop(C<System.Windows.Shapes.Path>("LeftLiquid"),h*.48-180);Canvas.SetTop(C<System.Windows.Shapes.Path>("RightLiquid"),h*.48-180);
    }
    void ResizeArtwork(){
        double h=Math.Max(45,Math.Min(115,Window.ActualHeight-675));double size=Math.Min(108,h);
        foreach(string name in new[]{"LeftArtwork","RightArtwork"}){var g=C<Grid>(name);g.Height=h;var outer=(System.Windows.Shapes.Ellipse)g.Children[0];var inner=(System.Windows.Shapes.Ellipse)g.Children[1];outer.Width=outer.Height=size;inner.Width=inner.Height=size*86/108;((TextBlock)g.Children[2]).FontSize=42*size/108;}
    }
    void SetStartup(bool value){
        if(settingStartup||!ready)return;
        if(preview)return; // test/preview never modifies registry
        try{StartupRegistration.SetEnabled(value);settings.AutoStart=value;Save();Notice(value?"已启用当前账号的登录自启；以后每次打开会按保存的设备自动播放。移动程序后请重新开关此选项。":"已关闭登录自启与打开即播放。",false);}
        catch(Exception ex){settingStartup=true;C<CheckBox>("AutoStart").IsChecked=!value;settingStartup=false;Notice("未能更新自启设置："+ex.Message,true);}
    }
    void PollAutoStart(){
        if(!autoPending||engine!=null||DateTime.UtcNow<nextAutoAttempt)return;
        if(DateTime.UtcNow>autoDeadline){autoPending=false;Notice("自动播放未启动：设备未就绪。请连接音箱后手动开启。",false);return;}
        nextAutoAttempt=DateTime.UtcNow.AddSeconds(3);LoadDevices();
        if(Id("SourceDevice")==""||Id("LeftDevice")==""||Id("RightDevice")==""){Notice("等待已保存的音频设备连接……",false);return;}
        Start();
    }
    void ToggleMaximize(){SetIdle(false);Window.WindowState=Window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;desktopFrame.UpdateCorners();C<Border>("WindowOutline").CornerRadius=new CornerRadius(Window.WindowState==WindowState.Maximized?0:DesktopFrame.OuterRadius);}
    static Drawing.Icon LoadBrandIcon(){
        using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("redo-luma.ico"))
        using(var icon=new Drawing.Icon(stream,Forms.SystemInformation.SmallIconSize))return (Drawing.Icon)icon.Clone();
    }
    void CreateTray(){
        tray=new Forms.NotifyIcon{Text="Luma Stereo 1.1.2 · redokeillo",Icon=LoadBrandIcon(),Visible=false};
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("显示主界面",null,(s,e)=>Window.Dispatcher.BeginInvoke(new Action(RestoreFromTray)));
        menu.Items.Add("播放 / 暂停",null,(s,e)=>Window.Dispatcher.BeginInvoke(new Action(()=>{autoPending=false;if(engine==null)Start();else Stop();})));
        menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,(s,e)=>Window.Dispatcher.BeginInvoke(new Action(()=>Window.Close())));
        tray.ContextMenuStrip=menu;tray.MouseClick+=(s,e)=>{if(e.Button==Forms.MouseButtons.Left)Window.Dispatcher.BeginInvoke(new Action(RestoreFromTray));};
    }
    internal void ToTray(){
        if(inTray)return;Save();inTray=true;timer.Stop();CompositionTarget.Rendering-=RenderBufferBars;SetIdle(false,false);Window.Hide();Window.ShowInTaskbar=false;
        if(tray!=null)tray.Visible=true;Apply();
    }
    internal void RestoreFromTray(){
        if(disposed)return;inTray=false;Window.ShowInTaskbar=true;Window.Show();if(Window.WindowState==WindowState.Minimized)Window.WindowState=WindowState.Normal;
        if(tray!=null)tray.Visible=false;Window.Activate();pointerLeft=DateTime.UtcNow;SetIdle(false,false);desktopFrame.UpdateCorners();
        if(!preview){timer.Start();CompositionTarget.Rendering-=RenderBufferBars;CompositionTarget.Rendering+=RenderBufferBars;}ApplyBackdrop();RefreshAudioControls();if(lastNotice!=null)SetText("Notice",lastNotice);
    }
    void CheckIdle(){if(C<Grid>("Dashboard").Visibility==Visibility.Visible)return;if(!Window.IsMouseOver&&DateTime.UtcNow-pointerLeft>=TimeSpan.FromSeconds(5))SetIdle(true);}
    internal void SetIdle(bool value,bool animate=true){
        if(idle==value&&animate)return;idle=value;
        double distance=Math.Max(Window.ActualWidth,880)*.6;
        SlideCard("LeftCard",value?-distance:0,value?0:1,animate);SlideCard("RightCard",value?distance:0,value?0:1,animate);
        foreach(string name in new[]{"OperationHeader","DashboardButton"}){
            var element=C<FrameworkElement>(name);Animate(element,UIElement.OpacityProperty,value?0:1,animate?420:0);element.IsHitTestVisible=!value;
        }
        Animate(C<Grid>("IdleBars"),UIElement.OpacityProperty,value?1:0,animate?420:0);
        var panel=C<Border>("MasterPanel");var group=(TransformGroup)panel.RenderTransform;var scale=(ScaleTransform)group.Children[0];var move=(TranslateTransform)group.Children[1];
        scale.CenterX=panel.ActualWidth/2;scale.CenterY=panel.ActualHeight/2;
        Animate(scale,ScaleTransform.ScaleXProperty,value?.36:1,animate?420:0);Animate(scale,ScaleTransform.ScaleYProperty,value?.045:1,animate?420:0);
        Animate(move,TranslateTransform.YProperty,value?panel.ActualHeight/2+56:0,animate?420:0);Animate(panel,UIElement.OpacityProperty,value?0:1,animate?420:0);
    }
    void SlideCard(string name,double x,double opacity,bool animate){var card=C<Border>(name);var move=(TranslateTransform)card.RenderTransform;Animate(move,TranslateTransform.XProperty,x,animate?420:0);Animate(card,UIElement.OpacityProperty,opacity,animate?420:0);card.IsHitTestVisible=!idle;}
    internal void ShowTrim(bool left){SetTrim(left,left?!leftExpanded:!rightExpanded);}
    void SetTrim(bool left,bool expanded){
        if(left){leftExpanded=expanded;leftHide=DateTime.UtcNow.AddSeconds(4);}else{rightExpanded=expanded;rightHide=DateTime.UtcNow.AddSeconds(4);}
        var editor=C<Grid>(left?"LeftTrimEditor":"RightTrimEditor");editor.IsHitTestVisible=expanded;Animate(editor,FrameworkElement.HeightProperty,expanded?52:0,260);Animate(editor,UIElement.OpacityProperty,expanded?1:0,220);
    }
    void HideTrim(string name,DateTime deadline){bool left=name=="LeftTrimEditor";var editor=C<Grid>(name);if((left?leftExpanded:rightExpanded)&&DateTime.UtcNow>deadline&&!editor.IsMouseOver&&!editor.IsKeyboardFocusWithin)SetTrim(left,false);}
    static void Animate(DependencyObject target,DependencyProperty property,double to,int ms){
        double from=(double)target.GetValue(property);if(double.IsNaN(from))from=0;
        var element=target as UIElement;var transform=target as System.Windows.Media.Animation.Animatable;
        if(ms==0){if(element!=null)element.BeginAnimation(property,null);else if(transform!=null)transform.BeginAnimation(property,null);target.SetValue(property,to);return;}
        var animation=new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(ms)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut}};
        if(element!=null)element.BeginAnimation(property,animation,HandoffBehavior.SnapshotAndReplace);else if(transform!=null)transform.BeginAnimation(property,animation,HandoffBehavior.SnapshotAndReplace);
    }
    void DisposeDesktop(){CompositionTarget.Rendering-=RenderBufferBars;controlTimer.Stop();if(tray!=null){tray.Visible=false;var icon=tray.Icon;var menu=tray.ContextMenuStrip;tray.Dispose();if(icon!=null)icon.Dispose();if(menu!=null)menu.Dispose();tray=null;}}
}
