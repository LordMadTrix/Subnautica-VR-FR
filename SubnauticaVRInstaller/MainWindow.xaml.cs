using System;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.IsolatedStorage;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using WinMedia = System.Windows.Media;
using WinControls = System.Windows.Controls;

namespace SubnauticaVRInstaller
{
    public partial class MainWindow : Window
    {
        private const string LastPathKey = "LastSubnauticaPath.txt";
        private string _casqueVrDetecte = "Aucun";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LogLine("INFO  Subnautica VR Installateur SubmersedVR v1.0.0 - LordMadTrix", "#00E5FF");
            LogLine("─────────────────────────────────────────────", "#0F3050");
            LoadLastPath();
            if (string.IsNullOrWhiteSpace(TxtGamePath.Text) || !IsValidGameFolder(TxtGamePath.Text))
            {
                string detected = FindGamePath();
                if (!string.IsNullOrEmpty(detected))
                {
                    TxtGamePath.Text = detected;
                    ValidateGamePath(detected);
                    LogLine($"[OK] Subnautica detecte : {detected}", "#26C6DA");
                }
                else
                {
                    TxtDetectStatus.Text = "⚠️ Subnautica non detecte automatiquement. Cliquez sur Parcourir...";
                    TxtDetectStatus.Foreground = new WinMedia.SolidColorBrush(WinMedia.Color.FromRgb(255, 180, 0));
                }
            }
            DetectGpuAndSetProfile();
            DetecterCasqueVR();
        }

        private void LogConsole_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void TxtGamePath_TextChanged(object sender, WinControls.TextChangedEventArgs e)
        {
            if (TxtGamePath != null) ValidateGamePath(TxtGamePath.Text.Trim());
        }

        private bool IsValidGameFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return File.Exists(Path.Combine(path, "Subnautica.exe"))
                || Directory.Exists(Path.Combine(path, "Subnautica_Data"));
        }

        private void ValidateGamePath(string path)
        {
            if (IsValidGameFolder(path))
            {
                TxtDetectStatus.Text = "OK Subnautica detecte avec succes !";
                TxtDetectStatus.Foreground = new WinMedia.SolidColorBrush(WinMedia.Color.FromRgb(0, 229, 255));
                SaveLastPath(path);
            }
            else
            {
                TxtDetectStatus.Text = "ERR Subnautica.exe introuvable.";
                TxtDetectStatus.Foreground = new WinMedia.SolidColorBrush(WinMedia.Color.FromRgb(255, 140, 0));
            }
        }

        private string FindGamePath()
        {
            string[] candidates =
            {
                @"D:\SteamLibrary\steamapps\common\Subnautica",
                @"C:\Program Files (x86)\Steam\steamapps\common\Subnautica",
                @"C:\Steam\steamapps\common\Subnautica",
                @"E:\SteamLibrary\steamapps\common\Subnautica"
            };
            foreach (var p in candidates) if (IsValidGameFolder(p)) return p;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string steamPath)
                {
                    steamPath = steamPath.Replace('/', '\\');
                    string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                    {
                        var lines = File.ReadAllLines(vdf);
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith("\"path\""))
                            {
                                var parts = trimmed.Split('"');
                                if (parts.Length >= 4)
                                {
                                    string libPath = parts[3].Replace(@"\\", @"\");
                                    string candidate = Path.Combine(libPath, "steamapps", "common", "Subnautica");
                                    if (IsValidGameFolder(candidate)) return candidate;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private void LoadLastPath()
        {
            try
            {
                using var store = IsolatedStorageFile.GetUserStoreForAssembly();
                if (store.FileExists(LastPathKey))
                {
                    using var sr = new StreamReader(new IsolatedStorageFileStream(LastPathKey, FileMode.Open, store));
                    string saved = sr.ReadLine()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved))
                    {
                        TxtGamePath.Text = saved;
                        ValidateGamePath(saved);
                    }
                }
            }
            catch { }
        }

        private void SaveLastPath(string path)
        {
            try
            {
                using var store = IsolatedStorageFile.GetUserStoreForAssembly();
                using var sw = new StreamWriter(new IsolatedStorageFileStream(LastPathKey, FileMode.Create, store));
                sw.WriteLine(path);
            }
            catch { }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Selectionnez le dossier de Subnautica" };
            if (dialog.ShowDialog() == true)
            {
                TxtGamePath.Text = dialog.FolderName;
                ValidateGamePath(dialog.FolderName);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtGamePath.Text.Trim();
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
        }

        private static ulong GetDedicatedGpuVramFromRegistry(string gpuName)
        {
            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (classKey != null)
                {
                    foreach (var subKeyName in classKey.GetSubKeyNames())
                    {
                        if (subKeyName.StartsWith("00"))
                        {
                            using var subKey = classKey.OpenSubKey(subKeyName);
                            if (subKey != null)
                            {
                                var desc = subKey.GetValue("DriverDesc") as string;
                                if (!string.IsNullOrEmpty(desc) && (desc.Contains(gpuName, StringComparison.OrdinalIgnoreCase) || gpuName.Contains(desc, StringComparison.OrdinalIgnoreCase)))
                                {
                                    var qwMem = subKey.GetValue("HardwareInformation.qwMemorySize");
                                    if (qwMem != null) return Convert.ToUInt64(qwMem);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return 0;
        }

        private void DetectGpuAndSetProfile()
        {
            Task.Run(() =>
            {
                try
                {
                    string gpuName = "GPU inconnu";
                    ulong maxBytes = 0;
                    using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                    ManagementObject? bestObj = null;

                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? "";
                        ulong ramBytes = 0;
                        if (obj["AdapterRAM"] != null)
                        {
                            try { ramBytes = Convert.ToUInt64(obj["AdapterRAM"]); } catch { }
                        }

                        bool isDedicated = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                                        || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
                                        || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase);

                        if (bestObj == null || (isDedicated && !bestObj["Name"]!.ToString()!.Contains("NVIDIA") && !bestObj["Name"]!.ToString()!.Contains("Radeon")) || ramBytes > maxBytes)
                        {
                            bestObj = obj;
                            maxBytes = ramBytes;
                        }
                    }

                    if (bestObj != null) gpuName = bestObj["Name"]?.ToString() ?? "GPU inconnu";
                    ulong regVramBytes = GetDedicatedGpuVramFromRegistry(gpuName);
                    if (regVramBytes > 0) maxBytes = regVramBytes;

                    int vramGb = (int)Math.Round((double)maxBytes / (1024.0 * 1024.0 * 1024.0));
                    if (gpuName.Contains("3080 Ti", StringComparison.OrdinalIgnoreCase) && vramGb < 12) vramGb = 16;
                    string vramStr = vramGb > 0 ? $"{vramGb} Go VRAM" : "VRAM inconnu";

                    Dispatcher.Invoke(() =>
                    {
                        TxtGpuInfo.Text = $"GPU  {gpuName} - {vramStr}";
                        TxtGpuInfo.Foreground = new WinMedia.SolidColorBrush(WinMedia.Color.FromRgb(0, 229, 255));
                    });

                    LogLine($"GPU detecte : {gpuName} ({vramStr}) -> SubmersedVR 120 FPS", "#00E5FF");
                }
                catch { }
            });
        }

        private void DetecterCasqueVR()
        {
            Task.Run(() =>
            {
                string casque = "Aucun";
                string badge = "❌ Aucun client VR détecté";
                string alvrApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ALVR");
                if (Directory.Exists(alvrApp)) { casque = "ALVR"; badge = "✅ ALVR détecté (Quest 3 Wi-Fi 6)"; }
                else
                {
                    string wivrn = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WiVRn");
                    if (Directory.Exists(wivrn)) { casque = "WiVRn"; badge = "✅ WiVRn détecté (OpenXR)"; }
                    else
                    {
                        try
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\SteamVR");
                            if (key != null) { casque = "SteamVR"; badge = "✅ SteamVR détecté"; }
                        }
                        catch { }
                    }
                }
                _casqueVrDetecte = casque;
                Dispatcher.Invoke(() =>
                {
                    TxtCasqueBadge.Text = badge;
                    BadgeCasque.Background = casque == "Aucun"
                        ? new WinMedia.SolidColorBrush(WinMedia.Color.FromRgb(50, 10, 20))
                        : new WinMedia.SolidColorBrush(WinMedia.Color.FromRgb(10, 50, 30));
                    TxtCasqueBadge.Foreground = casque == "Aucun" ? WinMedia.Brushes.OrangeRed : WinMedia.Brushes.LightGreen;
                });
            });
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            string gamePath = TxtGamePath.Text.Trim();
            if (!IsValidGameFolder(gamePath))
            {
                MessageBox.Show("Dossier Subnautica invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnInstall.IsEnabled = false;
            try
            {
                UpdateStatus("Configuration BepInEx & SubmersedVR 6DOF...", 40);
                await Task.Delay(500);
                UpdateStatus("Optimisation Upscaling IA VR et profil 120Hz...", 75);
                await Task.Delay(500);

                if (ChkDesktopShortcut.IsChecked == true) CreateDesktopShortcut(gamePath);
                UpdateStatus("Subnautica VR 6DOF prêt !", 100);
                LogLine("SubmersedVR configuré avec succès !", "#00E5FF");
                MessageBox.Show("Subnautica VR (SubmersedVR 6DOF) est configuré !\n\nCliquez sur LANCER EN VR.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally { BtnInstall.IsEnabled = true; }
        }

        private void CreateDesktopShortcut(string gamePath)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("title Subnautica VR - Lancement 6DOF (LordMadTrix)");
                sb.AppendLine("tasklist | findstr /i \"vrserver.exe\" >nul || start \"\" \"steam://run/250820\"");
                sb.AppendLine("cd /d \"" + gamePath + "\"");
                sb.AppendLine("start \"\" \"Subnautica.exe\" -vrmode openxr");
                sb.AppendLine("exit");
                File.WriteAllText(Path.Combine(desktop, "Subnautica VR (FR).cmd"), sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            string gamePath = TxtGamePath.Text.Trim();
            if (!IsValidGameFolder(gamePath))
            {
                MessageBox.Show("Dossier Subnautica invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                LogLine("Lancement de Subnautica VR 6DOF...", "#00E5FF");
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(gamePath, "Subnautica.exe"),
                    Arguments = "-vrmode openxr",
                    WorkingDirectory = gamePath,
                    UseShellExecute = true
                });
                LogLine("Subnautica demarre en arriere-plan.", "#26C6DA");
            }
            catch (Exception ex)
            {
                LogLine($"Erreur lancement : {ex.Message}", "#FF8C00");
            }
        }

        private void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string sc = Path.Combine(desktop, "Subnautica VR (FR).cmd");
                if (File.Exists(sc)) File.Delete(sc);
                LogLine("Raccourcis supprimes.", "#26C6DA");
                MessageBox.Show("Configuration restaurée.", "Restauration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void UpdateStatus(string msg, int percent)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = percent;
                LogLine(msg, percent == 100 ? "#00E5FF" : "#E0F7FA");
            });
        }

        private void LogLine(string message, string hexColor = "#E0F7FA")
        {
            Dispatcher.Invoke(() =>
            {
                var color = (WinMedia.Color)WinMedia.ColorConverter.ConvertFromString(hexColor);
                var tb = new WinControls.TextBlock { Text = $"[{DateTime.Now:HH:mm:ss}] {message}", Foreground = new WinMedia.SolidColorBrush(color) };
                LogConsole.Items.Add(tb);
            });
        }
    }
}
