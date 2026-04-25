using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BetterNL5.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using WinRT.Interop;

namespace BetterNL5
{
    public sealed partial class MainWindow : Window
    {
        private readonly SemaphoreSlim serviceGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1, 1);
        private readonly DispatcherQueueTimer refreshTimer;
        private ControlCenterService? service;
        private bool initialized;
        private bool busy;
        private bool windowPositioned;
        private bool applyingStatus;
        private PowerMode? currentPowerMode;

        public MainWindow()
        {
            InitializeComponent();
            Activated += MainWindow_Activated;
            refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            refreshTimer.Interval = TimeSpan.FromMilliseconds(500);
            refreshTimer.Tick += RefreshTimer_Tick;
            UpdateFanSliderText();
            UpdateLedBrightnessText();
            SidebarNavigationView.SelectedItem = PowerNavItem;
            SelectSection("power");
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (windowPositioned)
            {
                return;
            }

            windowPositioned = true;
            PositionWindow(1380, 920);
        }

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            await InitializeServiceAsync();
        }

        private async Task InitializeServiceAsync()
        {
            await RunBusyAsync(async () =>
            {
                await serviceGate.WaitAsync();
                try
                {
                    service = await Task.Run(() =>
                    {
                        var createdService = new ControlCenterService(ControlCenterService.DefaultInstallDir);
                        createdService.Initialize();
                        return createdService;
                    });
                }
                finally
                {
                    serviceGate.Release();
                }
            }, "正在读取设备状态...");

            refreshTimer.Start();
            await RefreshStatusAsync(syncEditorSelections: true);
        }

        private async Task<T> RunServiceAsync<T>(Func<ControlCenterService, T> action)
        {
            await serviceGate.WaitAsync();
            try
            {
                if (service == null)
                {
                    throw new ControlCenterException("ControlCenter service is not initialized.");
                }

                return await Task.Run(() => action(service));
            }
            finally
            {
                serviceGate.Release();
            }
        }

        private async Task RunActionAsync(Func<ControlCenterService, string> action)
        {
            try
            {
                string message = string.Empty;
                await RunBusyAsync(async () =>
                {
                    message = await RunServiceAsync(action);
                }, "正在应用更改...");

                await RefreshStatusAsync(message);
            }
            catch (Exception ex)
            {
                ShowMessage(GetErrorMessage(ex), InfoBarSeverity.Error);
            }
        }

        private async Task RefreshStatusAsync(string? successMessage = null, bool syncEditorSelections = false, bool background = false)
        {
            if (background)
            {
                if (!await refreshGate.WaitAsync(0))
                {
                    return;
                }
            }
            else
            {
                await refreshGate.WaitAsync();
            }

            try
            {
                var status = await RunServiceAsync(current => current.GetStatus());

                ApplyStatus(status, syncEditorSelections);

                if (!string.IsNullOrWhiteSpace(successMessage))
                {
                    ShowMessage(successMessage, InfoBarSeverity.Success);
                }
                else if (!background)
                {
                    StatusInfoBar.IsOpen = false;
                }
            }
            catch (Exception ex)
            {
                if (!background)
                {
                    ShowMessage(GetErrorMessage(ex), InfoBarSeverity.Error);
                }
            }
            finally
            {
                refreshGate.Release();
            }
        }

        private async Task RunBusyAsync(Func<Task> action, string statusText)
        {
            SetBusy(true, statusText);
            try
            {
                await action();
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void ApplyStatus(ControlCenterStatus status, bool syncEditorSelections)
        {
            applyingStatus = true;
            try
            {
                var friendlyScheme = FormatSchemeName(status.PowerScheme);
                var friendlyStoredMode = FormatStoredPowerMode(status.StoredPowerMode);
                var systemTempText = status.Fan.SupportsThreeFan
                    ? status.Fan.Telemetry.SysTemp.ToString(CultureInfo.InvariantCulture) + " °C"
                    : "不适用";
                var systemRpmText = status.Fan.SupportsThreeFan
                    ? status.Fan.Telemetry.SysRpm.ToString(CultureInfo.InvariantCulture)
                    : "不适用";

                SubtitleText.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} | CPU {1} °C | GPU {2} °C | 系统 {3}",
                    friendlyScheme,
                    status.Fan.Telemetry.CpuTemp,
                    status.Fan.Telemetry.GpuTemp,
                    systemTempText);

                currentPowerMode = GetCurrentPowerMode(status);
                UpdatePowerModeButtons();

                InstallDirText.Text = status.InstallDir;
                PowerSchemeText.Text = friendlyScheme;
                StoredPowerModeText.Text = friendlyStoredMode;
                FirmwareSupportText.Text = status.SupportsFirmwarePowerMode ? "支持" : "旧版 BIOS 路径";
                TypeCAdapterText.Text = status.TypeCAdapter ? "Type-C 供电路径" : "标准适配器";
                RuntimeWmiText.Text = status.WmiAvailable ? "在线" : "离线";
                BiosWordText.Text = status.BiosWord.ToString(CultureInfo.InvariantCulture);

                CpuTempText.Text = status.Fan.Telemetry.CpuTemp.ToString(CultureInfo.InvariantCulture) + " °C";
                GpuTempText.Text = status.Fan.Telemetry.GpuTemp.ToString(CultureInfo.InvariantCulture) + " °C";
                SysTempText.Text = systemTempText;
                CpuRpmText.Text = status.Fan.Telemetry.CpuRpm.ToString(CultureInfo.InvariantCulture);
                GpuRpmText.Text = status.Fan.Telemetry.GpuRpm.ToString(CultureInfo.InvariantCulture);
                SysRpmText.Text = systemRpmText;
                StoredFanStatusText.Text = FormatFanStatus(status.Fan.StoredFanControlStatus);
                RawFanStatusText.Text = FormatFanStatus(status.Fan.RawFanControlStatus);
                ThreeFanSupportText.Text = status.Fan.SupportsThreeFan ? "已检测到三风扇" : "双风扇布局";

                CpuTempBar.Value = status.Fan.Telemetry.CpuTemp;
                GpuTempBar.Value = status.Fan.Telemetry.GpuTemp;
                SysTempBar.Value = status.Fan.SupportsThreeFan ? status.Fan.Telemetry.SysTemp : 0;

                if (syncEditorSelections)
                {
                    SelectFanProfile(status.Fan.StoredFanControlProfile);
                    SelectLedModule((int)status.LedModule);
                }

                SysFanSliderRow.Visibility = status.Fan.SupportsThreeFan ? Visibility.Visible : Visibility.Collapsed;
                SysCurveRow.Visibility = status.Fan.SupportsThreeFan ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                applyingStatus = false;
            }
        }

        private void SetBusy(bool isBusy, string? statusText)
        {
            busy = isBusy;
            BusyRing.IsActive = isBusy;
            SidebarNavigationView.IsEnabled = !isBusy;
            PowerPanel.IsHitTestVisible = !isBusy;
            FansPanel.IsHitTestVisible = !isBusy;
            LightingPanel.IsHitTestVisible = !isBusy;
            UpdatePowerModeButtons();

            if (!string.IsNullOrWhiteSpace(statusText))
            {
                SubtitleText.Text = statusText;
            }
        }

        private void ShowMessage(string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Title = severity == InfoBarSeverity.Error ? "操作失败" : "完成";
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
        }

        private void UpdatePowerModeButtons()
        {
            if (AudioPowerButton == null || GamingPowerButton == null || HighPowerButton == null)
            {
                return;
            }

            AudioPowerButton.IsEnabled = !busy && currentPowerMode != PowerMode.Audio;
            GamingPowerButton.IsEnabled = !busy && currentPowerMode != PowerMode.Gaming;
            HighPowerButton.IsEnabled = !busy && currentPowerMode != PowerMode.High;
        }

        private void SelectFanProfile(int profileIndex)
        {
            var index = Math.Max(0, Math.Min(2, profileIndex));
            FanProfileComboBox.SelectedIndex = index;
        }

        private void SelectLedModule(int moduleIndex)
        {
            var index = Math.Max(0, Math.Min(3, moduleIndex));
            LedModuleComboBox.SelectedIndex = index;
        }

        private void SelectSection(string section)
        {
            PowerPanel.Visibility = section == "power" ? Visibility.Visible : Visibility.Collapsed;
            FansPanel.Visibility = section == "fans" ? Visibility.Visible : Visibility.Collapsed;
            LightingPanel.Visibility = section == "lighting" ? Visibility.Visible : Visibility.Collapsed;

            switch (section)
            {
                case "fans":
                    PageTitleText.Text = "风扇";
                    SidebarNavigationView.SelectedItem = FansNavItem;
                    break;
                case "lighting":
                    PageTitleText.Text = "灯效";
                    SidebarNavigationView.SelectedItem = LightingNavItem;
                    break;
                default:
                    PageTitleText.Text = "性能";
                    SidebarNavigationView.SelectedItem = PowerNavItem;
                    break;
            }
        }

        private FanProfile GetSelectedFanProfile()
        {
            return FanProfileComboBox.SelectedIndex switch
            {
                1 => FanProfile.UserFan2,
                2 => FanProfile.UserFan3,
                _ => FanProfile.UserFan1,
            };
        }

        private LedModule GetSelectedLedModule()
        {
            return LedModuleComboBox.SelectedIndex switch
            {
                1 => LedModule.Perf,
                2 => LedModule.Audio,
                3 => LedModule.Custom,
                _ => LedModule.Game,
            };
        }

        private LedZone GetSelectedLedZone()
        {
            return LedZoneComboBox.SelectedIndex switch
            {
                0 => LedZone.Logo,
                1 => LedZone.Trunk,
                2 => LedZone.Led1,
                3 => LedZone.Led2,
                _ => LedZone.Led3,
            };
        }

        private byte ReadByte(TextBox textBox, string name)
        {
            if (!byte.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new ControlCenterException($"Invalid {name}: {textBox.Text}");
            }

            return value;
        }

        private uint ReadRgb(TextBox textBox)
        {
            var text = textBox.Text.Trim();
            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                text = text.Substring(1);
            }

            if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                throw new ControlCenterException($"Invalid RGB value: {textBox.Text}. Expected RRGGBB.");
            }

            return rgb;
        }

        private void PositionWindow(int width, int height)
        {
            var handle = WindowNative.GetWindowHandle(this);
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32(width, height));

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
            appWindow.Move(new PointInt32(x, y));
        }

        private void UpdateFanSliderText()
        {
            if (CpuFanSlider == null || GpuFanSlider == null || SysFanSlider == null ||
                CpuFanSliderText == null || GpuFanSliderText == null || SysFanSliderText == null)
            {
                return;
            }

            CpuFanSliderText.Text = Convert.ToInt32(CpuFanSlider.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "%";
            GpuFanSliderText.Text = Convert.ToInt32(GpuFanSlider.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "%";
            SysFanSliderText.Text = Convert.ToInt32(SysFanSlider.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private void UpdateLedBrightnessText()
        {
            if (LedBrightnessSlider == null || LedBrightnessText == null)
            {
                return;
            }

            LedBrightnessText.Text = "亮度：" + Convert.ToInt32(LedBrightnessSlider.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatSchemeName(string scheme)
        {
            return scheme switch
            {
                "CHOOSE_CUS_H" => "狂暴",
                "CHOOSE_CUS_GAMING" => "游戏",
                "CHOOSE_CUS_AUDIO" => "办公",
                "CHOOSE_SYS_HIGH" => "系统高性能",
                "CHOOSE_SYS_BALANCE" => "平衡",
                "CHOOSE_SYS_L" => "静音",
                _ => scheme,
            };
        }

        private static PowerMode? GetCurrentPowerMode(ControlCenterStatus status)
        {
            return status.PowerScheme switch
            {
                "CHOOSE_CUS_H" => PowerMode.High,
                "CHOOSE_CUS_GAMING" => PowerMode.Gaming,
                "CHOOSE_CUS_AUDIO" => PowerMode.Audio,
                _ => status.StoredPowerMode switch
                {
                    0U => PowerMode.High,
                    1U => PowerMode.Gaming,
                    2U => PowerMode.Audio,
                    _ => null,
                },
            };
        }

        private static string FormatStoredPowerMode(uint mode)
        {
            return mode switch
            {
                0U => "0 / 狂暴",
                1U => "1 / 游戏",
                2U => "2 / 办公",
                _ => mode.ToString(CultureInfo.InvariantCulture),
            };
        }

        private static string FormatFanStatus(int status)
        {
            return status switch
            {
                0 => "0 / 自动",
                1 => "1 / 自定义",
                _ => status.ToString(CultureInfo.InvariantCulture),
            };
        }

        private static string FormatFanMode(FanStatus status)
        {
            if (status.StoredFanControlStatus == 0)
            {
                return "自动";
            }

            return FormatFanProfile(status.StoredFanControlProfile);
        }

        private static string FormatFanProfile(int profileIndex)
        {
            return profileIndex switch
            {
                0 => "自定义风扇 1",
                1 => "自定义风扇 2",
                2 => "自定义风扇 3",
                _ => "档位 " + (profileIndex + 1).ToString(CultureInfo.InvariantCulture),
            };
        }

        private static string FormatLedModule(LedModule module)
        {
            return module switch
            {
                LedModule.Game => "游戏",
                LedModule.Perf => "性能",
                LedModule.Audio => "办公",
                LedModule.Custom => "自定义",
                _ => module.ToString(),
            };
        }

        private static string GetErrorMessage(Exception ex)
        {
            while (true)
            {
                if (ex is TargetInvocationException targetInvocationException && targetInvocationException.InnerException != null)
                {
                    ex = targetInvocationException.InnerException;
                    continue;
                }

                if (ex is AggregateException aggregateException && aggregateException.InnerExceptions.Count == 1 && aggregateException.InnerException != null)
                {
                    ex = aggregateException.InnerException;
                    continue;
                }

                return ex.Message;
            }
        }

        private async void RefreshTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (!busy)
            {
                await RefreshStatusAsync(background: true);
            }
        }

        private void SidebarNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (busy || args.SelectedItemContainer == null)
            {
                return;
            }

            var section = Convert.ToString(args.SelectedItemContainer.Tag, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(section))
            {
                SelectSection(section);
            }
        }

        private async void HighPowerButton_Click(object sender, RoutedEventArgs e)
        {
            var force = ForcePowerToggle.IsOn;
            await RunActionAsync(current =>
            {
                current.SetPowerMode(PowerMode.High, force);
                return "已切换到狂暴模式。";
            });
        }

        private async void GamingPowerButton_Click(object sender, RoutedEventArgs e)
        {
            var force = ForcePowerToggle.IsOn;
            await RunActionAsync(current =>
            {
                current.SetPowerMode(PowerMode.Gaming, force);
                return "已切换到游戏模式。";
            });
        }

        private async void AudioPowerButton_Click(object sender, RoutedEventArgs e)
        {
            var force = ForcePowerToggle.IsOn;
            await RunActionAsync(current =>
            {
                current.SetPowerMode(PowerMode.Audio, force);
                return "已切换到办公模式。";
            });
        }

        private async void EnableFanButton_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetSelectedFanProfile();
            await RunActionAsync(current =>
            {
                current.EnableUserFanControl(profile);
                return string.Format(CultureInfo.InvariantCulture, "已启用自定义风扇档位 {0}。", (int)profile + 1);
            });
        }

        private async void ApplyFanCurveButton_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetSelectedFanProfile();
            await RunActionAsync(current =>
            {
                current.ApplyUserFanCurve(profile);
                return string.Format(CultureInfo.InvariantCulture, "已按曲线应用自定义风扇档位 {0}。", (int)profile + 1);
            });
        }

        private async void DisableFanButton_Click(object sender, RoutedEventArgs e)
        {
            await RunActionAsync(current =>
            {
                current.DisableUserFanControl();
                return "已关闭自定义风扇控制。";
            });
        }

        private async void ApplyManualFanButton_Click(object sender, RoutedEventArgs e)
        {
            var cpu = (byte)Convert.ToInt32(CpuFanSlider.Value, CultureInfo.InvariantCulture);
            var gpu = (byte)Convert.ToInt32(GpuFanSlider.Value, CultureInfo.InvariantCulture);
            var sys = SysFanSliderRow.Visibility == Visibility.Visible
                ? (byte?)Convert.ToInt32(SysFanSlider.Value, CultureInfo.InvariantCulture)
                : null;

            await RunActionAsync(current =>
            {
                current.SetFanSpeed(cpu, gpu, sys);
                return string.Format(CultureInfo.InvariantCulture, "已应用手动风扇速度：CPU {0}% / GPU {1}% / 系统 {2}。", cpu, gpu, sys.HasValue ? sys.Value.ToString(CultureInfo.InvariantCulture) + "%" : "不适用");
            });
        }

        private async void ApplyLedModuleButton_Click(object sender, RoutedEventArgs e)
        {
            var module = GetSelectedLedModule();
            await RunActionAsync(current =>
            {
                current.ApplyLedModule(module);
                return string.Format(CultureInfo.InvariantCulture, "已应用 {0} 灯效模块。", FormatLedModule(module));
            });
        }

        private async void ApplyLedBrightnessButton_Click(object sender, RoutedEventArgs e)
        {
            var module = GetSelectedLedModule();
            var brightness = Convert.ToInt32(LedBrightnessSlider.Value, CultureInfo.InvariantCulture);
            await RunActionAsync(current =>
            {
                current.SetKeyboardBrightness(module, brightness);
                return string.Format(CultureInfo.InvariantCulture, "已保存键盘亮度：{0}。", brightness);
            });
        }

        private async void SetLedAllButton_Click(object sender, RoutedEventArgs e)
        {
            var module = GetSelectedLedModule();
            var mode = ReadByte(LedModeTextBox, "mode");
            var alpha = ReadByte(LedAlphaTextBox, "brightness nibble");
            var rgb = ReadRgb(LedRgbTextBox);
            var data = ControlCenterService.CombineLedData(mode, alpha, rgb);

            await RunActionAsync(current =>
            {
                current.SetLedAll(module, data);
                return "已写入全部灯效区域。";
            });
        }

        private async void SetLedZoneButton_Click(object sender, RoutedEventArgs e)
        {
            var module = GetSelectedLedModule();
            var zone = GetSelectedLedZone();
            var mode = ReadByte(LedModeTextBox, "mode");
            var alpha = ReadByte(LedAlphaTextBox, "brightness nibble");
            var rgb = ReadRgb(LedRgbTextBox);
            var data = ControlCenterService.CombineLedData(mode, alpha, rgb);

            await RunActionAsync(current =>
            {
                current.SetLedZone(module, zone, data);
                return string.Format(CultureInfo.InvariantCulture, "已写入区域 {0}。", zone);
            });
        }

        private async void FanProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!initialized || applyingStatus || busy || service == null)
            {
                return;
            }

            await LoadFanCurveEditorAsync();
        }

        private async void ReloadCurveButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadFanCurveEditorAsync();
        }

        private async void SaveCurveButton_Click(object sender, RoutedEventArgs e)
        {
            var primaryProfile = GetSelectedFanProfile();
            var pairCurve = BuildPairCurve(CpuCurveTextBox.Text, GpuCurveTextBox.Text);
            var sysCurve = SysCurveRow.Visibility == Visibility.Visible ? NormalizeCurveSection(SysCurveTextBox.Text, "系统风扇曲线") : null;

            await RunActionAsync(current =>
            {
                current.SetUserFanCurve(primaryProfile, pairCurve);
                if (sysCurve != null)
                {
                    current.SetUserFanCurve(primaryProfile switch
                    {
                        FanProfile.UserFan1 => FanProfile.UserFan1Sys,
                        FanProfile.UserFan2 => FanProfile.UserFan2Sys,
                        FanProfile.UserFan3 => FanProfile.UserFan3Sys,
                        _ => FanProfile.UserFan1Sys,
                    }, sysCurve);
                }

                return string.Format(CultureInfo.InvariantCulture, "已保存自定义风扇档位 {0} 的曲线。", (int)primaryProfile + 1);
            });
        }

        private async Task LoadFanCurveEditorAsync()
        {
            try
            {
                await RunBusyAsync(async () =>
                {
                    var primaryProfile = GetSelectedFanProfile();
                    var pairCurve = await RunServiceAsync(current => current.GetEffectiveFanCurve(primaryProfile));
                    var sysCurve = await RunServiceAsync(current => current.GetEffectiveFanCurve(primaryProfile switch
                    {
                        FanProfile.UserFan1 => FanProfile.UserFan1Sys,
                        FanProfile.UserFan2 => FanProfile.UserFan2Sys,
                        FanProfile.UserFan3 => FanProfile.UserFan3Sys,
                        _ => FanProfile.UserFan1Sys,
                    }));

                    var pairSections = pairCurve.Split(';');
                    CpuCurveTextBox.Text = pairSections.Length > 0 ? NormalizeCurveSection(pairSections[0], "CPU 曲线") : string.Empty;
                    GpuCurveTextBox.Text = pairSections.Length > 1 ? NormalizeCurveSection(pairSections[1], "GPU 曲线") : string.Empty;
                    SysCurveTextBox.Text = NormalizeCurveSection(sysCurve, "系统风扇曲线");
                }, "正在读取风扇曲线...");
            }
            catch (Exception ex)
            {
                ShowMessage(GetErrorMessage(ex), InfoBarSeverity.Error);
            }
        }

        private static string BuildPairCurve(string cpuText, string gpuText)
        {
            var cpu = NormalizeCurveSection(cpuText, "CPU 曲线");
            var gpu = NormalizeCurveSection(gpuText, "GPU 曲线");
            return cpu + ";" + gpu;
        }

        private static string NormalizeCurveSection(string value, string name)
        {
            var parts = value.Split(new[] { ',', '，', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 8)
            {
                throw new ControlCenterException(name + " 需要填写 8 个百分比值。\n");
            }

            var normalized = new string[8];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var valueInt) || valueInt < 0 || valueInt > 100)
                {
                    throw new ControlCenterException(name + " 中存在无效值：" + parts[i]);
                }

                normalized[i] = valueInt.ToString(CultureInfo.InvariantCulture);
            }

            return string.Join(",", normalized);
        }

        private void FanSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateFanSliderText();
        }

        private void LedBrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateLedBrightnessText();
        }
    }
}
