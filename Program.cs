// DesktopHamster: slower wheel runs, in-wheel rests, clear feet, no foreground bars, curled sleeping.
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
namespace DesktopHamster;
internal static class Program {
 [STAThread] static void Main(){using var m=new Mutex(true,@"Local\DesktopHamsterBuddy",out bool first);if(!first)return;ApplicationConfiguration.Initialize();Application.Run(new Buddy());}
}
public sealed class SavedState {
 public double Hunger{get;set;}=25; public double Energy{get;set;}=85; public double Happiness{get;set;}=75; public int Food{get;set;}=24;
}
internal sealed class Prop {
 public string Kind; public PointF Position; public bool Visible;
 public Prop(string kind){Kind=kind;}
 public RectangleF Bounds=>Kind=="Wheel"?new(Position.X-108,Position.Y-125,216,215):new(Position.X-60,Position.Y-60,120,120);
}
internal sealed class Buddy:Form {
 readonly Random rng=new(); readonly Stopwatch clock=Stopwatch.StartNew();
 readonly System.Windows.Forms.Timer timer=new(){Interval=33};
 readonly Dictionary<int,Prop> props=new(){[1]=new("Food"),[2]=new("Bed"),[3]=new("Toy"),[4]=new("Ball"),[5]=new("Wheel")};
 readonly ContextMenuStrip menu=new(); readonly NotifyIcon tray; readonly List<int> keys=new(); readonly List<Rectangle> windows=new();
 readonly List<(float x,float y,float len,Color color)> fur=new();
 readonly string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopHamster","state.json");
 SavedState stats=new(); Prop? dragging;
 readonly ToolStripMenuItem hungerItem=new(){Enabled=false},energyItem=new(){Enabled=false},happinessItem=new(){Enabled=false},foodItem=new(){Enabled=false},activityItem=new(){Enabled=false};
 double nextCareUpdate,petStarted,lastPetReward=-10;
 PointF pet,target,offset; int goal; bool hidden,paused,ball,right=true;
 string activity="Resting"; double last,chooseAt,until,nextBite,playStart,nextScan,nextSave; float spin;
 double simulationTime,nextRest=40,wheelCooldown;bool wheelNeedsRecovery;int wheelBursts;
 float wheelAngle,wheelSpeed;
 const double NormalDrain=.18,WheelDrain=NormalDrain*4,RestGain=.65;
 double Now=>simulationTime;
 bool OnWheel=>goal==5&&(activity=="Running on wheel"||activity=="Resting on wheel");
 public Buddy(){
  try{if(File.Exists(file))stats=JsonSerializer.Deserialize<SavedState>(File.ReadAllText(file))??new();}catch{}
  stats.Hunger=Math.Clamp(stats.Hunger,0,100);stats.Energy=Math.Clamp(stats.Energy,0,100);stats.Happiness=Math.Clamp(stats.Happiness,0,100);stats.Food=Math.Clamp(stats.Food,0,24);
  try{string? exe=Environment.ProcessPath;if(exe!=null)Icon=System.Drawing.Icon.ExtractAssociatedIcon(exe);}catch{}
  Text="Desktop Hamster";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
  Bounds=SystemInformation.VirtualScreen;BackColor=Color.Magenta;TransparencyKey=Color.Magenta;DoubleBuffered=true;
  var area=Screen.PrimaryScreen!.WorkingArea;pet=new(area.Left+area.Width/2f-Left,area.Bottom-110-Top);target=pet;
  var seed=new Random(71);for(int i=0;i<1700;i++){int v=seed.Next(-22,23);fur.Add((-51+(float)seed.NextDouble()*111,-42+(float)seed.NextDouble()*74,1.4f+(float)seed.NextDouble()*2.4f,Color.FromArgb(145,194+v,145+v,85+v)));}
  menu.ShowItemToolTips=true;
  menu.Items.Add(new ToolStripMenuItem("Hamster care"){Enabled=false});
  hungerItem.ToolTipText="Lower hunger is better.";energyItem.ToolTipText="Higher energy is better.";happinessItem.ToolTipText="Higher happiness is better.";
  menu.Items.AddRange(new ToolStripItem[]{hungerItem,energyItem,happinessItem,foodItem,activityItem,new ToolStripSeparator()});
  menu.Opening+=(_,_)=>UpdateCare();
  menu.Items.Add("Call to pointer",null,(_,_)=>Call());menu.Items.Add("Hide / show",null,(_,_)=>ToggleHidden());
  var pause=new ToolStripMenuItem("Pause"){CheckOnClick=true};pause.CheckedChanged+=(_,_)=>{paused=pause.Checked;last=clock.Elapsed.TotalSeconds;};menu.Items.Add(pause);
  var refill=new ToolStripMenuItem("Refill food bowl"){ShortcutKeyDisplayString="Ctrl+Alt+E"};refill.Click+=(_,_)=>RefillFood();menu.Items.Add(refill);menu.Items.Add("Exit",null,(_,_)=>Close());
  tray=new(){Icon=Icon??SystemIcons.Information,Text="Desktop Hamster - click for care",ContextMenuStrip=menu,Visible=true};
  tray.MouseClick+=(_,e)=>{if(e.Button==MouseButtons.Left){UpdateCare();menu.Show(Cursor.Position);}};
  UpdateCare();
  timer.Tick+=(_,_)=>Step();Shown+=(_,_)=>{RegisterKeys();timer.Start();};
 }
 protected override bool ShowWithoutActivation=>true;
 protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
 void RegisterKeys(){Keys[] list={Keys.F,Keys.B,Keys.T,Keys.Q,Keys.R,Keys.H,Keys.E,Keys.W};var failed=new List<string>();for(int i=0;i<list.Length;i++){if(RegisterHotKey(Handle,i+1,0x4003,(uint)list[i]))keys.Add(i+1);else failed.Add("Ctrl+Alt+"+list[i]);}if(failed.Count>0){tray.BalloonTipTitle="Shortcut unavailable";tray.BalloonTipText=string.Join(", ",failed)+" is in use. Close the conflicting app and restart.";tray.ShowBalloonTip(6000);}}
 protected override void WndProc(ref Message m){if(m.Msg==0x312){int id=m.WParam.ToInt32();if(id<=4)Toggle(id);else if(id==5)Call();else if(id==6)ToggleHidden();else if(id==7)RefillFood();else if(id==8)Toggle(5);return;}base.WndProc(ref m);}
 static float Dist(PointF a,PointF b)=>MathF.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
 PointF Clamp(PointF p){var r=Screen.FromPoint(PointToScreen(Point.Round(p))).WorkingArea;r.Offset(-Left,-Top);return new(Math.Clamp(p.X,r.Left+80,Math.Max(r.Left+80,r.Right-80)),Math.Clamp(p.Y,r.Top+80,Math.Max(r.Top+80,r.Bottom-80)));}
 void Cancel(){goal=0;until=0;target=pet;chooseAt=Now+1;activity="Resting";}
 void ToggleHidden(){hidden=!hidden;last=clock.Elapsed.TotalSeconds;Invalidate();}
 void Toggle(int id){var p=props[id];p.Visible=!p.Visible;if(p.Visible)p.Position=ClampProp(p,PointToClient(Cursor.Position));if(id==4&&!p.Visible)ball=false;if(goal==id){if(id==5&&OnWheel)LeaveWheel();else Cancel();}if(dragging==p){dragging=null;Capture=false;}Invalidate();}
 void Call(){if(OnWheel)LeaveWheel();nextRest=Now+30;hidden=false;goal=0;until=0;target=Clamp(PointToClient(Cursor.Position));chooseAt=Now+90;activity="Walking";}
 PointF Approach(int id){var p=props[id].Position;return id==1?Clamp(new(p.X-80,p.Y-5)):id==3?Clamp(new(p.X-72,p.Y)):id==5?new(p.X,p.Y+29):p;}
 PointF ClampProp(Prop p,PointF value){
  if(p.Kind!="Wheel")return Clamp(value);
  var r=Screen.FromPoint(PointToScreen(Point.Round(value))).WorkingArea;r.Offset(-Left,-Top);
  return new(Math.Clamp(value.X,r.Left+115,Math.Max(r.Left+115,r.Right-115)),Math.Clamp(value.Y,r.Top+128,Math.Max(r.Top+128,r.Bottom-120)));
 }
 void BeginRest(bool tired=false){
  goal=0;target=pet;activity="Taking a breather";until=Now+(tired?Math.Clamp((55-stats.Energy)/RestGain,18,90):rng.Next(8,15));chooseAt=until;nextRest=until+rng.Next(35,66);
 }
 void LeaveWheel(){
  bool tired=stats.Energy<25;if(tired)wheelNeedsRecovery=true;
  var p=props[5].Position;pet=Clamp(new(p.X-148,p.Y+29));wheelCooldown=Now+rng.Next(45,81);BeginRest(tired);
  if(tired&&props[2].Visible&&dragging!=props[2]){goal=2;target=Approach(2);until=0;chooseAt=Now+90;activity="Walking";}
 }
 void StartWheelRun(){right=true;activity="Running on wheel";until=Now+rng.Next(7,13);chooseAt=until;}

 void Choose(){
  until=0;if(ball&&(stats.Energy<35||rng.NextDouble()<.25))ball=false;
  if(!ball&&stats.Energy<25){if(props[2].Visible&&dragging!=props[2])goal=2;else{BeginRest(true);return;}}
  else if(!ball&&props[1].Visible&&dragging!=props[1]&&stats.Food>0&&stats.Hunger>28)goal=1;
  else if(!ball&&props[2].Visible&&dragging!=props[2]&&stats.Energy<45)goal=2;
  else if(!ball&&Now>=nextRest){BeginRest();return;}
  else{
   int[] list=props.Where(k=>k.Value.Visible&&k.Value!=dragging&&(k.Key!=1||stats.Food>0)&&(k.Key!=5||(stats.Energy>50&&!wheelNeedsRecovery&&Now>=wheelCooldown))).Select(k=>k.Key).ToArray();
   goal=!ball&&list.Length>0&&rng.NextDouble()<.65?list[rng.Next(list.Length)]:0;
  }
  if(goal>0)target=Approach(goal);else{var r=Screen.FromPoint(PointToScreen(Point.Round(pet))).WorkingArea;target=Clamp(new(r.Left-Left+(float)rng.NextDouble()*r.Width,r.Top-Top+(float)rng.NextDouble()*r.Height));}chooseAt=Now+18;
 }
 void Step(){
  double real=clock.Elapsed.TotalSeconds;float dt=(float)Math.Clamp(real-last,0,.1);last=real;
  if(real>nextSave){Save();nextSave=real+30;}
  if(hidden||paused||dragging?.Kind=="Ball"&&ball){UpdateCare();return;}
  simulationTime+=dt;double now=Now;if(now>=nextCareUpdate){UpdateCare();nextCareUpdate=now+.25;}
  stats.Hunger=Math.Min(100,stats.Hunger+dt*.06);stats.Happiness=Math.Max(0,stats.Happiness-dt*.015);
  bool running=activity=="Running on wheel",resting=activity is "Taking a breather" or "Resting" or "Resting on wheel";
  if(activity=="Sleeping")stats.Energy=Math.Min(100,stats.Energy+dt*5);
  else if(resting)stats.Energy=Math.Min(100,stats.Energy+dt*RestGain);
  else stats.Energy=Math.Max(0,stats.Energy-dt*(running?WheelDrain:NormalDrain));
  if(stats.Energy>50)wheelNeedsRecovery=false;
  float desired=running ? 85 : 0;wheelSpeed+=(desired-wheelSpeed)*Math.Min(1,dt*3);if(!running&&wheelSpeed<.2f)wheelSpeed=0;
  wheelAngle=(wheelAngle+wheelSpeed*dt)%360;
  if(now>nextScan){ReadWindows();nextScan=now+1;}
  if(goal>0&&(!props[goal].Visible||dragging==props[goal])){if(OnWheel)LeaveWheel();else Cancel();}
  if(OnWheel){
   pet=Approach(5);
   if(stats.Energy<25){LeaveWheel();Invalidate();return;}
   if(running)stats.Happiness=Math.Min(100,stats.Happiness+dt*.8);
   if(now>=until){
    if(running){wheelBursts++;activity="Resting on wheel";until=now+rng.Next(7,12);chooseAt=until;}
    else if(wheelBursts>=3)LeaveWheel();
    else StartWheelRun();
   }
   Invalidate();return;
  }
  if(now<until){
   if(activity=="Eating"){
    if(stats.Food<=0||stats.Hunger<=0){Cancel();Invalidate();return;}
    if(now>=nextBite){stats.Food--;stats.Hunger=Math.Max(0,stats.Hunger-5);nextBite=now+1.15;}
   }else if(activity=="Playing")stats.Happiness=Math.Min(100,stats.Happiness+dt*8);
   Invalidate();return;
  }
  if(activity=="Taking a breather"){Cancel();chooseAt=now;}
  if(goal>0&&(activity is "Eating" or "Sleeping" or "Playing")){if(activity=="Sleeping")nextRest=now+rng.Next(35,66);Cancel();}
  if(goal==0&&activity=="Walking"&&!ball&&now>=nextRest){BeginRest();Invalidate();return;}
  if(now>=chooseAt)Choose();if(now<until){Invalidate();return;}
  if(goal>0)target=Approach(goal);float d=Dist(pet,target);
  if(d<4){
   pet=target;
   if(goal==4){ball=true;goal=0;chooseAt=0;activity="Resting";props[4].Position=pet;}
   else if(goal==1){right=true;activity="Eating";until=now+10;chooseAt=until;nextBite=now+1.15;}
   else if(goal==2){activity="Sleeping";until=now+12;chooseAt=until;}
   else if(goal==3){right=true;activity="Playing";playStart=now;until=now+6;chooseAt=until;}
   else if(goal==5){if(stats.Energy<=50||wheelNeedsRecovery||now<wheelCooldown)BeginRest();else{wheelBursts=0;StartWheelRun();}}
   else{activity="Resting";if(chooseAt>now+3)chooseAt=now+2;}
  }else{
   activity=ball?"Rolling":"Walking";float dx=(target.X-pet.X)/d,dy=(target.Y-pet.Y)/d,speed=ball?120:58;right=dx>=0;
   PointF next=Clamp(new(pet.X+dx*Math.Min(d,speed*dt),pet.Y+dy*Math.Min(d,speed*dt)));bool hit=false;
   if(goal==0&&rng.NextDouble()<.35){Point a=PointToScreen(Point.Round(pet)),b=PointToScreen(Point.Round(next));foreach(var r in windows)if(r.Contains(a)!=r.Contains(b)){hit=true;break;}}
   if(hit){activity="Investigating window edge";until=now+1.2;target=Clamp(new(pet.X-dx*100,pet.Y-dy*100));chooseAt=now+4;}else pet=next;
   if(ball){props[4].Position=pet;spin+=speed*dt*.8f;}
  }Invalidate();
 }
 void ReadWindows(){windows.Clear();EnumWindows((h,l)=>{
  if(h==Handle||!IsWindowVisible(h)||IsIconic(h))return true;GetWindowThreadProcessId(h,out uint pid);if(pid==(uint)Environment.ProcessId)return true;
  var name=new StringBuilder(128);GetClassName(h,name,128);if(name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return true;
  if(DwmGetWindowAttribute(h,14,out int cloaked,4)==0&&cloaked!=0)return true;RECT r;if(DwmGetWindowAttribute(h,9,out r,Marshal.SizeOf<RECT>())!=0&&!GetWindowRect(h,out r))return true;
  if(r.Right-r.Left>180&&r.Bottom-r.Top>100)windows.Add(Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom));return true;},IntPtr.Zero);}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Right){menu.Show(Cursor.Position);return;}if(e.Button!=MouseButtons.Left)return;if(!ball&&HitHamster(e.Location)){PetHamster();return;}foreach(var p in props.Values.Reverse()){if(!p.Visible||!p.Bounds.Contains(e.Location))continue;dragging=p;offset=new(p.Position.X-e.X,p.Position.Y-e.Y);Capture=true;if(goal>0&&props[goal]==p){if(p.Kind=="Wheel"&&OnWheel)LeaveWheel();else Cancel();}return;}}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging==null)return;dragging.Position=ClampProp(dragging,new(e.X+offset.X,e.Y+offset.Y));if(dragging.Kind=="Ball"&&ball){pet=dragging.Position;target=pet;chooseAt=Now+1;}Invalidate();}
 protected override void OnMouseUp(MouseEventArgs e){dragging=null;Capture=false;base.OnMouseUp(e);}
 void RefillFood(){stats.Food=24;Save();UpdateCare();Invalidate();}
 void UpdateCare(){
  hungerItem.Text=$"Hunger: {stats.Hunger:0}/100 (lower is better)";energyItem.Text=$"Energy: {stats.Energy:0}/100";
  happinessItem.Text=$"Happiness: {stats.Happiness:0}/100";foodItem.Text=$"Food remaining: {stats.Food}/24 portions";
  activityItem.Text="Activity: "+(hidden?"Hidden / paused":paused?"Paused":activity);
  string summary=$"Hunger {stats.Hunger:0}% | Energy {stats.Energy:0}%\nHappy {stats.Happiness:0}% | Food {stats.Food}/24";
  if(tray.Text!=summary)tray.Text=summary;
 }
 bool HitHamster(Point point){
  float x=point.X-pet.X,y=point.Y-pet.Y;if(!right)x=-x;
  if(activity=="Sleeping"){using var curled=CurledBody();return curled.IsVisible(x,y);}
  using var body=Body();using var head=Head();return body.IsVisible(x,y)||head.IsVisible(x,y)||new RectangleF(15,-42,18,21).Contains(x,y);
 }
 void PetHamster(){
  if(hidden||paused||ball)return;if(OnWheel)LeaveWheel();
  double now=Now;goal=0;target=pet;petStarted=now;until=now+2.4;chooseAt=until+.5;activity="Enjoying pets";
  if(now-lastPetReward>=.7){stats.Happiness=Math.Min(100,stats.Happiness+2);lastPetReward=now;}UpdateCare();Invalidate();
 }
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(hidden)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;foreach(var p in props.Values)if(p.Visible&&!(ball&&p.Kind=="Ball"))DrawProp(g,p);DrawHamster(g);if(ball)DrawBall(g,pet,true);}
 static void E(Graphics g,Color c,float x,float y,float w,float h){using var b=new SolidBrush(c);g.FillEllipse(b,x,y,w,h);}
 static void L(Graphics g,Color c,float width,float x,float y,float xx,float yy){using var p=new Pen(c,width){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawLine(p,x,y,xx,yy);}
 static GraphicsPath Body(){var p=new GraphicsPath();p.AddBezier(-49,10,-53,-13,-42,-34,-21,-37);p.AddBezier(-21,-37,-1,-42,20,-34,33,-23);p.AddBezier(33,-23,39,-13,43,7,33,19);p.AddBezier(33,19,14,32,-29,30,-43,22);p.AddBezier(-43,22,-47,20,-49,15,-49,10);p.CloseFigure();return p;}
 static GraphicsPath Head(){var p=new GraphicsPath();p.AddBezier(7,-28,21,-38,39,-30,44,-18);p.AddBezier(44,-18,47,-8,55,-6,58,-1);p.AddBezier(58,-1,58,5,49,8,45,12);p.AddBezier(45,12,37,24,17,20,10,11);p.AddBezier(10,11,3,0,1,-19,7,-28);p.CloseFigure();return p;}
 void Paw(Graphics g,float x,float y,float lift){using var p=new GraphicsPath();p.AddBezier(x,y-lift,x+3,y-3-lift,x+8,y-1-lift,x+10,y+2-lift);p.AddBezier(x+10,y+2-lift,x+12,y+5-lift,x+5,y+6-lift,x,y+3-lift);p.CloseFigure();using var b=new LinearGradientBrush(new RectangleF(x-1,y-4-lift,15,12),Color.FromArgb(230,189,177),Color.FromArgb(167,116,106),90);g.FillPath(b,p);for(int i=0;i<3;i++)L(g,Color.FromArgb(138,100,91),.6f,x+5+i*2,y+2-lift,x+5+i*2,y+4-lift);}
 void DrawHamster(Graphics g){
  var saved=g.Save();g.TranslateTransform(pet.X,pet.Y);if(!right)g.ScaleTransform(-1,1);
  bool wheelRun=activity=="Running on wheel",moving=activity is "Walking" or "Rolling" or "Running on wheel",eating=activity=="Eating",petted=activity=="Enjoying pets"&&Now<until;
  bool resting=activity is "Taking a breather" or "Resting" or "Resting on wheel";
  float petTime=(float)(Now-petStarted),petEase=petted?Math.Clamp(petTime/.28f,0,1)*Math.Clamp((float)(until-Now)/.45f,0,1):0;
  float stride=moving?MathF.Sin((float)Now*(wheelRun?8:12)):0;
  E(g,Color.FromArgb(97,88,76),-44,27,93,6);
  if(activity!="Sleeping"){Paw(g,-28,24,Math.Max(0,stride)*(wheelRun?6:2.5f));Paw(g,23,23,Math.Max(0,-stride)*(wheelRun?6:2.5f));}
  g.TranslateTransform(0,moving?MathF.Abs(stride)*(wheelRun?-.5f:-1.2f):MathF.Sin((float)Now*(resting||activity=="Sleeping"?1.8f:2.7f))*(resting||activity=="Sleeping" ? .8f : .5f));
  if(petted){g.TranslateTransform(0,22);g.RotateTransform((-2.3f+MathF.Sin(petTime*7)*.8f)*petEase);g.TranslateTransform(0,-22);g.TranslateTransform(1.5f*petEase,MathF.Sin(petTime*8)*.6f*petEase);}
  if(activity=="Sleeping"){DrawSleeping(g);g.Restore(saved);return;}
  E(g,Color.FromArgb(175,139,102),-54,10,10,7);
  using(var body=Body()){using var b=new LinearGradientBrush(new Rectangle(-54,-44,110,80),Color.FromArgb(226,191,130),Color.FromArgb(132,89,50),78);g.FillPath(b,body);var clip=g.Save();g.SetClip(body,CombineMode.Intersect);E(g,Color.FromArgb(236,219,187),-25,-3,76,39);foreach(var f in fur)L(g,f.color,.6f,f.x,f.y,f.x-f.len,f.y+f.len*.4f);g.Restore(clip);}
  E(g,Color.FromArgb(151,108,75),15,-42,18,21);E(g,Color.FromArgb(220,178,162),18,-39,12,15);E(g,Color.FromArgb(190,143,130),21,-37,6,11);
  var hs=g.Save();if(petted){g.TranslateTransform(15,-11);g.RotateTransform(-7*petEase);g.TranslateTransform(-15,11);}if(eating){g.TranslateTransform(15,-11);g.RotateTransform(5f+(float)Math.Sin(Now*5)*.7f);g.TranslateTransform(-15,11);}
  using(var head=Head()){using var b=new LinearGradientBrush(new Rectangle(0,-36,63,59),Color.FromArgb(234,203,151),Color.FromArgb(168,121,77),73);g.FillPath(b,head);var clip=g.Save();g.SetClip(head,CombineMode.Intersect);E(g,Color.FromArgb(244,229,199),29,-3,35,26);for(int i=0;i<100;i++){float x=9+(i*17%47),y=-30+(i*11%48);L(g,Color.FromArgb(110,227,198,150),.6f,x,y,x-2.5f,y+1.3f);}g.Restore(clip);}
  E(g,Color.FromArgb(124,89,58),29,-21,11,12);if(petted||resting&&Now%6<3){using var eye=new Pen(Color.FromArgb(35,26,20),1.5f){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawArc(eye,31,-19,8,6,12,156);}else if(Now%5.6<.13)L(g,Color.FromArgb(35,26,20),1.6f,31,-15,39,-15);else{E(g,Color.FromArgb(24,21,18),31,-21,8,10);E(g,Color.WhiteSmoke,32,-20,2.2f,2.2f);}
  E(g,Color.FromArgb(199,143,135),54,-3,6,4);L(g,Color.FromArgb(109,76,57),.7f,54,2,52,6);
  for(int i=0;i<4;i++)L(g,Color.FromArgb(167,153,130),.6f,48,5,65+i*2,-4+i*4+(float)Math.Sin(Now*11+i)*.45f);
  if(eating){float phase=(float)((Now-nextBite+1.15)/1.15);bool lifting=phase>.43f&&phase<.94f;float lift=lifting?MathF.Sin((phase-.43f)/.51f*MathF.PI)*2.3f:0;L(g,Color.FromArgb(214,188,147),4,34,16,43,13-lift);Paw(g,42,12-lift,0);if(lifting&&stats.Food>0)E(g,Color.FromArgb(173,116,55),51,8-lift,3,3);L(g,Color.FromArgb(93,63,39),.65f,52,8,55,8+(float)Math.Sin(Now*17)*.45f);}
  g.Restore(hs);
  if(wheelRun){DrawRunningLeg(g,-26,(float)Now*8);DrawRunningLeg(g,26,(float)Now*8+MathF.PI);}
  if(activity=="Playing"){float reach=MathF.Pow(Math.Max(0,MathF.Sin((float)(Now-playStart)*7)),3);L(g,Color.FromArgb(211,183,144),4,29,19,41+reach*10,17);Paw(g,38+reach*10,17,0);}g.Restore(saved);
 }
 void DrawProp(Graphics g,Prop p){
  float x=p.Position.X,y=p.Position.Y;if(p.Kind=="Wheel"){DrawWheel(g,p);return;}if(p.Kind=="Ball"){DrawBall(g,p.Position,false);return;}E(g,Color.FromArgb(109,100,85),x-44,y+21,88,7);
  if(p.Kind=="Food"){
   using var bowl=new LinearGradientBrush(new RectangleF(x-40,y-8,80,39),Color.LightSteelBlue,Color.FromArgb(58,100,132),90);g.FillEllipse(bowl,x-40,y-8,80,37);E(g,Color.FromArgb(204,222,230),x-42,y-16,84,30);E(g,Color.FromArgb(92,85,70),x-35,y-12,70,22);E(g,Color.FromArgb(133,133,116),x-30,y-9,60,15);
   for(int i=0;i<stats.Food;i++){float a=i*2.39996f,r=5+(i%6)*4.5f,xx=x+MathF.Cos(a)*r,yy=y-3+MathF.Sin(a)*r*.25f;E(g,i%3==0?Color.Wheat:i%3==1?Color.Peru:Color.SaddleBrown,xx-3,yy-2,6,4);L(g,Color.FromArgb(104,78,44),.5f,xx-1,yy-1,xx+1,yy+1);}using var rim=new Pen(Color.FromArgb(181,203,215),2);g.DrawArc(rim,x-40,y-14,80,27,0,180);
  }else if(p.Kind=="Bed"){
   E(g,Color.FromArgb(125,85,64),x-48,y-25,96,58);E(g,Color.FromArgb(172,132,104),x-44,y-27,88,45);E(g,Color.FromArgb(224,210,180),x-33,y-20,66,33);for(int i=0;i<30;i++)L(g,i%2==0?Color.Wheat:Color.Tan,1,x-28+i*2,y+4,x-24+i*2,y-12);
  }else{
   bool playing=activity=="Playing"&&goal==3&&!paused&&!hidden&&dragging!=p;float hop=0,roll=0,nudge=0;
   if(playing){float phase=(float)(Now-playStart)*2.6f,cycle=phase-MathF.Floor(phase);hop=4*cycle*(1-cycle)*16;nudge=MathF.Sin(phase*MathF.PI)*9;roll=nudge*2.4f;}
   var s=g.Save();g.TranslateTransform(x+nudge,y-hop);g.RotateTransform(roll);using var b=new LinearGradientBrush(new RectangleF(-24,-24,48,48),Color.FromArgb(213,134,94),Color.FromArgb(116,62,39),40);g.FillEllipse(b,-24,-24,48,48);using var seam=new Pen(Color.FromArgb(228,204,161),3);g.DrawArc(seam,-14,-23,24,46,-90,180);E(g,Color.FromArgb(233,175,130),-10,-16,9,5);g.Restore(s);
  }
 }
 // The entire wheel is painted BEFORE the hamster; no front bars cover him.
 void DrawWheel(Graphics g,Prop p){
  float x=p.Position.X,y=p.Position.Y,cy=y-25;
  E(g,Color.FromArgb(105,99,86),x-108,y+78,218,9);
  L(g,Color.FromArgb(54,68,72),13,x+10,cy,x-47,y+75);L(g,Color.FromArgb(54,68,72),13,x+10,cy,x+64,y+75);
  using(var baseBrush=new LinearGradientBrush(new RectangleF(x-96,y+69,192,16),Color.FromArgb(120,146,145),Color.FromArgb(45,64,65),90)){
   using var basePath=new GraphicsPath();basePath.AddBezier(x-96,y+82,x-94,y+69,x-79,y+69,x-65,y+69);basePath.AddLine(x-65,y+69,x+75,y+69);basePath.AddBezier(x+75,y+69,x+90,y+69,x+99,y+76,x+99,y+82);basePath.CloseFigure();g.FillPath(baseBrush,basePath);
  }
  using(var depth=new LinearGradientBrush(new RectangleF(x-91,cy-94,198,188),Color.FromArgb(101,131,130),Color.FromArgb(37,64,67),0))g.FillEllipse(depth,x-82,cy-94,188,188);
  using(var outer=new LinearGradientBrush(new RectangleF(x-94,cy-94,188,188),Color.FromArgb(215,197,155),Color.FromArgb(132,103,64),125))g.FillEllipse(outer,x-94,cy-94,188,188);
  using(var inner=new LinearGradientBrush(new RectangleF(x-83,cy-83,166,166),Color.FromArgb(67,91,91),Color.FromArgb(133,160,155),100))g.FillEllipse(inner,x-83,cy-83,166,166);
  using(var track=new Pen(Color.FromArgb(183,157,110),11))g.DrawArc(track,x-77,cy-77,154,154,12,156);
  using(var lip=new Pen(Color.FromArgb(239,220,177),2))g.DrawEllipse(lip,x-87,cy-87,174,174);
  var saved=g.Save();g.TranslateTransform(x,cy);g.RotateTransform(wheelAngle);
  for(int i=0;i<24;i++){float a=i*MathF.PI/12;L(g,Color.FromArgb(139,112,74),1.6f,MathF.Cos(a)*86,MathF.Sin(a)*86,MathF.Cos(a)*91,MathF.Sin(a)*91);}
  g.Restore(saved);
  using var highlight=new Pen(Color.FromArgb(243,230,203),2);g.DrawArc(highlight,x-91,cy-91,182,182,207,67);
 }
 void DrawRunningLeg(Graphics g,float anchor,float phase){
  float swing=MathF.Cos(phase)*7,lift=Math.Max(0,MathF.Sin(phase))*8;
  float footX=anchor+swing,footY=27-lift;
  L(g,Color.FromArgb(187,143,96),4.8f,anchor-3,14,footX,footY-1);
  L(g,Color.FromArgb(226,199,156),2.4f,anchor-2,14,footX,footY-2);
  Paw(g,footX-3,footY,0);
 }
 static GraphicsPath CurledBody(){
  var p=new GraphicsPath();p.AddBezier(-46,15,-57,-9,-42,-35,-15,-38);p.AddBezier(-15,-38,12,-42,38,-26,43,-2);
  p.AddBezier(43,-2,51,20,30,31,7,30);p.AddBezier(7,30,-17,34,-39,30,-46,15);p.CloseFigure();return p;
 }
 void DrawSleeping(Graphics g){
  using(var curled=CurledBody()){
   using var coat=new LinearGradientBrush(new RectangleF(-54,-43,108,78),Color.FromArgb(229,194,135),Color.FromArgb(132,88,52),90);g.FillPath(coat,curled);
   var clip=g.Save();g.SetClip(curled,CombineMode.Intersect);
   using var undercoat=new LinearGradientBrush(new RectangleF(-48,-30,95,66),Color.FromArgb(20,244,222,181),Color.FromArgb(135,100,65,38),90);g.FillRectangle(undercoat,-48,-30,95,66);
   foreach(var f in fur)L(g,f.color,.65f,f.x,f.y,f.x-f.len*.8f,f.y+f.len*.65f);
   g.Restore(clip);
  }
  // A tucked, fur-textured head nestled against the flank, not a white disk.
  using var head=new GraphicsPath();head.AddBezier(2,1,10,-7,23,-4,29,5);head.AddBezier(29,5,34,10,39,14,35,19);
  head.AddBezier(35,19,28,28,9,29,0,20);head.AddBezier(0,20,-5,13,-3,6,2,1);head.CloseFigure();
  using(var shade=new Pen(Color.FromArgb(110,110,76,48),3))g.DrawArc(shade,-2,-6,43,37,12,126);
  using(var face=new LinearGradientBrush(new RectangleF(-5,-8,45,40),Color.FromArgb(221,186,133),Color.FromArgb(172,125,80),65))g.FillPath(face,head);
  var headClip=g.Save();g.SetClip(head,CombineMode.Intersect);
  using(var chin=new LinearGradientBrush(new RectangleF(8,10,31,20),Color.FromArgb(65,242,221,185),Color.FromArgb(170,232,210,172),80))g.FillRectangle(chin,8,10,31,20);
  for(int i=0;i<180;i++){float xx=-3+(i*17%41),yy=-5+(i*13%34);L(g,i%3==0?Color.FromArgb(145,238,208,160):Color.FromArgb(105,189,147,99),.6f,xx,yy,xx-2,yy+1.6f);}
  g.Restore(headClip);
  E(g,Color.FromArgb(157,113,79),5,-8,15,17);E(g,Color.FromArgb(211,165,150),8,-5,9,11);
  using(var earShade=new Pen(Color.FromArgb(176,128,107),.8f))g.DrawArc(earShade,9,-3,7,8,200,130);
  using(var eye=new Pen(Color.FromArgb(56,40,28),1.3f){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawArc(eye,21,7,8,6,8,155);
  E(g,Color.FromArgb(191,135,126),32,16,4,3);L(g,Color.FromArgb(118,80,59),.6f,31,20,33,20);
  for(int i=0;i<3;i++)L(g,Color.FromArgb(160,182,163,136),.55f,30,19,40+i*2,17+i*3);
  // Only a small paw tip shows beneath the chin; the other paws are tucked away.
  Paw(g,16,24,0);
  using(var fold=new Pen(Color.FromArgb(95,116,79,47),1.1f))g.DrawArc(fold,-34,-20,61,47,115,108);
 }
 void DrawBall(Graphics g,PointF p,bool occupied){var s=g.Save();g.TranslateTransform(p.X,p.Y);using var edge=new Pen(Color.SteelBlue,2);g.DrawEllipse(edge,-65,-65,130,130);g.RotateTransform(occupied?spin:0);using var seam=new Pen(Color.LightSteelBlue,1.5f);g.DrawEllipse(seam,-34,-65,68,130);g.DrawEllipse(seam,-65,-23,130,46);for(int i=0;i<8;i++){float a=i*MathF.PI/4;L(g,Color.CadetBlue,2,MathF.Cos(a)*54,MathF.Sin(a)*54,MathF.Cos(a)*60,MathF.Sin(a)*60);}g.Restore(s);using var shine=new Pen(Color.WhiteSmoke,3);g.DrawArc(shine,p.X-58,p.Y-58,116,116,205,45);}
 void Save(){try{Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file+".tmp",JsonSerializer.Serialize(stats));File.Move(file+".tmp",file,true);}catch{}}
 protected override void OnFormClosed(FormClosedEventArgs e){timer.Stop();Save();foreach(int k in keys)UnregisterHotKey(Handle,k);tray.Visible=false;tray.Dispose();menu.Dispose();timer.Dispose();base.OnFormClosed(e);}
 [StructLayout(LayoutKind.Sequential)]struct RECT{public int Left,Top,Right,Bottom;}
 delegate bool WindowCallback(IntPtr h,IntPtr l);
 [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
 [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr l);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder text,int count);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out RECT value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out int value,int size);
}
