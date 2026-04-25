using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using BetterNL5.Core;

namespace BetterNL5.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (ControlCenterException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                Console.Error.WriteLine(ex.InnerException.ToString());
                return 3;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("help", StringComparer.OrdinalIgnoreCase))
            {
                PrintUsage();
                return 0;
            }

            var installDir = ControlCenterService.DefaultInstallDir;
            var remaining = new List<string>(args);
            for (var i = 0; i < remaining.Count; i++)
            {
                if (!string.Equals(remaining[i], "--install-dir", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= remaining.Count)
                {
                    throw new ControlCenterException("Missing value after --install-dir.");
                }

                installDir = remaining[i + 1];
                remaining.RemoveAt(i + 1);
                remaining.RemoveAt(i);
                break;
            }

            var service = new ControlCenterService(installDir);
            service.Initialize();

            switch (remaining[0].ToLowerInvariant())
            {
                case "status":
                    PrintStatus(service.GetStatus());
                    return 0;

                case "power":
                    return RunPower(service, remaining.Skip(1).ToArray());

                case "fan":
                    return RunFan(service, remaining.Skip(1).ToArray());

                case "led":
                    return RunLed(service, remaining.Skip(1).ToArray());

                default:
                    throw new ControlCenterException($"Unknown command: {remaining[0]}");
            }
        }

        private static int RunPower(ControlCenterService service, string[] args)
        {
            if (args.Length < 2 || !string.Equals(args[0], "set", StringComparison.OrdinalIgnoreCase))
            {
                throw new ControlCenterException("Usage: power set <high|gaming|audio> [--force]");
            }

            var force = args.Any(arg => string.Equals(arg, "--force", StringComparison.OrdinalIgnoreCase));
            var mode = ParsePowerMode(args[1]);
            service.SetPowerMode(mode, force);
            Console.WriteLine($"power={args[1].ToLowerInvariant()}");
            return 0;
        }

        private static int RunFan(ControlCenterService service, string[] args)
        {
            if (args.Length == 0)
            {
                throw new ControlCenterException("Missing fan subcommand.");
            }

            switch (args[0].ToLowerInvariant())
            {
                case "status":
                    PrintFanStatus(service.GetFanStatus());
                    return 0;

                case "enable":
                {
                    FanProfile? profile = null;
                    if (args.Length >= 2)
                    {
                        profile = ParseFanProfile(args[1]);
                    }

                    service.EnableUserFanControl(profile);
                    Console.WriteLine("fanControlStatus=1");
                    return 0;
                }

                case "apply":
                {
                    FanProfile? profile = null;
                    if (args.Length >= 2)
                    {
                        profile = ParseFanProfile(args[1]);
                    }

                    service.ApplyUserFanCurve(profile);
                    Console.WriteLine("fanApplied=1");
                    return 0;
                }

                case "watch":
                {
                    FanProfile? profile = null;
                    var intervalMs = 5000;
                    if (args.Length >= 2)
                    {
                        profile = ParseFanProfile(args[1]);
                    }

                    if (args.Length >= 3)
                    {
                        intervalMs = ParseInt(args[2], "intervalMs", 200, 60000);
                    }

                    using (var cancellationSource = new CancellationTokenSource())
                    {
                        Console.CancelKeyPress += (_, e) =>
                        {
                            e.Cancel = true;
                            cancellationSource.Cancel();
                        };

                        service.WatchUserFanControl(profile, intervalMs, cancellationSource.Token, command =>
                        {
                            Console.WriteLine($"fanApplied=cpu:{command.CpuPercent},gpu:{command.GpuPercent},sys:{command.SysPercent}");
                        });
                    }

                    return 0;
                }

                case "disable":
                    service.DisableUserFanControl();
                    Console.WriteLine("fanControlStatus=0");
                    return 0;

                case "speed":
                {
                    if (args.Length < 3)
                    {
                        throw new ControlCenterException("Usage: fan speed <cpuPercent> <gpuPercent> [sysPercent]");
                    }

                    var cpu = ParseByte(args[1], "cpuPercent");
                    var gpu = ParseByte(args[2], "gpuPercent");
                    byte? sys = args.Length >= 4 ? ParseByte(args[3], "sysPercent") : null;
                    service.SetFanSpeed(cpu, gpu, sys);
                    Console.WriteLine($"fanSpeed=cpu:{cpu},gpu:{gpu},sys:{(sys.HasValue ? sys.Value.ToString(CultureInfo.InvariantCulture) : "255")}");
                    return 0;
                }

                case "curve":
                {
                    if (args.Length < 3)
                    {
                        throw new ControlCenterException("Usage: fan curve <get|set> <profile> [value]");
                    }

                    var profile = ParseFanProfile(args[2]);
                    if (string.Equals(args[1], "get", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine(service.GetUserFanCurve(profile));
                        return 0;
                    }

                    if (string.Equals(args[1], "set", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Length < 4)
                        {
                            throw new ControlCenterException("Usage: fan curve set <profile> <value>");
                        }

                        service.SetUserFanCurve(profile, args[3]);
                        Console.WriteLine("fanCurveSaved=1");
                        return 0;
                    }

                    throw new ControlCenterException($"Unknown fan curve action: {args[1]}");
                }

                default:
                    throw new ControlCenterException($"Unknown fan subcommand: {args[0]}");
            }
        }

        private static int RunLed(ControlCenterService service, string[] args)
        {
            if (args.Length == 0)
            {
                throw new ControlCenterException("Missing led subcommand.");
            }

            switch (args[0].ToLowerInvariant())
            {
                case "module":
                {
                    if (args.Length < 2)
                    {
                        throw new ControlCenterException("Usage: led module <game|perf|audio|custom>");
                    }

                    var module = ParseLedModule(args[1]);
                    service.ApplyLedModule(module);
                    Console.WriteLine($"ledModule={args[1].ToLowerInvariant()}");
                    return 0;
                }

                case "brightness":
                {
                    if (args.Length < 3)
                    {
                        throw new ControlCenterException("Usage: led brightness <module> <0-15>");
                    }

                    var module = ParseLedModule(args[1]);
                    var brightness = ParseInt(args[2], "brightness", 0, 15);
                    service.SetKeyboardBrightness(module, brightness);
                    Console.WriteLine($"ledBrightness={brightness}");
                    return 0;
                }

                case "set-all":
                {
                    if (args.Length < 5)
                    {
                        throw new ControlCenterException("Usage: led set-all <module> <mode> <brightness> <rrggbb>");
                    }

                    var module = ParseLedModule(args[1]);
                    var mode = ParseByte(args[2], "mode");
                    var alpha = ParseByte(args[3], "brightness");
                    var rgb = ParseRgb(args[4]);
                    service.SetLedAll(module, ControlCenterService.CombineLedData(mode, alpha, rgb));
                    Console.WriteLine("ledUpdated=1");
                    return 0;
                }

                case "set-zone":
                {
                    if (args.Length < 6)
                    {
                        throw new ControlCenterException("Usage: led set-zone <module> <logo|trunk|led1|led2|led3> <mode> <brightness> <rrggbb>");
                    }

                    var module = ParseLedModule(args[1]);
                    var zone = ParseLedZone(args[2]);
                    var mode = ParseByte(args[3], "mode");
                    var alpha = ParseByte(args[4], "brightness");
                    var rgb = ParseRgb(args[5]);
                    service.SetLedZone(module, zone, ControlCenterService.CombineLedData(mode, alpha, rgb));
                    Console.WriteLine("ledUpdated=1");
                    return 0;
                }

                default:
                    throw new ControlCenterException($"Unknown led subcommand: {args[0]}");
            }
        }

        private static void PrintStatus(ControlCenterStatus status)
        {
            Console.WriteLine($"installDir={status.InstallDir}");
            Console.WriteLine($"wmi={status.WmiAvailable}");
            Console.WriteLine($"biosWord={status.BiosWord}");
            Console.WriteLine($"powerScheme={status.PowerScheme}");
            Console.WriteLine($"storedPowerMode={status.StoredPowerMode}");
            Console.WriteLine($"supportsFirmwarePowerMode={status.SupportsFirmwarePowerMode}");
            Console.WriteLine($"typeCAdapter={status.TypeCAdapter}");
            Console.WriteLine($"storedFanControlStatus={status.Fan.StoredFanControlStatus}");
            Console.WriteLine($"rawFanControlStatus={status.Fan.RawFanControlStatus}");
            Console.WriteLine($"fanControlProfile={status.Fan.StoredFanControlProfile + 1}");
            Console.WriteLine($"supportsThreeFan={status.Fan.SupportsThreeFan}");
            Console.WriteLine($"cpuTemp={status.Fan.Telemetry.CpuTemp}");
            Console.WriteLine($"gpuTemp={status.Fan.Telemetry.GpuTemp}");
            Console.WriteLine($"sysTemp={status.Fan.Telemetry.SysTemp}");
            Console.WriteLine($"cpuRpm={status.Fan.Telemetry.CpuRpm}");
            Console.WriteLine($"gpuRpm={status.Fan.Telemetry.GpuRpm}");
            Console.WriteLine($"sysRpm={status.Fan.Telemetry.SysRpm}");
            Console.WriteLine($"ledModule={status.LedModule}");
        }

        private static void PrintFanStatus(FanStatus status)
        {
            Console.WriteLine($"storedFanControlStatus={status.StoredFanControlStatus}");
            Console.WriteLine($"rawFanControlStatus={status.RawFanControlStatus}");
            Console.WriteLine($"fanControlProfile={status.StoredFanControlProfile + 1}");
            Console.WriteLine($"cpuTemp={status.Telemetry.CpuTemp}");
            Console.WriteLine($"gpuTemp={status.Telemetry.GpuTemp}");
            Console.WriteLine($"sysTemp={status.Telemetry.SysTemp}");
            Console.WriteLine($"cpuRpm={status.Telemetry.CpuRpm}");
            Console.WriteLine($"gpuRpm={status.Telemetry.GpuRpm}");
            Console.WriteLine($"sysRpm={status.Telemetry.SysRpm}");
        }

        private static PowerMode ParsePowerMode(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "high" => PowerMode.High,
                "gaming" => PowerMode.Gaming,
                "audio" => PowerMode.Audio,
                _ => throw new ControlCenterException($"Unknown power mode: {value}"),
            };
        }

        private static LedModule ParseLedModule(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "game" => LedModule.Game,
                "perf" => LedModule.Perf,
                "audio" => LedModule.Audio,
                "custom" => LedModule.Custom,
                _ => throw new ControlCenterException($"Unknown LED module: {value}"),
            };
        }

        private static LedZone ParseLedZone(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "logo" => LedZone.Logo,
                "trunk" => LedZone.Trunk,
                "led1" => LedZone.Led1,
                "led2" => LedZone.Led2,
                "led3" => LedZone.Led3,
                _ => throw new ControlCenterException($"Unknown LED zone: {value}"),
            };
        }

        private static FanProfile ParseFanProfile(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "1" or "userfan1" => FanProfile.UserFan1,
                "2" or "userfan2" => FanProfile.UserFan2,
                "3" or "userfan3" => FanProfile.UserFan3,
                "1-sys" or "userfan1_sys" => FanProfile.UserFan1Sys,
                "2-sys" or "userfan2_sys" => FanProfile.UserFan2Sys,
                "3-sys" or "userfan3_sys" => FanProfile.UserFan3Sys,
                _ => throw new ControlCenterException($"Unknown fan profile: {value}"),
            };
        }

        private static byte ParseByte(string value, string name)
        {
            if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            {
                throw new ControlCenterException($"Invalid {name}: {value}");
            }

            return result;
        }

        private static int ParseInt(string value, string name, int min, int max)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result < min || result > max)
            {
                throw new ControlCenterException($"Invalid {name}: {value}. Expected {min}-{max}.");
            }

            return result;
        }

        private static uint ParseRgb(string value)
        {
            var text = value.StartsWith("#", StringComparison.Ordinal) ? value.Substring(1) : value;
            if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                throw new ControlCenterException($"Invalid RGB value: {value}. Expected RRGGBB.");
            }

            return rgb;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("BetterNL5 CLI");
            Console.WriteLine("Usage:");
            Console.WriteLine("  betternl5 status");
            Console.WriteLine("  betternl5 power set <high|gaming|audio> [--force]");
            Console.WriteLine("  betternl5 fan status");
            Console.WriteLine("  betternl5 fan enable [profile]");
            Console.WriteLine("  betternl5 fan apply [profile]");
            Console.WriteLine("  betternl5 fan watch [profile] [intervalMs]");
            Console.WriteLine("  betternl5 fan disable");
            Console.WriteLine("  betternl5 fan speed <cpuPercent> <gpuPercent> [sysPercent]");
            Console.WriteLine("  betternl5 fan curve get <1|2|3|1-sys|2-sys|3-sys>");
            Console.WriteLine("  betternl5 fan curve set <1|2|3|1-sys|2-sys|3-sys> <value>");
            Console.WriteLine("  betternl5 led module <game|perf|audio|custom>");
            Console.WriteLine("  betternl5 led brightness <module> <0-15>");
            Console.WriteLine("  betternl5 led set-all <module> <mode> <brightness> <rrggbb>");
            Console.WriteLine("  betternl5 led set-zone <module> <logo|trunk|led1|led2|led3> <mode> <brightness> <rrggbb>");
            Console.WriteLine("Options:");
            Console.WriteLine("  --install-dir <path>");
        }
    }
}
