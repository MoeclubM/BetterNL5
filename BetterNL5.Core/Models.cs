using System;

namespace BetterNL5.Core
{
    public enum PowerMode
    {
        High = 0,
        Gaming = 1,
        Audio = 2,
    }

    public enum LedModule
    {
        Game = 0,
        Perf = 1,
        Audio = 2,
        Custom = 3,
    }

    public enum LedZone
    {
        Logo = 1,
        Trunk = 2,
        Led1 = 3,
        Led2 = 4,
        Led3 = 5,
    }

    public enum FanProfile
    {
        UserFan1 = 0,
        UserFan2 = 1,
        UserFan3 = 2,
        UserFan1Sys = 3,
        UserFan2Sys = 4,
        UserFan3Sys = 5,
    }

    public sealed class FanTelemetry
    {
        public byte CpuTemp { get; set; }

        public byte GpuTemp { get; set; }

        public byte SysTemp { get; set; }

        public ushort CpuRpm { get; set; }

        public ushort GpuRpm { get; set; }

        public ushort SysRpm { get; set; }
    }

    public sealed class FanStatus
    {
        public int StoredFanControlStatus { get; set; }

        public int RawFanControlStatus { get; set; }

        public int StoredFanControlProfile { get; set; }

        public bool SupportsThreeFan { get; set; }

        public FanTelemetry Telemetry { get; set; } = new FanTelemetry();
    }

    public sealed class FanSpeedCommand
    {
        public byte CpuPercent { get; set; }

        public byte GpuPercent { get; set; }

        public byte SysPercent { get; set; }
    }

    public sealed class ControlCenterStatus
    {
        public string InstallDir { get; set; } = string.Empty;

        public bool WmiAvailable { get; set; }

        public ushort BiosWord { get; set; }

        public string PowerScheme { get; set; } = string.Empty;

        public uint StoredPowerMode { get; set; }

        public bool SupportsFirmwarePowerMode { get; set; }

        public bool TypeCAdapter { get; set; }

        public FanStatus Fan { get; set; } = new FanStatus();

        public uint LedModule { get; set; }
    }

    public sealed class ControlCenterException : Exception
    {
        public ControlCenterException(string message)
            : base(message)
        {
        }
    }
}
