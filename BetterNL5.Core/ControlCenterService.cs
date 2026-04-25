using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace BetterNL5.Core
{
    public sealed class ControlCenterService
    {
        public const string DefaultInstallDir = @"C:\Program Files (x86)\NL5\ControlCenter";

        private const string AssemblyName = "ControlCenter.exe";
        private const string RegistryPath = @"SOFTWARE\WOW6432Node\NL5\ControlCenter";

        private const byte RegLedWizard = 6;
        private const byte RegWinKey = 7;
        private const byte RegLedModule = 8;
        private const byte RegSelectAll = 9;
        private const byte RegPowerPlan = 11;
        private const byte RegFanControlStatus = 16;
        private const byte RegFanControlSelect = 17;

        private const string LedGaming = "61FF0080;61FF0080;61FF0080;61FF0080;61FF0080";
        private const string LedHighPerformance = "12FF0000;12FF0000;12FF0000;12FF0000;12FF0000";
        private const string LedAudio = "1000FFFF;1000FFFF;10FF0000;1000FF00;100000FF";
        private const string LedCustom = "1100FFFF;1100FFFF;1100FFFF;1100FFFF;1100FFFF";

        private static readonly int[] DefaultTemperatures = { 30, 40, 50, 60, 70, 80, 90, 100 };

        private const string NlxbDefaultFan = "0,0,0,0,40,50,60,70;0,0,0,0,30,40,50,70";
        private const string NlzdDefaultFan = "0,0,0,0,40,50,60,70;0,0,0,0,30,40,50,70";
        private const string NlzeDefaultFan = "0,0,0,0,40,50,60,70;0,0,0,0,30,40,50,70";
        private const string NlyDefaultFan = "30,30,30,30,50,60,70,100;30,30,30,30,50,60,70,100";
        private const string NlyDefaultFanSys = "30,30,30,30,50,60,70,100";

        private readonly Assembly assembly;
        private readonly Type myWmiBaseType;
        private readonly Type smiStructType;
        private readonly Type gmRegistyType;
        private readonly Type myPowerPlanType;
        private readonly Type powerPlanSchemeType;
        private readonly Type myPowerPlanCreateType;
        private readonly Type myPowerPlanBaseType;
        private readonly Type ledCtrlType;
        private readonly Type ledCtrlRegLedType;
        private readonly Type ledGroupType;

        private readonly MethodInfo myWmiInit;
        private readonly MethodInfo acpiPerformSmi;
        private readonly MethodInfo gmGetRegistry;
        private readonly MethodInfo gmSetRegistry;
        private readonly MethodInfo gmGetRegistryStr;
        private readonly MethodInfo gmSetRegistryStr;
        private readonly MethodInfo gmGetUserFan;
        private readonly MethodInfo gmSetFanCurve;
        private readonly MethodInfo initSchemeGuid;
        private readonly MethodInfo addNewPowerScheme;
        private readonly MethodInfo ledSetSingleDevice;
        private readonly MethodInfo ledSetAllDevice;
        private readonly MethodInfo groupSaveReg;
        private readonly MethodInfo groupWmiLedControl;

        private readonly FieldInfo wmiDataField;
        private readonly FieldInfo gamingWmiField;
        private readonly FieldInfo gmKeyNameField;
        private readonly FieldInfo chooseGuidField;

        private readonly PropertyInfo mySchemeProperty;
        private readonly PropertyInfo groupStrFormatProperty;
        private readonly PropertyInfo keyboardBrightnessProperty;

        private readonly object wmi;
        private readonly object powerPlan;

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("kernel32.dll", SetLastError = false)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public ControlCenterService(string installDir)
        {
            InstallDir = installDir;

            var assemblyPath = Path.Combine(installDir, AssemblyName);
            if (!File.Exists(assemblyPath))
            {
                throw new ControlCenterException($"Original assembly not found: {assemblyPath}");
            }

            assembly = Assembly.LoadFrom(assemblyPath);

            myWmiBaseType = RequireType("ControlCenterSpace.MyWmiBase");
            smiStructType = RequireType("ControlCenterSpace.SMI_STRUCT_S");
            gmRegistyType = RequireType("ControlCenterSpace.GMRegisty");
            myPowerPlanType = RequireType("ControlCenterSpace.BIOS.MyPowerPlan");
            powerPlanSchemeType = RequireType("ControlCenterSpace.BIOS.MyPowerPlan+SCHEME_SELECT");
            myPowerPlanCreateType = RequireType("ControlCenterSpace.BIOS.MyPowerPlanCreate");
            myPowerPlanBaseType = RequireType("ControlCenterSpace.BIOS.MyPowerPlanBase");
            ledCtrlType = RequireType("ControlCenterSpace.LEDCtrl");
            ledCtrlRegLedType = RequireType("ControlCenterSpace.LEDCtrl+REG_LED");
            ledGroupType = RequireType("ControlCenterSpace.MY_DATA_LED_GROUP");

            RuntimeHelpers.RunClassConstructor(gmRegistyType.TypeHandle);
            RuntimeHelpers.RunClassConstructor(myPowerPlanBaseType.TypeHandle);

            myWmiInit = RequireMethod(myWmiBaseType, "Init", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, Type.EmptyTypes);
            acpiPerformSmi = RequireMethod(myWmiBaseType, "ACPIPerformSMI", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes);
            gmGetRegistry = RequireMethod(gmRegistyType, "GetRegistry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, typeof(byte));
            gmSetRegistry = RequireMethod(gmRegistyType, "SetRegistry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, typeof(byte), typeof(uint));
            gmGetRegistryStr = RequireMethod(gmRegistyType, "GetRegistryStr", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, typeof(byte));
            gmSetRegistryStr = RequireMethod(gmRegistyType, "SetRegistryStr", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, typeof(byte), typeof(string));
            gmGetUserFan = RequireMethod(gmRegistyType, "GetUserFanFromRegistry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, typeof(byte));
            gmSetFanCurve = RequireMethod(gmRegistyType, "SetFanStrToRegistry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, typeof(byte), typeof(string));
            initSchemeGuid = RequireMethod(myPowerPlanType, "init_scheme_guid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes);
            addNewPowerScheme = RequireMethod(myPowerPlanCreateType, "AddNewPowerScheme", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                typeof(Guid), typeof(Guid), typeof(string), typeof(string));
            ledSetSingleDevice = RequireMethod(ledCtrlType, "SetSingleDevice", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, typeof(uint), typeof(uint));
            ledSetAllDevice = RequireMethod(ledCtrlType, "SetAllLEDDevice", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, typeof(uint));
            groupSaveReg = RequireMethod(ledGroupType, "save_reg", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, typeof(byte));
            groupWmiLedControl = RequireMethod(ledGroupType, "wmi_led_control", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, ledCtrlRegLedType);

            wmiDataField = RequireField(myWmiBaseType, "data");
            gamingWmiField = RequireField(myWmiBaseType, "m_GamingWmi");
            gmKeyNameField = RequireField(gmRegistyType, "KeyName");
            chooseGuidField = RequireField(myPowerPlanType, "m_choose_guid");

            mySchemeProperty = RequireProperty(myPowerPlanType, "MyScheme");
            groupStrFormatProperty = RequireProperty(ledGroupType, "str_format");
            keyboardBrightnessProperty = RequireProperty(ledGroupType, "keyboard_brightness");

            gmKeyNameField.SetValue(null, RegistryPath);
            wmi = Activator.CreateInstance(myWmiBaseType);
            powerPlan = Activator.CreateInstance(myPowerPlanType);
        }

        public string InstallDir { get; }

        public void Initialize()
        {
            if (!InvokeBool(myWmiInit, null))
            {
                throw new ControlCenterException("ControlCenter WMI interface is not available.");
            }

            if (!CheckBiosVersion())
            {
                throw new ControlCenterException("BIOS version check failed.");
            }

            EnsureDefaultLedRegistry();
            initSchemeGuid.Invoke(powerPlan, null);
            EnsurePowerPlans();
            initSchemeGuid.Invoke(powerPlan, null);
        }

        public ControlCenterStatus GetStatus()
        {
            initSchemeGuid.Invoke(powerPlan, null);

            return new ControlCenterStatus
            {
                InstallDir = InstallDir,
                WmiAvailable = Convert.ToBoolean(gamingWmiField.GetValue(null), CultureInfo.InvariantCulture),
                BiosWord = GetBiosVersionWord(),
                PowerScheme = GetActivePowerSchemeName(),
                StoredPowerMode = GetRegistry(RegPowerPlan),
                SupportsFirmwarePowerMode = SupportsFirmwarePowerMode(),
                TypeCAdapter = DetectTypeCAdapter(),
                Fan = GetFanStatus(),
                LedModule = GetRegistry(RegLedModule),
            };
        }

        public void SetPowerMode(PowerMode mode, bool force)
        {
            var onBattery = PowerStatusReader.IsOnBattery();
            var typeCAdapter = DetectTypeCAdapter();
            if (!force && (onBattery || typeCAdapter) && mode != PowerMode.Audio)
            {
                throw new ControlCenterException("原版 ControlCenter 在电池或 Type-C 供电下会强制切到办公模式。启用强制开关后才能覆盖。\n");
            }

            EnsurePowerPlans();
            initSchemeGuid.Invoke(powerPlan, null);
            var schemeGuid = GetPowerSchemeGuid(mode);

            if (mode == PowerMode.Audio)
            {
                SetRegistry(RegPowerPlan, 2);
            }
            else if (mode == PowerMode.Gaming)
            {
                SetRegistry(RegPowerPlan, 1);
            }
            else
            {
                SetRegistry(RegPowerPlan, 0);
            }

            mySchemeProperty.SetValue(powerPlan, Enum.ToObject(powerPlanSchemeType, (int)mode));

            if (SupportsFirmwarePowerMode())
            {
                PerformSmi(64256, 768, (uint)mode);
            }

            ActivatePowerScheme(schemeGuid);
            EnsureActivePowerScheme(schemeGuid);
            initSchemeGuid.Invoke(powerPlan, null);
        }

        public string GetEffectiveFanCurve(FanProfile profile)
        {
            var value = GetUserFanCurve(profile);
            if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            GetDefaultFanData(out var defaultPair, out var defaultSys);
            return (int)profile <= (int)FanProfile.UserFan3 ? defaultPair : defaultSys;
        }

        public FanStatus GetFanStatus()
        {
            var supportsThreeFan = SupportsThreeFan();
            return new FanStatus
            {
                StoredFanControlStatus = GetStoredFanControlStatus(),
                RawFanControlStatus = GetRawFanControlStatus(),
                StoredFanControlProfile = GetStoredFanControlProfile(),
                SupportsThreeFan = supportsThreeFan,
                Telemetry = ReadFanTelemetry(supportsThreeFan),
            };
        }

        public int GetRawFanControlStatus()
        {
            var data = PerformSmi(64000, 518);
            return (int)GetDword(data, "a2");
        }

        public int GetStoredFanControlStatus()
        {
            return (int)GetRegistry(RegFanControlStatus);
        }

        public int GetStoredFanControlProfile()
        {
            return (int)GetRegistry(RegFanControlSelect);
        }

        public void EnableUserFanControl(FanProfile? profile)
        {
            var primaryProfile = NormalizePrimaryProfile(profile);
            SetRegistry(RegFanControlSelect, (uint)primaryProfile);
            SetRegistry(RegFanControlStatus, 1);
            SetFanControlStatus(1);
            ApplyUserFanCurve(primaryProfile);
        }

        public void DisableUserFanControl()
        {
            SetRegistry(RegFanControlStatus, 0);
            SetRegistry(RegFanControlSelect, 0);
            SetFanControlStatus(0);
            SetFanSpeed(byte.MaxValue, byte.MaxValue, byte.MaxValue);
        }

        public void SetFanControlStatus(int status)
        {
            PerformSmi(64256, 518, (uint)status);
        }

        public void SetFanSpeed(byte cpu, byte gpu, byte? sys)
        {
            var sysValue = SupportsThreeFan() ? (sys ?? byte.MaxValue) : byte.MaxValue;
            PerformSmi(64256, 517, cpu, gpu, sysValue);
        }

        public string GetUserFanCurve(FanProfile profile)
        {
            return Convert.ToString(gmGetUserFan.Invoke(null, new object[] { (byte)profile }), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        public void SetUserFanCurve(FanProfile profile, string curve)
        {
            gmSetFanCurve.Invoke(null, new object[] { (byte)profile, curve });
        }

        public FanTelemetry ReadFanTelemetry()
        {
            return ReadFanTelemetry(SupportsThreeFan());
        }

        public void ApplyUserFanCurve(FanProfile? profile)
        {
            var primaryProfile = NormalizePrimaryProfile(profile);
            SetRegistry(RegFanControlSelect, (uint)primaryProfile);

            var command = BuildFanSpeedCommand(primaryProfile);
            SetFanSpeed(command.CpuPercent, command.GpuPercent, command.SysPercent == byte.MaxValue ? (byte?)null : command.SysPercent);
        }

        public void WatchUserFanControl(FanProfile? profile, int intervalMs, CancellationToken cancellationToken, Action<FanSpeedCommand> onApplied = null)
        {
            var primaryProfile = NormalizePrimaryProfile(profile);
            SetRegistry(RegFanControlSelect, (uint)primaryProfile);
            SetRegistry(RegFanControlStatus, 1);
            SetFanControlStatus(1);

            byte lastCpu = byte.MaxValue;
            byte lastGpu = byte.MaxValue;
            byte lastSys = byte.MaxValue;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (GetStoredFanControlStatus() == 0)
                {
                    break;
                }

                var command = BuildFanSpeedCommand(primaryProfile);
                if (command.CpuPercent != lastCpu || command.GpuPercent != lastGpu || command.SysPercent != lastSys)
                {
                    SetFanSpeed(command.CpuPercent, command.GpuPercent, command.SysPercent == byte.MaxValue ? (byte?)null : command.SysPercent);
                    onApplied?.Invoke(command);
                    lastCpu = command.CpuPercent;
                    lastGpu = command.GpuPercent;
                    lastSys = command.SysPercent;
                }

                if (cancellationToken.WaitHandle.WaitOne(intervalMs))
                {
                    break;
                }
            }
        }

        public void ApplyLedModule(LedModule module)
        {
            SetRegistry(RegLedModule, (uint)module);
            var group = Activator.CreateInstance(ledGroupType, (byte)module);
            ApplyGroupZone(group, 5);
            ApplyGroupZone(group, 4);
            ApplyGroupZone(group, 3);
            ApplyGroupZone(group, 8);
            ApplyGroupZone(group, 7);
            groupSaveReg.Invoke(group, new object[] { (byte)module });
        }

        public void SetKeyboardBrightness(LedModule module, int brightness)
        {
            SetRegistry(RegLedModule, (uint)module);
            var group = Activator.CreateInstance(ledGroupType, (byte)module);
            keyboardBrightnessProperty.SetValue(group, brightness);
            groupSaveReg.Invoke(group, new object[] { (byte)module });
            ApplyGroupZone(group, 5);
            ApplyGroupZone(group, 4);
            ApplyGroupZone(group, 3);
        }

        public void SetLedAll(LedModule module, uint ledData32)
        {
            SetRegistry(RegLedModule, (uint)module);
            SetLedString(module, FormatLedData(new[] { ledData32, ledData32, ledData32, ledData32, ledData32 }));
            ledSetAllDevice.Invoke(null, new object[] { ledData32 });
        }

        public void SetLedZone(LedModule module, LedZone zone, uint ledData32)
        {
            SetRegistry(RegLedModule, (uint)module);

            var group = Activator.CreateInstance(ledGroupType, (byte)module);
            var values = ParseLedData(Convert.ToString(groupStrFormatProperty.GetValue(group), CultureInfo.InvariantCulture) ?? string.Empty);
            values[(int)zone - 1] = ledData32;
            groupStrFormatProperty.SetValue(group, FormatLedData(values));
            groupSaveReg.Invoke(group, new object[] { (byte)module });
            ledSetSingleDevice.Invoke(null, new object[] { MapZone(zone), ledData32 });
        }

        public static uint CombineLedData(byte mode, byte alpha, uint rgb)
        {
            return (((uint)(mode * 16 + alpha)) << 24) | rgb;
        }

        public bool SupportsThreeFan()
        {
            var data = PerformSmi(64000, 513);
            var sku = GetDword(data, "a2");
            return sku == 52 || sku == 53;
        }

        public bool SupportsFirmwarePowerMode()
        {
            return GetBiosVersionWord() >= 262;
        }

        public bool DetectTypeCAdapter()
        {
            var data = PerformSmi(64000, 518);
            return GetDword(data, "a4") == 1;
        }

        private void EnsureDefaultLedRegistry()
        {
            var current = GetLedString(LedModule.Game);
            if (string.Equals(current, string.Empty, StringComparison.Ordinal))
            {
                throw new ControlCenterException("LED registry access failed.");
            }

            if (!string.Equals(current, "NULL", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SetLedString(LedModule.Game, LedGaming);
            SetLedString(LedModule.Perf, LedHighPerformance);
            SetLedString(LedModule.Audio, LedAudio);
            SetLedString(LedModule.Custom, LedCustom);
            SetRegistry(RegLedWizard, 1);
            SetRegistry(RegWinKey, 0);
            SetRegistry(RegLedModule, 0);
            SetRegistry(RegSelectAll, 0);
        }

        private void EnsurePowerPlans()
        {
            initSchemeGuid.Invoke(powerPlan, null);
            var guids = (Array)chooseGuidField.GetValue(powerPlan);
            var baseGuid = GetGuidField("POWER_SYS_BALANCE_GUID");
            if (baseGuid == Guid.Empty)
            {
                return;
            }

            if ((Guid)guids.GetValue((int)PowerMode.Audio) == Guid.Empty)
            {
                AddPowerScheme(baseGuid, GetGuidField("POWER_AUDIO_GUID"), "Audio", "Control Center Audio Mode");
            }

            if ((Guid)guids.GetValue((int)PowerMode.Gaming) == Guid.Empty)
            {
                AddPowerScheme(baseGuid, GetGuidField("POWER_GAME_GUID"), "Gaming", "Control Center Game Mode");
            }

            if ((Guid)guids.GetValue((int)PowerMode.High) == Guid.Empty)
            {
                AddPowerScheme(baseGuid, GetGuidField("POWER_NEW_HIGH_GUID"), "High performance", "Control Center High Performance Mode");
            }
        }

        private void AddPowerScheme(Guid sourceScheme, Guid targetScheme, string title, string description)
        {
            if (sourceScheme == Guid.Empty || targetScheme == Guid.Empty)
            {
                return;
            }

            var creator = Activator.CreateInstance(myPowerPlanCreateType);
            addNewPowerScheme.Invoke(creator, new object[] { sourceScheme, targetScheme, title, description });
        }

        private Guid GetPowerSchemeGuid(PowerMode mode)
        {
            var guids = (Array)chooseGuidField.GetValue(powerPlan);
            if (guids.Length > (int)mode)
            {
                var fromPlan = (Guid)guids.GetValue((int)mode);
                if (fromPlan != Guid.Empty)
                {
                    return fromPlan;
                }
            }

            return mode switch
            {
                PowerMode.Audio => GetGuidField("POWER_AUDIO_GUID"),
                PowerMode.Gaming => GetGuidField("POWER_GAME_GUID"),
                _ => GetGuidField("POWER_NEW_HIGH_GUID"),
            };
        }

        private string GetActivePowerSchemeName()
        {
            var activeSchemeGuid = TryGetActivePowerSchemeGuid();
            if (activeSchemeGuid != Guid.Empty)
            {
                if (activeSchemeGuid == GetPowerSchemeGuid(PowerMode.High))
                {
                    return "CHOOSE_CUS_H";
                }

                if (activeSchemeGuid == GetPowerSchemeGuid(PowerMode.Gaming))
                {
                    return "CHOOSE_CUS_GAMING";
                }

                if (activeSchemeGuid == GetPowerSchemeGuid(PowerMode.Audio))
                {
                    return "CHOOSE_CUS_AUDIO";
                }
            }

            return Convert.ToString(mySchemeProperty.GetValue(powerPlan), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static Guid TryGetActivePowerSchemeGuid()
        {
            var result = PowerGetActiveScheme(IntPtr.Zero, out var activePolicyGuid);
            if (result != 0 || activePolicyGuid == IntPtr.Zero)
            {
                return Guid.Empty;
            }

            try
            {
                return Marshal.PtrToStructure<Guid>(activePolicyGuid);
            }
            finally
            {
                LocalFree(activePolicyGuid);
            }
        }

        private static void EnsureActivePowerScheme(Guid expectedSchemeGuid)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (TryGetActivePowerSchemeGuid() == expectedSchemeGuid)
                {
                    return;
                }

                Thread.Sleep(100);
            }

            throw new ControlCenterException("系统电源方案没有成功切换到目标模式。");
        }

        private static void ActivatePowerScheme(Guid schemeGuid)
        {
            if (schemeGuid == Guid.Empty)
            {
                throw new ControlCenterException("目标电源方案不存在。\n");
            }

            var result = PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
            if (result != 0)
            {
                throw new ControlCenterException(string.Format(CultureInfo.InvariantCulture, "切换系统电源方案失败: 0x{0:X8}", result));
            }
        }

        private ushort GetBiosVersionWord()
        {
            var data = PerformSmi(64000, 513);
            return GetWord(data, "a0");
        }

        private bool CheckBiosVersion()
        {
            return GetBiosVersionWord() >= 260;
        }

        private object PerformSmi(ushort a0, ushort a1, uint a2 = 0, uint a3 = 0, uint a4 = 0, uint a5 = 0, uint a6 = 0)
        {
            var data = Activator.CreateInstance(smiStructType);
            SetStructField(data, "a0", a0);
            SetStructField(data, "a1", a1);
            SetStructField(data, "a2", a2);
            SetStructField(data, "a3", a3);
            SetStructField(data, "a4", a4);
            SetStructField(data, "a5", a5);
            SetStructField(data, "a6", a6);

            wmiDataField.SetValue(wmi, data);
            if (!InvokeBool(acpiPerformSmi, wmi))
            {
                throw new ControlCenterException($"SMI call failed for a0={a0} a1={a1}.");
            }

            return wmiDataField.GetValue(wmi);
        }

        private void ApplyGroupZone(object group, int regLedValue)
        {
            groupWmiLedControl.Invoke(group, new object[] { Enum.ToObject(ledCtrlRegLedType, regLedValue) });
        }

        private FanCurveData LoadFanCurveData(FanProfile profile, bool supportsThreeFan)
        {
            var data = new FanCurveData();
            GetDefaultFanData(out var defaultPair, out var defaultSys);

            var pairCurve = GetUserFanCurve(profile);
            if (string.IsNullOrWhiteSpace(pairCurve) || string.Equals(pairCurve, "NULL", StringComparison.OrdinalIgnoreCase))
            {
                pairCurve = defaultPair;
            }

            ParsePairCurve(pairCurve, data.Cpu, data.Gpu);

            if (supportsThreeFan)
            {
                var sysCurve = GetUserFanCurve(profile switch
                {
                    FanProfile.UserFan1 => FanProfile.UserFan1Sys,
                    FanProfile.UserFan2 => FanProfile.UserFan2Sys,
                    FanProfile.UserFan3 => FanProfile.UserFan3Sys,
                    _ => FanProfile.UserFan1Sys,
                });

                if (string.IsNullOrWhiteSpace(sysCurve) || string.Equals(sysCurve, "NULL", StringComparison.OrdinalIgnoreCase))
                {
                    sysCurve = defaultSys;
                }

                ParseSingleCurve(sysCurve, data.Sys);
            }

            return data;
        }

        private FanSpeedCommand BuildFanSpeedCommand(FanProfile profile)
        {
            var supportsThreeFan = SupportsThreeFan();
            var curves = LoadFanCurveData(profile, supportsThreeFan);
            var telemetry = ReadFanTelemetry(supportsThreeFan);

            return new FanSpeedCommand
            {
                CpuPercent = (byte)curves.Cpu[GetTemperatureBucket(telemetry.CpuTemp)],
                GpuPercent = (byte)curves.Gpu[GetTemperatureBucket(telemetry.GpuTemp)],
                SysPercent = supportsThreeFan ? (byte)curves.Sys[GetTemperatureBucket(telemetry.SysTemp)] : byte.MaxValue,
            };
        }

        private FanTelemetry ReadFanTelemetry(bool supportsThreeFan)
        {
            var info = PerformSmi(64000, 512);
            var telemetry = new FanTelemetry
            {
                CpuTemp = (byte)GetDword(info, "a2"),
                GpuTemp = (byte)GetDword(info, "a3"),
                CpuRpm = (ushort)GetDword(info, "a4"),
                GpuRpm = (ushort)GetDword(info, "a5"),
            };

            if (supportsThreeFan)
            {
                var sysInfo = PerformSmi(64000, 519);
                telemetry.SysTemp = (byte)GetDword(sysInfo, "a2");
                telemetry.SysRpm = (ushort)GetDword(sysInfo, "a3");
            }

            return telemetry;
        }

        private void GetDefaultFanData(out string defaultPair, out string defaultSys)
        {
            defaultPair = NlxbDefaultFan;
            defaultSys = NlyDefaultFanSys;

            var data = PerformSmi(64000, 517);
            switch (GetDword(data, "a2"))
            {
                case 1:
                    defaultPair = NlxbDefaultFan;
                    break;
                case 2:
                    defaultPair = NlzdDefaultFan;
                    break;
                case 3:
                    defaultPair = NlzeDefaultFan;
                    break;
                case 4:
                    defaultPair = NlyDefaultFan;
                    defaultSys = NlyDefaultFanSys;
                    break;
            }
        }

        private static void ParsePairCurve(string value, int[] cpu, int[] gpu)
        {
            var sections = value.Split(new[] { ';' }, StringSplitOptions.None);
            if (sections.Length < 2)
            {
                throw new ControlCenterException($"Invalid fan curve: {value}");
            }

            ParseCurveSection(sections[0], cpu);
            ParseCurveSection(sections[1], gpu);
        }

        private static void ParseSingleCurve(string value, int[] fan)
        {
            var section = value.Split(new[] { ';' }, StringSplitOptions.None)[0];
            ParseCurveSection(section, fan);
        }

        private static void ParseCurveSection(string value, int[] target)
        {
            var parts = value.Split(new[] { ',' }, StringSplitOptions.None);
            if (parts.Length < target.Length)
            {
                throw new ControlCenterException($"Invalid fan curve section: {value}");
            }

            for (var i = 0; i < target.Length; i++)
            {
                target[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
            }
        }

        private static int GetTemperatureBucket(int temperature)
        {
            if (temperature < DefaultTemperatures[0])
            {
                temperature = DefaultTemperatures[0];
            }

            for (var i = 0; i < DefaultTemperatures.Length; i++)
            {
                if (temperature >= DefaultTemperatures[i] && temperature - DefaultTemperatures[i] < 10)
                {
                    return i;
                }
            }

            return DefaultTemperatures.Length - 1;
        }

        private static FanProfile NormalizePrimaryProfile(FanProfile? profile)
        {
            var selected = profile ?? FanProfile.UserFan1;
            if ((int)selected > (int)FanProfile.UserFan3)
            {
                throw new ControlCenterException("Primary fan profile must be 1, 2, or 3.");
            }

            return selected;
        }

        private uint GetRegistry(byte index)
        {
            return Convert.ToUInt32(gmGetRegistry.Invoke(null, new object[] { index }), CultureInfo.InvariantCulture);
        }

        private void SetRegistry(byte index, uint value)
        {
            gmSetRegistry.Invoke(null, new object[] { index, value });
        }

        private string GetLedString(LedModule module)
        {
            return Convert.ToString(gmGetRegistryStr.Invoke(null, new object[] { (byte)module }), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private void SetLedString(LedModule module, string value)
        {
            gmSetRegistryStr.Invoke(null, new object[] { (byte)module, value });
        }

        private Guid GetGuidField(string name)
        {
            return (Guid)RequireField(myPowerPlanBaseType, name).GetValue(null);
        }

        private Type RequireType(string fullName)
        {
            var type = assembly.GetType(fullName, throwOnError: false);
            if (type == null)
            {
                throw new ControlCenterException($"Type not found: {fullName}");
            }

            return type;
        }

        private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags, params Type[] parameterTypes)
        {
            var method = type.GetMethod(name, flags, null, parameterTypes, null);
            if (method == null)
            {
                throw new ControlCenterException($"Method not found: {type.FullName}.{name}");
            }

            return method;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (field == null)
            {
                throw new ControlCenterException($"Field not found: {type.FullName}.{name}");
            }

            return field;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (property == null)
            {
                throw new ControlCenterException($"Property not found: {type.FullName}.{name}");
            }

            return property;
        }

        private static void SetStructField(object target, string name, object value)
        {
            var field = RequireField(target.GetType(), name);
            field.SetValue(target, value);
        }

        private static ushort GetWord(object target, string name)
        {
            return Convert.ToUInt16(RequireField(target.GetType(), name).GetValue(target), CultureInfo.InvariantCulture);
        }

        private static uint GetDword(object target, string name)
        {
            return Convert.ToUInt32(RequireField(target.GetType(), name).GetValue(target), CultureInfo.InvariantCulture);
        }

        private static bool InvokeBool(MethodInfo method, object target)
        {
            return Convert.ToBoolean(method.Invoke(target, null), CultureInfo.InvariantCulture);
        }

        private static uint[] ParseLedData(string value)
        {
            var result = new uint[5];
            var parts = value.Split(new[] { ';' }, StringSplitOptions.None);
            for (var i = 0; i < result.Length && i < parts.Length; i++)
            {
                if (uint.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                {
                    result[i] = parsed;
                }
            }

            return result;
        }

        private static string FormatLedData(IEnumerable<uint> values)
        {
            return string.Join(";", values.Select(value => value.ToString("x", CultureInfo.InvariantCulture)));
        }

        private static uint MapZone(LedZone zone)
        {
            return zone switch
            {
                LedZone.Logo => 8,
                LedZone.Trunk => 7,
                LedZone.Led1 => 5,
                LedZone.Led2 => 4,
                LedZone.Led3 => 3,
                _ => throw new ControlCenterException($"Unsupported LED zone: {zone}"),
            };
        }

        private sealed class FanCurveData
        {
            public int[] Cpu { get; } = new int[8];

            public int[] Gpu { get; } = new int[8];

            public int[] Sys { get; } = new int[8];
        }
    }

    internal static class PowerStatusReader
    {
        public static bool IsOnBattery()
        {
            if (!GetSystemPowerStatus(out var status))
            {
                return false;
            }

            return status.ACLineStatus == 0;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }
    }
}
