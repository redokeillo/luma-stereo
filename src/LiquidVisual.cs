using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

// Freeform mesh-like colour, never a circular speaker illustration.
// Geometry, colour motion and grain are UI-only and stop with the visual timer.
static class LiquidVisual {
    sealed class Motion {public double Last=-1,Phase;}
    static readonly ConditionalWeakTable<Path,Motion> motions=new ConditionalWeakTable<Path,Motion>();
    static readonly DrawingBrush grain=MakeGrain();
    internal static double ScaleForLevel(double level){return .64+.96*Math.Max(0,Math.Min(1,level));}
    internal static double MotionSpeed(double level){return .035+1.8*level;}
    public static void Update(Path path,double time,SpectrumView spectrum,bool right,bool demo){
        double level=demo?.58:spectrum.Level;
        double low=demo?.65:Mean(spectrum,0),mid=demo?.5:Mean(spectrum,4),high=demo?.4:Mean(spectrum,8);
        var motion=motions.GetOrCreateValue(path);double dt=motion.Last<0?0:Math.Max(0,Math.Min(.1,time-motion.Last));motion.Last=time;motion.Phase+=dt*MotionSpeed(level);
        double phase=motion.Phase+(right?2.7:0);
        path.Data=Morph(phase,level,low,mid,high);
        var group=(TransformGroup)path.RenderTransform;var scale=(ScaleTransform)group.Children[0];var rotate=(RotateTransform)group.Children[1];var move=(TranslateTransform)group.Children[2];
        scale.ScaleX=ScaleForLevel(level)*(1+(.008+.025*level)*Math.Sin(phase*.7));scale.ScaleY=ScaleForLevel(level)*(1+(.008+.025*level)*Math.Cos(phase*.9));
        rotate.Angle=(right?-1:1)*(Math.Sin(phase*.4)*(4+level*20));move.X=Math.Sin(phase*.57)*(1+level*35);move.Y=Math.Cos(phase*.43)*(1+level*24);
        path.Fill=Mesh(phase,level,high,right);path.Opacity=.24+.73*level;
    }
    static DrawingBrush Mesh(double phase,double level,double high,bool right){
        var drawing=new DrawingGroup();using(var c=drawing.Open()){
            c.DrawRectangle(new SolidColorBrush(Color.FromRgb(135,159,194)),null,new Rect(0,0,380,380));
            double[] hues={178,221,270,316,9,62,111};
            for(int i=0;i<hues.Length;i++){
                double angle=i*2*Math.PI/hues.Length+(right?.7:0);
                double cx=190+Math.Cos(angle+Math.Sin(phase*.65+i)*.55)*118+Math.Sin(phase+i*1.3)*28;
                double cy=190+Math.Sin(angle+Math.Cos(phase*.72+i)*.55)*118+Math.Cos(phase*.8+i)*24;
                Color colour=Hsv(hues[i]+Math.Sin(phase*.3+i)*12+high*9,.65+.25*level,1,235);
                var brush=new RadialGradientBrush();brush.GradientStops.Add(new GradientStop(colour,0));brush.GradientStops.Add(new GradientStop(Color.FromArgb(150,colour.R,colour.G,colour.B),.45));brush.GradientStops.Add(new GradientStop(Color.FromArgb(0,colour.R,colour.G,colour.B),1));
                c.DrawEllipse(brush,null,new Point(cx,cy),155+Math.Sin(phase+i)*16,155+Math.Cos(phase*.8+i)*16);
            }
            var pearl=new RadialGradientBrush(Color.FromArgb(70,245,250,255),Colors.Transparent);c.DrawEllipse(pearl,null,new Point(110,75),170,130);
            c.DrawRectangle(grain,null,new Rect(0,0,380,380));
        }
        drawing.Freeze();var fill=new DrawingBrush(drawing){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,380,380),Stretch=Stretch.Fill};fill.Freeze();return fill;
    }
    static DrawingBrush MakeGrain(){
        var tile=new DrawingGroup();using(var c=tile.Open()){
            c.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,18,18));var random=new Random(111);
            var light=new SolidColorBrush(Color.FromArgb(40,255,255,255));var shade=new SolidColorBrush(Color.FromArgb(19,20,28,50));
            for(int i=0;i<25;i++)c.DrawEllipse(i%3==0?shade:light,null,new Point(random.NextDouble()*18,random.NextDouble()*18),.38,.38);
        }tile.Freeze();var brush=new DrawingBrush(tile){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,18,18),ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,18,18)};brush.Freeze();return brush;
    }
    static double Mean(SpectrumView s,int start){return(s.Bands[start]+s.Bands[start+1]+s.Bands[start+2]+s.Bands[start+3])/4;}
    internal static StreamGeometry Morph(double t,double level,double low,double mid,double high){
        const int n=48;var points=new Point[n];
        for(int i=0;i<n;i++){double a=2*Math.PI*i/n;double wave=(.025+.045*level)*Math.Sin(3*a+t*1.4)+(.01+.025*mid)*Math.Sin(5*a-t*.9)+.015*high*Math.Cos(2*a+t*2);double radius=140*(1+wave+.08*low);points[i]=new Point(190+Math.Cos(a)*radius,190+Math.Sin(a)*radius);}
        var geometry=new StreamGeometry();using(var c=geometry.Open()){
            c.BeginFigure(points[0],true,true);
            for(int i=0;i<n;i++){Point p0=points[(i+n-1)%n],p1=points[i],p2=points[(i+1)%n],p3=points[(i+2)%n];c.BezierTo(new Point(p1.X+(p2.X-p0.X)/6,p1.Y+(p2.Y-p0.Y)/6),new Point(p2.X-(p3.X-p1.X)/6,p2.Y-(p3.Y-p1.Y)/6),p2,true,false);}
        }geometry.Freeze();return geometry;
    }
    static Color Hsv(double hue,double saturation,double value,byte alpha){
        hue=((hue%360)+360)%360;double chroma=value*saturation,x=chroma*(1-Math.Abs(hue/60%2-1)),m=value-chroma;double r=0,g=0,b=0;
        if(hue<60){r=chroma;g=x;}else if(hue<120){r=x;g=chroma;}else if(hue<180){g=chroma;b=x;}else if(hue<240){g=x;b=chroma;}else if(hue<300){r=x;b=chroma;}else{r=chroma;b=x;}
        return Color.FromArgb(alpha,(byte)((r+m)*255),(byte)((g+m)*255),(byte)((b+m)*255));
    }
}
