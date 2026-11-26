//All from https://github.com/awgil/ffxiv_navmesh/blob/master/vnavmesh/Movement/OverrideCamera.cs.

using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using System.Runtime.InteropServices;
// ReSharper disable All
#pragma warning disable IDE0044

namespace AutoDuty.External;
using System;
using System.Numerics;
using ECommons.DalamudServices;
using ECommons.MathHelpers;
using Logging;

[StructLayout(LayoutKind.Explicit, Size = 0x2B0)]
public unsafe struct CameraEx
{
    [FieldOffset(0x140)] public float DirH; // 0 is north, increases CW
    [FieldOffset(0x144)] public float DirV; // 0 is horizontal, positive is looking up, negative looking down
    [FieldOffset(0x148)] public float InputDeltaHAdjusted;
    [FieldOffset(0x14C)] public float InputDeltaVAdjusted;
    [FieldOffset(0x150)] public float InputDeltaH;
    [FieldOffset(0x154)] public float InputDeltaV;
    [FieldOffset(0x158)] public float DirVMin; // -85deg by default
    [FieldOffset(0x15C)] public float DirVMax; // +45deg by default
}

public sealed unsafe class OverrideCamera : IDisposable
{
    public bool Enabled
    {
        get => this._rmiCameraHook.IsEnabled;
        set
        {
            if (value)
                this._rmiCameraHook.Enable();
            else
                this._rmiCameraHook.Disable();
        }
    }

    public bool IgnoreUserInput; // if true - override even if user tries to change camera orientation, otherwise override only if user does nothing
    public Angle DesiredAzimuth;
    public Angle DesiredAltitude;
    public Angle SpeedH = 360.Degrees(); // per second
    public Angle SpeedV = 360.Degrees(); // per second

    private delegate void RMICameraDelegate(CameraEx* self, int inputMode, float speedH, float speedV);
    [Signature("48 8B C4 53 48 81 EC ?? ?? ?? ?? 44 0F 29 50 ??")]
    private Hook<RMICameraDelegate> _rmiCameraHook = null!;

    public OverrideCamera()
    {
        Svc.Hook.InitializeFromAttributes(this);
        ADLogging.Info($"RMICamera address: 0x{this._rmiCameraHook.Address:X}");
    }

    public void Dispose() => 
        this._rmiCameraHook.Dispose();

    internal void Face(Vector3 pos)
    {
        if (!Player.Available)
            return;

        this.Enabled        = true;
        this.SpeedH         = this.SpeedV = 360.Degrees();
        this.DesiredAzimuth = Angle.FromDirectionXZ(pos - Player.Object!.Position) + 180.Degrees();
        this.DesiredAltitude   = -30.Degrees();
    }

    private void RMICameraDetour(CameraEx* self, int inputMode, float speedH, float speedV)
    {
        this._rmiCameraHook.Original(self, inputMode, speedH, speedV);
        if (this.IgnoreUserInput || inputMode == 0) // let user override...
        {
            float dt     = Framework.Instance()->FrameDeltaTime;
            Angle deltaH = (this.DesiredAzimuth  - self->DirH.Radians()).Normalized();
            Angle deltaV = (this.DesiredAltitude - self->DirV.Radians()).Normalized();
            float maxH   = this.SpeedH.Rad * dt;
            float maxV   = this.SpeedV.Rad    * dt;
            self->InputDeltaH = Math.Clamp(deltaH.Rad, -maxH, maxH);
            self->InputDeltaV = Math.Clamp(deltaV.Rad, -maxV, maxV);
            this.Enabled         = false;
        }
    }
}
