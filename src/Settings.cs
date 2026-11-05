using System;
using System.IO;
using System.Xml.Serialization;

public sealed class Settings {
    public string SourceId="",LeftId="",RightId="";
    public double LeftTrim=100,RightTrim=100,LeftDelay=0,RightDelay=0,LocalMaster=50;
    public bool FollowSystem=true,Visuals=true;
    public bool Transparency=true,AutoStart=false,MinimizeToTray=true;
    public int Profile=0,LeftMode=0,RightMode=1;
    public static string FilePath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"redokeillo","LumaStereo-v1.1.2","settings.xml");}}
    public static Settings Load(){
        string path=FilePath;
        if(!File.Exists(path))path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"redokeillo","LumaStereo-v1.1.1","settings.xml");
        if(!File.Exists(path))path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"redokeillo","LumaStereo-v1.1","settings.xml");
        if(!File.Exists(path))path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"redokeillo","LumaStereo","settings.xml");
        try{using(var s=File.OpenRead(path)){var v=(Settings)new XmlSerializer(typeof(Settings)).Deserialize(s);v.Validate();return v;}}catch{return new Settings();}
    }
    public void Validate(){
        LeftTrim=Clamp(LeftTrim,100);RightTrim=Clamp(RightTrim,100);LocalMaster=Clamp(LocalMaster,100);
        LeftDelay=Clamp(LeftDelay,2000);RightDelay=Clamp(RightDelay,2000);
        Profile=Profile==1?1:0;LeftMode=Math.Max(0,Math.Min(3,LeftMode));RightMode=Math.Max(0,Math.Min(3,RightMode));
    }
    static double Clamp(double v,double max){return double.IsNaN(v)||double.IsInfinity(v)?0:Math.Max(0,Math.Min(max,v));}
    public void Save(){
        Validate();Directory.CreateDirectory(Path.GetDirectoryName(FilePath));string temp=FilePath+".tmp";
        using(var s=File.Create(temp))new XmlSerializer(typeof(Settings)).Serialize(s,this);
        if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);
    }
}
sealed class DeviceItem {
    public string Id,Name;
    public string Detail {get{return Name+"  ·  "+Id.Substring(Math.Max(0,Id.Length-9)).TrimEnd('}');}}
    public override string ToString(){return Detail;}
}
