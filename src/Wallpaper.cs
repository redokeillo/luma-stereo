using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

sealed partial class Controller {
    string wallpaperPath;
    DateTime wallpaperWrite;
    // A cached, decoded wallpaper layer avoids DWM's opaque maximized fallback.
    // No screenshots, desktop capture, or work on the audio callback.
    void UpdateWallpaper(bool enabled){
        var image=C<Image>("MaximizedWallpaper");var tint=C<Border>("WallpaperTint");
        bool show=enabled&&Window.WindowState==WindowState.Maximized;
        if(show){
            try{
                var text=new StringBuilder(32768);SystemParametersInfo(0x0073,(uint)text.Capacity,text,0);
                string path=text.ToString();
                if(!File.Exists(path))path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft","Windows","Themes","TranscodedWallpaper");
                if(File.Exists(path)){
                    DateTime stamp=File.GetLastWriteTimeUtc(path);
                    if(path!=wallpaperPath||stamp!=wallpaperWrite||image.Source==null){
                        var bitmap=new BitmapImage();using(var file=File.OpenRead(path)){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=1920;bitmap.StreamSource=file;bitmap.EndInit();}bitmap.Freeze();image.Source=bitmap;wallpaperPath=path;wallpaperWrite=stamp;
                    }
                }else image.Source=null;
            }catch{image.Source=null;}
        }
        image.Visibility=tint.Visibility=show&&image.Source!=null?Visibility.Visible:Visibility.Collapsed;
        if(show)desktopFrame.BackdropStatus+=image.Source!=null?" · 最大化壁纸磨砂":" · 系统未提供可读的静态壁纸";
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SystemParametersInfo(uint action,uint param,StringBuilder value,uint flags);
}
