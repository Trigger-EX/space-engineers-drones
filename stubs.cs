// Minimal stand-ins for the SE ingame API, matching real member names/types, for syntax/type checking only.
using System;
using System.Collections.Generic;
using System.Text;
namespace VRageMath {
  public struct Vector3 { public float X,Y,Z; public Vector3(float x,float y,float z){X=x;Y=y;Z=z;} }
  public struct Vector3I { public int X,Y,Z; public Vector3I(int x,int y,int z){X=x;Y=y;Z=z;} }
  public struct Vector3D {
    public double X,Y,Z;
    public Vector3D(double x,double y,double z){X=x;Y=y;Z=z;}
    public Vector3D(Vector3 v){X=v.X;Y=v.Y;Z=v.Z;}
    public static Vector3D Zero, Forward, Backward, Up, Down, Left, Right;
    public double Length(){return 0;} public double LengthSquared(){return 0;} public double Normalize(){return 0;}
    public static Vector3D Normalize(Vector3D v){return v;}
    public static double Dot(Vector3D a, Vector3D b){return 0;}
    public static Vector3D Cross(Vector3D a, Vector3D b){return a;}
    public static double Distance(Vector3D a, Vector3D b){return 0;}
    public static Vector3D TransformNormal(Vector3D v, MatrixD m){return v;}
    public static Vector3D Transform(Vector3D v, MatrixD m){return v;}
    public static Vector3D operator+(Vector3D a, Vector3D b){return a;}
    public static Vector3D operator-(Vector3D a, Vector3D b){return a;}
    public static Vector3D operator-(Vector3D a){return a;}
    public static Vector3D operator*(Vector3D a, double b){return a;}
    public static Vector3D operator*(double b, Vector3D a){return a;}
    public static Vector3D operator/(Vector3D a, double b){return a;}
    public static bool operator==(Vector3D a, Vector3D b){return true;}
    public static bool operator!=(Vector3D a, Vector3D b){return false;}
    public override bool Equals(object o){return false;} public override int GetHashCode(){return 0;}
  }
  public struct MatrixD {
    public Vector3D Forward, Backward, Up, Down, Left, Right, Translation;
    public static MatrixD Identity; public static MatrixD Transpose(MatrixD m){return m;}
    public static MatrixD CreateFromAxisAngle(Vector3D a, double ang){return new MatrixD();}
  }
  public struct BoundingBoxD { public Vector3D Size; public Vector3D Center; }
  public struct BoundingSphereD { public Vector3D Center; public double Radius; }
  public static class MathHelper { public static double Clamp(double v,double a,double b){return v;} public static float Clamp(float v,float a,float b){return v;} }
  public struct QuaternionD {}
}
namespace VRage {
  public struct MyFixedPoint { public long RawValue; }
  public struct MyTuple<T1,T2,T3,T4> { public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public MyTuple(T1 a,T2 b,T3 c,T4 d){Item1=a;Item2=b;Item3=c;Item4=d;} }
  public struct MyTuple<T1,T2,T3,T4,T5> { public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public T5 Item5; public MyTuple(T1 a,T2 b,T3 c,T4 d,T5 e){Item1=a;Item2=b;Item3=c;Item4=d;Item5=e;} }
}
namespace VRage.ObjectBuilders { public struct SerializableDefinitionId { public string SubtypeId {get{return "";}} } }
namespace VRage.Game.Components { public class MyEntityComponentBase {} public class MyEntityComponentContainer { public bool TryGet<T>(out T c) where T: MyEntityComponentBase { c = null; return false; } } }
namespace Sandbox.Game.EntityComponents { }
namespace VRage.Game {
  public enum MyCubeSize : byte { Large, Small }
  public struct MyDefinitionId { public static MyDefinitionId Parse(string s){return new MyDefinitionId();} }
  public enum MyRelationsBetweenPlayerAndBlock { NoOwnership, Owner, FactionShare, Neutral, Enemies, Friends }
}
namespace VRage.Game.GUI.TextPanel { public enum ContentType { NONE, TEXT_AND_IMAGE, SCRIPT } }
namespace VRage.Game.ModAPI.Ingame {
  using VRageMath; using VRage;
  public interface IMyInventory { MyFixedPoint CurrentVolume {get;} MyFixedPoint MaxVolume {get;} }
  public interface IMyEntity { VRage.Game.Components.MyEntityComponentContainer Components {get;} MatrixD WorldMatrix {get;} long EntityId {get;} Vector3D GetPosition(); IMyInventory GetInventory(int i); BoundingSphereD WorldVolume {get;} }
  public interface IMySlimBlock { float BuildIntegrity {get;} float CurrentDamage {get;} float MaxIntegrity {get;} Vector3I Position {get;} }
  public interface IMyCubeGrid : IMyEntity { VRage.Game.MyCubeSize GridSizeEnum {get;} Vector3I Min {get;} Vector3I Max {get;} bool CubeExists(Vector3I p); IMySlimBlock GetCubeBlock(Vector3I p); }
  public interface IMyCubeBlock : IMyEntity { VRage.ObjectBuilders.SerializableDefinitionId BlockDefinition {get;} IMyCubeGrid CubeGrid {get;} bool IsFunctional {get;} bool IsWorking {get;} long OwnerId {get;} }
  public enum MyDetectedEntityType { None, Unknown, SmallGrid, LargeGrid, CharacterHuman, CharacterOther, FloatingObject, Asteroid, Planet, Meteor, Missile }
  public struct MyDetectedEntityInfo {
    public long EntityId; public string Name; public MyDetectedEntityType Type; public Vector3D? HitPosition; public Vector3 Velocity;
    public VRage.Game.MyRelationsBetweenPlayerAndBlock Relationship; public BoundingBoxD BoundingBox; public long TimeStamp; public Vector3D Position; public MatrixD Orientation;
    public bool IsEmpty(){return false;}
  }
}
namespace VRage.Game.ModAPI.Ingame.Utilities {
  public struct MyIniValue { public string ToString(string d){return d;} public double ToDouble(double d){return d;} public bool ToBoolean(bool d){return d;} }
  public struct MyIniParseResult { public int LineNo; }
  public class MyIni { public bool TryParse(string s, out MyIniParseResult r){r=new MyIniParseResult();return true;} public bool ContainsKey(string s,string k){return true;}
    public void Set(string s,string k,string v){} public void Set(string s,string k,double v){} public void Set(string s,string k,bool v){}
    public MyIniValue Get(string s,string k){return new MyIniValue();} }
}
namespace Sandbox.ModAPI.Ingame {
  using VRageMath; using VRage.Game.ModAPI.Ingame; using VRage.Game.GUI.TextPanel;
  public interface ITerminalProperty {} public interface IMyTerminalBlock : IMyCubeBlock { ITerminalProperty GetProperty(string id); string CustomName {get;set;} string CustomData {get;set;} bool IsSameConstructAs(IMyTerminalBlock o); }
  public interface IMyFunctionalBlock : IMyTerminalBlock { bool Enabled {get;set;} }
  public interface IMyTextSurface { ContentType ContentType {get;set;} float FontSize {get;set;} string Font {get;set;} bool WriteText(string s, bool append=false); }
  public interface IMyProgrammableBlock : IMyFunctionalBlock { IMyTextSurface GetSurface(int i); }
  public struct MyShipVelocities { public Vector3D LinearVelocity; public Vector3D AngularVelocity; }
  public struct MyShipMass { public float BaseMass, TotalMass, PhysicalMass; }
  public interface IMyShipController : IMyTerminalBlock { bool CanControlShip {get;} bool IsUnderControl {get;} bool DampenersOverride {get;set;}
     MyShipVelocities GetShipVelocities(); Vector3D GetNaturalGravity(); MyShipMass CalculateShipMass(); }
  public interface IMyRemoteControl : IMyShipController {}
  public interface IMyGyro : IMyFunctionalBlock { bool GyroOverride {get;set;} float Pitch {get;set;} float Yaw {get;set;} float Roll {get;set;} }
  public interface IMyThrust : IMyFunctionalBlock { float ThrustOverridePercentage {get;set;} float MaxEffectiveThrust {get;} }
  public interface IMyUserControllableGun : IMyFunctionalBlock {}
  public interface IMySmallMissileLauncherReload : IMyUserControllableGun {} public interface IMySmallGatlingGun : IMyUserControllableGun {}
  public interface IMyLargeTurretBase : IMyUserControllableGun { bool HasTarget {get;} MyDetectedEntityInfo GetTargetedEntity(); }
  public interface IMyTurretControlBlock : IMyFunctionalBlock { bool HasTarget {get;} MyDetectedEntityInfo GetTargetedEntity(); }
  public interface IMyCameraBlock : IMyFunctionalBlock { bool EnableRaycast {get;set;} double AvailableScanRange {get;}
     MyDetectedEntityInfo Raycast(double d, float pitch=0, float yaw=0); MyDetectedEntityInfo Raycast(Vector3D target); }
  public interface IMySensorBlock : IMyFunctionalBlock { void DetectedEntities(List<MyDetectedEntityInfo> l); }
  public interface IMyBatteryBlock : IMyFunctionalBlock { float CurrentOutput {get;} float MaxOutput {get;} float CurrentStoredPower {get;} float MaxStoredPower {get;} }
  public interface IMyGridTerminalSystem { void GetBlocksOfType<T>(List<T> l, Func<T,bool> f=null) where T: class; }
  public static class TerminalBlockExtentions { public static void ApplyAction(this IMyTerminalBlock b, string a){} public static void SetValueFloat(this IMyTerminalBlock b, string n, float v){} public static void SetValueBool(this IMyTerminalBlock b, string n, bool v){} }
  [Flags] public enum UpdateType { None=0, Terminal=1, Trigger=2, Mod=8, Script=16, Update1=32, Update10=64, Update100=128, Once=256, IGC=512 }
  [Flags] public enum UpdateFrequency { None=0, Update1=1, Update10=2, Update100=4, Once=8 }
  public interface IMyGridProgramRuntimeInfo { TimeSpan TimeSinceLastRun {get;} double LastRunTimeMs {get;} UpdateFrequency UpdateFrequency {get;set;} }
  public struct MyIGCMessage { public object Data; public string Tag; public long Source; }
  public interface IMyBroadcastListener { bool HasPendingMessage {get;} MyIGCMessage AcceptMessage(); }
  public enum TransmissionDistance { CurrentConstruct, ConnectedConstructs, AntennaRelay }
  public interface IMyIntergridCommunicationSystem { long Me {get;} IMyBroadcastListener RegisterBroadcastListener(string t);
     void SendBroadcastMessage<T>(string tag, T data, TransmissionDistance d = TransmissionDistance.AntennaRelay); }
  public abstract class MyGridProgram {
    public IMyGridTerminalSystem GridTerminalSystem; public IMyProgrammableBlock Me; public IMyGridProgramRuntimeInfo Runtime;
    public IMyIntergridCommunicationSystem IGC; public string Storage; public Action<string> Echo;
  }
}
