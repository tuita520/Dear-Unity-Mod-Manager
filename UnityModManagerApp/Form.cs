﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using UnityModManagerNet.Injection;
using UnityModManagerNet.Installer.Properties;
using UnityModManagerNet.Marks;
using UnityModManagerNet.UI.Utils;
using FileAttributes = System.IO.FileAttributes;

namespace UnityModManagerNet.Installer
{
    [Serializable]
    public partial class UnityModManagerForm : Form
    {
        const string REG_PATH = @"HKEY_CURRENT_USER\Software\DearUnityModManager";
        private readonly string SKINS_PATH = $@"{Application.StartupPath}\Skins";
        private AutoSizeFormControlUtil _autoSizeFormControlUtil;

        private static readonly Version VER_0_13 = new Version(0, 13);
        private static readonly Version VER_0_22 = new Version(0, 22);

        [Flags]
        enum LibIncParam { Normal = 0, Minimal_lt_0_22 = 1 }

        private static readonly Dictionary<string, LibIncParam> libraryFiles = new Dictionary<string, LibIncParam>
        {
            { "0Harmony.dll", LibIncParam.Normal },
            { "0Harmony12.dll", LibIncParam.Minimal_lt_0_22 },
            { "0Harmony-1.2.dll", LibIncParam.Minimal_lt_0_22 },
            { "dnlib.dll", LibIncParam.Normal },
            { "System.Xml.dll", LibIncParam.Normal },
            { nameof(UnityModManager) + ".dll", LibIncParam.Normal }
        };

        public static UnityModManagerForm instance;

        static List<string> libraryPaths;
        static Config config;
        static Param param;
        static Version version;

        static string gamePath;
        static string managedPath;
        static string managerPath;
        static string entryAssemblyPath;
        static string injectedEntryAssemblyPath;
        static string managerAssemblyPath;
        static string entryPoint;
        static string injectedEntryPoint;

        static string gameExePath;

        static string doorstopFilename = "version.dll";
        static string doorstopFilenameX86 = "version_x86.dll";
        static string doorstopFilenameX64 = "version_x64.dll";
        static string doorstopConfigFilename = "doorstop_config.ini";
        static string doorstopPath;
        static string doorstopConfigPath;

        static ModuleDefMD assemblyDef;
        static ModuleDefMD injectedAssemblyDef;
        static ModuleDefMD managerDef;

        GameInfo selectedGame => (GameInfo)gameList.SelectedItem;
        Param.GameParam selectedGameParams;
        ModInfo selectedMod => listMods.SelectedItems.Count > 0 ? _mods.Find(x => x.DisplayName == listMods.SelectedItems[0].Text) : null;

        public UnityModManagerForm()
        {
            InitializeComponent();
            Load += UnityModManagerForm_Load;
        }

        private void UnityModManagerForm_Load(object sender, EventArgs e)
        {
            Init();
            InitPageMods();
        }

        private void Init()
        {
            var skins = new Dictionary<string, string> { ["默认皮肤"] = "" };
            skins = Utils.GetMatchedFiles(SKINS_PATH, "*.ssk", skins);
            var skinSet = new BindingSource { DataSource = skins };
            skinSetBox.DataSource = skinSet;
            skinSetBox.DisplayMember = "Key";
            skinSetBox.ValueMember = "Value";
            _autoSizeFormControlUtil = new AutoSizeFormControlUtil(this);
            _autoSizeFormControlUtil.RefreshControlsInfo(Controls[0]);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            instance = this;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            if (!Utils.IsUnixPlatform())
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var registry = asm.GetType("Microsoft.Win32.Registry");
                    if (registry == null) continue;
                    var getValue = registry.GetMethod("GetValue", new[] { typeof(string), typeof(string), typeof(object) });
                    if (getValue != null)
                    {
                        var exePath = getValue.Invoke(null, new object[] { REG_PATH, "ExePath", string.Empty }) as string;
                        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                        {
                            var setValue = registry.GetMethod("SetValue", new[] { typeof(string), typeof(string), typeof(object) });
                            if (setValue != null)
                            {
                                setValue.Invoke(null, new object[] { REG_PATH, "ExePath", Path.Combine(Application.StartupPath, "DearUnityModManager.exe") });
                                setValue.Invoke(null, new object[] { REG_PATH, "Path", Application.StartupPath });
                            }
                        }
                    }
                    break;
                }
            }
            var rbWidth = 0;
            for (var i = (InstallType)0; i < InstallType.Count; i++)
            {
                var rb = new RadioButton
                {
                    Name = i.ToString(),
                    Text = i == InstallType.DoorstopProxy ? $"{i.ToString()}（推荐）" : i.ToString(),
                    AutoSize = true,
                    Location = new Point(rbWidth + 8, 50),
                    Margin = new Padding(0)
                };
                rb.Click += installType_Click;
                installTypeGroup.Controls.Add(rb);
                rbWidth += rb.Width + 200;
            }
            version = typeof(UnityModManager).Assembly.GetName().Version;
            currentVersion.Text = version.ToString();
            config = Config.Load();
            param = Param.Load();
            skinSetBox.SelectedIndex = param.LastSelectedSkin;
            if (config?.GameInfo != null && config.GameInfo.Length > 0)
            {
                config.GameInfo = config.GameInfo.OrderBy(x => x.GameName).ToArray();
                gameList.Items.AddRange(config.GameInfo);

                GameInfo selected = null;
                if (!string.IsNullOrEmpty(param.LastSelectedGame))
                {
                    selected = config.GameInfo.FirstOrDefault(x => x.Name == param.LastSelectedGame);
                }
                selected = selected ?? config.GameInfo.First();
                gameList.SelectedItem = selected;
                selectedGameParams = param.GetGameParam(selected);
            }
            else
            {
                InactiveForm();
                Log.Print($"解析配置文件“{Config.filename}”失败！");
                return;
            }
            CheckLastVersion();
        }

        #region 窗体缩放      
        private void UnityModLoaderForm_SizeChanged(object sender, EventArgs e)
        {
            _autoSizeFormControlUtil?.FormSizeChanged();
        }
        #endregion

        #region 自定义更换皮肤      
        private void UnityModLoaderForm_SkinChanged(object sender, EventArgs e)
        {
            var skin = skinSetBox.SelectedValue.ToString();
            if (!string.IsNullOrEmpty(skin))
            {
                skinEngine.Active = true;
                skinEngine.SkinFile = skin;
            }
            else
                skinEngine.Active = false;
        }
        #endregion

        private void installType_Click(object sender, EventArgs e)
        {
            var btn = (sender as RadioButton);
            if (!btn.Checked) return;
            selectedGameParams.InstallType = (InstallType)Enum.Parse(typeof(InstallType), btn.Name);
            RefreshForm();
        }

        private void UnityModLoaderForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (config == null) return;
            Log.Writer.Close();
            param.LastSelectedSkin = skinSetBox.SelectedIndex;
            param.Sync(config.GameInfo);
            param.Save();
        }

        private void InactiveForm()
        {
            btnInstall.Enabled = false;
            btnRemove.Enabled = false;
            btnRestore.Enabled = false;
            tabControl.TabPages[1].Enabled = false;
            installedVersion.Text = "-";

            foreach (var ctrl in installTypeGroup.Controls)
            {
                if (ctrl is RadioButton btn)
                {
                    btn.Enabled = false;
                }
            }
        }

        private bool IsValid(GameInfo gameInfo)
        {
            if (selectedGame == null)
            {
                Log.Print("请先选定一个游戏！");
                return false;
            }

            var ignoreFields = new List<string>
            {
                nameof(GameInfo.GameExe),
                nameof(GameInfo.GameName),
                nameof(GameInfo.GameVersionPoint),
                nameof(GameInfo.GameScriptName),
                nameof(GameInfo.StartingPoint),
                nameof(GameInfo.UIStartingPoint),
                nameof(GameInfo.OldPatchTarget),
                nameof(GameInfo.Comment),
                nameof(GameInfo.MinimalManagerVersion),
                nameof(GameInfo.FixBlackUI)
            };

            var prefix = (!string.IsNullOrEmpty(gameInfo.Name) ? $"[{gameInfo.Name}]" : "[?]");
            var hasError = false;
            foreach (var field in typeof(GameInfo).GetFields())
            {
                if (field.IsStatic || !field.IsPublic || ignoreFields.Exists(x => x == field.Name)) continue;
                var value = field.GetValue(gameInfo);
                if (value != null && value.ToString() != "") continue;
                hasError = true;
                Log.Print($"节点“{prefix}”的子节点“{field.Name}”值为空！");
            }

            return !hasError && (string.IsNullOrEmpty(gameInfo.EntryPoint) || Utils.TryParseEntryPoint(gameInfo.EntryPoint, out _)) && (string.IsNullOrEmpty(gameInfo.StartingPoint) || Utils.TryParseEntryPoint(gameInfo.StartingPoint, out _)) && (string.IsNullOrEmpty(gameInfo.UIStartingPoint) || Utils.TryParseEntryPoint(gameInfo.UIStartingPoint, out _)) && (string.IsNullOrEmpty(gameInfo.OldPatchTarget) || Utils.TryParseEntryPoint(gameInfo.OldPatchTarget, out _));
        }

        private void RefreshForm()
        {
            if (!IsValid(selectedGame))
            {
                InactiveForm();
                return;
            }

            btnInstall.Text = "安装MOD管理器模块到游戏";
            btnRestore.Enabled = false;
            gamePath = "";

            if (string.IsNullOrEmpty(selectedGameParams.Path) || !Directory.Exists(selectedGameParams.Path))
            {
                var result = FindGameFolder(selectedGame.Folder);
                if (string.IsNullOrEmpty(result))
                {
                    InactiveForm();
                    btnOpenFolder.ForeColor = Color.Red;
                    btnOpenFolder.Text = "选择游戏主目录";
                    folderBrowserDialog.SelectedPath = null;
                    Log.Print($"游戏主目录“{selectedGame.Folder}”不存在！");
                    return;
                }
                Log.Print($"已检测到游戏主目录“{result}”。");
                selectedGameParams.Path = result;
            }

            if (!Utils.IsUnixPlatform() && !Directory.GetFiles(selectedGameParams.Path, "*.exe", SearchOption.TopDirectoryOnly).Any())
            {
                InactiveForm();
                Log.Print("请选择游戏可执行文件所在的目录。");
                return;
            }

            if (Utils.IsMacPlatform() && !selectedGameParams.Path.EndsWith(".app"))
            {
                InactiveForm();
                Log.Print("请选择游戏可执行文件（扩展名为.app）所在的目录。");
                return;
            }

            Utils.TryParseEntryPoint(selectedGame.EntryPoint, out var assemblyName);
            gamePath = selectedGameParams.Path;
            btnOpenFolder.ForeColor = Color.Green;
            btnOpenFolder.Text = new DirectoryInfo(gamePath).Name;
            folderBrowserDialog.SelectedPath = gamePath;
            managedPath = FindManagedFolder(gamePath);
            managerPath = Path.Combine(managedPath, nameof(UnityModManager));
            entryAssemblyPath = Path.Combine(managedPath, assemblyName);
            injectedEntryAssemblyPath = entryAssemblyPath;
            managerAssemblyPath = Path.Combine(managerPath, typeof(UnityModManager).Module.Name);
            entryPoint = selectedGame.EntryPoint;
            injectedEntryPoint = selectedGame.EntryPoint;
            assemblyDef = null;
            injectedAssemblyDef = null;
            managerDef = null;
            doorstopPath = Path.Combine(gamePath, doorstopFilename);
            doorstopConfigPath = Path.Combine(gamePath, doorstopConfigFilename);

            libraryPaths = new List<string>();
            var gameSupportVersion = !string.IsNullOrEmpty(selectedGame.MinimalManagerVersion) ? Utils.ParseVersion(selectedGame.MinimalManagerVersion) : VER_0_22;
            foreach (var item in libraryFiles.Where(item => (item.Value & LibIncParam.Minimal_lt_0_22) <= 0 || gameSupportVersion < VER_0_22))
                libraryPaths.Add(Path.Combine(managerPath, item.Key));

            if (!string.IsNullOrEmpty(selectedGame.GameExe))
            {
                if (selectedGame.GameExe.Contains('*'))
                    foreach (var file in new DirectoryInfo(gamePath).GetFiles(selectedGame.GameExe, SearchOption.TopDirectoryOnly))
                        selectedGame.GameExe = file.Name;
                gameExePath = Path.Combine(gamePath, selectedGame.GameExe);
            }
            else
                gameExePath = string.Empty;

            var path = new DirectoryInfo(Application.StartupPath).FullName;
            if (path.StartsWith(gamePath))
            {
                InactiveForm();
                Log.Print("DUMM目录不能放在游戏主目录及其子目录下，请先关闭DUMM，再将DUMM目录移动到单独的目录下再试！");
                return;
            }

            try
            {
                assemblyDef = ModuleDefMD.Load(File.ReadAllBytes(entryAssemblyPath));
            }
            catch (Exception e)
            {
                InactiveForm();
                Log.Print(e + Environment.NewLine + entryAssemblyPath);
                return;
            }

            var useOldPatchTarget = false;
            GameInfo.filepathInGame = Path.Combine(managerPath, "Config.xml");

            if (File.Exists(GameInfo.filepathInGame))
            {
                var gameConfig = GameInfo.ImportFromGame();
                if (gameConfig == null || !Utils.TryParseEntryPoint(gameConfig.EntryPoint, out assemblyName))
                {
                    InactiveForm();
                    return;
                }
                injectedEntryPoint = gameConfig.EntryPoint;
                injectedEntryAssemblyPath = Path.Combine(managedPath, assemblyName);
            }
            else if (!string.IsNullOrEmpty(selectedGame.OldPatchTarget))
            {
                if (!Utils.TryParseEntryPoint(selectedGame.OldPatchTarget, out assemblyName))
                {
                    InactiveForm();
                    return;
                }
                useOldPatchTarget = true;
                injectedEntryPoint = selectedGame.OldPatchTarget;
                injectedEntryAssemblyPath = Path.Combine(managedPath, assemblyName);
            }

            try
            {
                injectedAssemblyDef = injectedEntryAssemblyPath == entryAssemblyPath ? assemblyDef : ModuleDefMD.Load(File.ReadAllBytes(injectedEntryAssemblyPath));
                if (File.Exists(managerAssemblyPath))
                    managerDef = ModuleDefMD.Load(File.ReadAllBytes(managerAssemblyPath));
            }
            catch (Exception e)
            {
                InactiveForm();
                Log.Print(e + Environment.NewLine + injectedEntryAssemblyPath + Environment.NewLine + managerAssemblyPath);
                return;
            }

            var disabledMethods = new List<InstallType>();
            var unavailableMethods = new List<InstallType>();
            var managerType = typeof(UnityModManager);
            var starterType = typeof(UnityModManagerStarter);

        Rescan:
            var v0_12_Installed = injectedAssemblyDef.Types.FirstOrDefault(x => x.Name == managerType.Name);
            var newWayInstalled = injectedAssemblyDef.Types.FirstOrDefault(x => x.Name == starterType.Name);
            var hasInjectedAssembly = v0_12_Installed != null || newWayInstalled != null;

            if (useOldPatchTarget && !hasInjectedAssembly)
            {
                useOldPatchTarget = false;
                injectedEntryPoint = selectedGame.EntryPoint;
                injectedEntryAssemblyPath = entryAssemblyPath;
                injectedAssemblyDef = assemblyDef;
                goto Rescan;
            }

            if (Utils.IsUnixPlatform() || !File.Exists(gameExePath))
            {
                unavailableMethods.Add(InstallType.DoorstopProxy);
                selectedGameParams.InstallType = InstallType.Assembly;
            }
            else if (File.Exists(doorstopPath))
            {
                disabledMethods.Add(InstallType.Assembly);
                selectedGameParams.InstallType = InstallType.DoorstopProxy;
            }

            if (hasInjectedAssembly)
            {
                disabledMethods.Add(InstallType.DoorstopProxy);
                selectedGameParams.InstallType = InstallType.Assembly;
            }

            foreach (var ctrl in installTypeGroup.Controls)
            {
                if (!(ctrl is RadioButton btn)) continue;
                if (unavailableMethods.Exists(x => x.ToString() == btn.Name))
                {
                    btn.Visible = false;
                    btn.Enabled = false;
                    continue;
                }
                if (disabledMethods.Exists(x => x.ToString() == btn.Name))
                {
                    btn.Visible = true;
                    btn.Enabled = false;
                    continue;
                }

                btn.Visible = true;
                btn.Enabled = true;
                btn.Checked = btn.Name == selectedGameParams.InstallType.ToString();
            }

            if (selectedGameParams.InstallType == InstallType.Assembly)
                btnRestore.Enabled = IsDirty(injectedAssemblyDef) && File.Exists($"{injectedEntryAssemblyPath}{Utils.FileSuffixCache}");

            tabControl.TabPages[1].Enabled = true;
            managerDef ??= injectedAssemblyDef;
            var managerInstalled = managerDef.Types.FirstOrDefault(x => x.Name == managerType.Name);

            if (managerInstalled != null && (hasInjectedAssembly || selectedGameParams.InstallType == InstallType.DoorstopProxy))
            {
                btnInstall.Text = "更新MOD管理器模块";
                btnInstall.Enabled = false;
                btnRemove.Enabled = true;
                Version version2;

                if (v0_12_Installed != null)
                {
                    var versionString = managerInstalled.Fields.First(x => x.Name == nameof(UnityModManager.version)).Constant.Value.ToString();
                    version2 = Utils.ParseVersion(versionString);
                }
                else
                    version2 = managerDef.Assembly.Version;

                installedVersion.Text = version2.ToString();

                if (version > version2 && v0_12_Installed == null)
                    btnInstall.Enabled = true;
            }
            else
            {
                installedVersion.Text = "-";
                btnInstall.Enabled = true;
                btnRemove.Enabled = false;
            }
        }

        private string FindGameFolder(string str)
        {
            var disks = new[] { @"C:\", @"D:\", @"E:\", @"F:\" };
            var roots = new[] { "Games", "Program files", "Program files (x86)", "" };
            var folders = new[] { @"Steam\SteamApps\common", @"GoG Galaxy\Games", "" };

            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                disks = new[] { Environment.GetEnvironmentVariable("HOME") };
                roots = new[] { "Library/Application Support", ".steam" };
                folders = new[] { "Steam/SteamApps/common", "steam/steamapps/common", "Steam/steamapps/common" };
            }

            foreach (var disk in disks)
                foreach (var root in roots)
                    foreach (var folder in folders)
                    {
                        var path = Path.Combine(disk, root);
                        path = Path.Combine(path, folder);
                        path = Path.Combine(path, str);
                        if (!Directory.Exists(path)) continue;
                        if (!Utils.IsMacPlatform()) return path;
                        foreach (var dir in Directory.GetDirectories(path))
                        {
                            if (!dir.EndsWith(".app")) continue;
                            path = Path.Combine(path, dir);
                            break;
                        }
                        return path;
                    }
            return null;
        }

        private string FindManagedFolder(string path)
        {
            if (Utils.IsMacPlatform())
            {
                var dir = $"{path}/Contents/Resources/Data/Managed";
                if (Directory.Exists(dir))
                    return dir;
            }

            foreach (var di in new DirectoryInfo(path).GetDirectories())
            {
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                var dir = di.FullName;
                if (dir.EndsWith("Managed") && (File.Exists(Path.Combine(dir, "Assembly-CSharp.dll")) || File.Exists(Path.Combine(dir, "UnityEngine.dll"))))
                    return dir;
                var result = FindManagedFolder(dir);
                if (!string.IsNullOrEmpty(result))
                    return result;
            }

            return null;
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            if (!TestWritePermissions())
            {
                return;
            }
            if (selectedGameParams.InstallType == InstallType.DoorstopProxy)
            {
                InstallDoorstop(Actions.Remove);
            }
            else
            {
                InjectAssembly(Actions.Remove, injectedAssemblyDef);
            }

            RefreshForm();
        }

        private void btnInstall_Click(object sender, EventArgs e)
        {
            if (!TestWritePermissions())
            {
                return;
            }
            var modsPath = Path.Combine(gamePath, selectedGame.ModsDirectory);
            if (!Directory.Exists(modsPath))
            {
                Directory.CreateDirectory(modsPath);
            }
            if (selectedGameParams.InstallType == InstallType.DoorstopProxy)
            {
                InstallDoorstop(Actions.Install);
            }
            else
            {
                InjectAssembly(Actions.Install, assemblyDef);
            }

            RefreshForm();
        }

        private void btnRestore_Click(object sender, EventArgs e)
        {
            if (selectedGameParams.InstallType == InstallType.Assembly)
            {
                var injectedEntryAssemblyPath = Path.Combine(managedPath, injectedAssemblyDef.Name);
                var originalAssemblyPath = $"{injectedEntryAssemblyPath}{Utils.FileSuffixCache}";
                RestoreOriginal(injectedEntryAssemblyPath, originalAssemblyPath);
            }

            RefreshForm();
        }

        private void btnDownloadUpdate_Click(object sender, EventArgs e)
        {
            try
            {
                if (Resources.btnDownloadUpdate.Equals(btnDownloadUpdate.Text))
                {
                    if (!string.IsNullOrEmpty(config.HomePage))
                        Process.Start(config.HomePage);
                }
                else
                {
                    Process.Start(Resources.appUpdater);
                }
            }
            catch (Exception ex)
            {
                Log.Print(ex.ToString());
            }
        }

        private void btnOpenFolder_Click(object sender, EventArgs e)
        {
            var result = folderBrowserDialog.ShowDialog();
            if (result != DialogResult.OK) return;
            selectedGameParams.Path = folderBrowserDialog.SelectedPath;
            RefreshForm();
        }

        private void gameList_Changed(object sender, EventArgs e)
        {
            additionallyGroupBox.Visible = false;
            var selected = (GameInfo)((ComboBox)sender).SelectedItem;
            if (selected != null)
            {
                Log.Print($"切换游戏为“{selected.Name}”。");
                param.LastSelectedGame = selected.Name;
                selectedGameParams = param.GetGameParam(selected);
                if (!string.IsNullOrEmpty(selectedGameParams.Path))
                    Log.Print($"游戏目录“{selectedGameParams.Path}”。");
            }

            if (!string.IsNullOrEmpty(selected.Comment))
            {
                notesTextBox.Text = selected.Comment;
                additionallyGroupBox.Visible = true;
            }

            RefreshForm();
        }

        enum Actions
        {
            Install,
            Remove
        }

        private bool InstallDoorstop(Actions action, bool write = true)
        {
            var gameConfigPath = GameInfo.filepathInGame;

            var success = false;
            switch (action)
            {
                case Actions.Install:
                    try
                    {
                        Log.Print("=======================================");

                        if (!Directory.Exists(managerPath))
                            Directory.CreateDirectory(managerPath);

                        Utils.MakeBackup(doorstopPath);
                        Utils.MakeBackup(doorstopConfigPath);
                        Utils.MakeBackup(libraryPaths);

                        if (!InstallDoorstop(Actions.Remove, false))
                        {
                            Log.Print("安装管理器模块到游戏失败，不能卸载上一个版本！");
                            goto EXIT;
                        }

                        Log.Print("正在复制文件到游戏……");
                        var arch = Utils.UnmanagedDllIs64Bit(gameExePath);
                        var filename = arch == true ? doorstopFilenameX64 : doorstopFilenameX86;
                        Log.Print($"  '{filename}'");
                        File.Copy(filename, doorstopPath, true);
                        Log.Print($"  '{doorstopConfigFilename}'");
                        File.WriteAllText(doorstopConfigPath, $@"[UnityDoorstop]{Environment.NewLine}enabled = true{Environment.NewLine}targetAssembly = {managerAssemblyPath}");
                        DoactionLibraries(Actions.Install);
                        DoactionGameConfig(Actions.Install);
                        Log.Print("安装管理器模块到游戏成功！");

                        success = true;
                    }
                    catch (Exception e)
                    {
                        Log.Print(e.ToString());
                        Utils.RestoreBackup(doorstopPath);
                        Utils.RestoreBackup(doorstopConfigPath);
                        Utils.RestoreBackup(libraryPaths);
                        Utils.RestoreBackup(gameConfigPath);
                        Log.Print("安装管理器模块到游戏失败！");
                    }
                    break;

                case Actions.Remove:
                    try
                    {
                        if (write)
                        {
                            Log.Print("=======================================");
                        }

                        Utils.MakeBackup(gameConfigPath);
                        if (write)
                        {
                            Utils.MakeBackup(doorstopPath);
                            Utils.MakeBackup(doorstopConfigPath);
                            Utils.MakeBackup(libraryPaths);
                        }

                        Log.Print("正在从游戏目录删除文件……");
                        Log.Print($"  '{doorstopFilename}'");
                        File.Delete(doorstopPath);
                        Log.Print($"  '{doorstopConfigFilename}'");
                        File.Delete(doorstopConfigPath);

                        if (write)
                        {
                            DoactionLibraries(Actions.Remove);
                            DoactionGameConfig(Actions.Remove);
                            Log.Print("从游戏目录删除文件成功！");
                        }

                        success = true;
                    }
                    catch (Exception e)
                    {
                        Log.Print(e.ToString());
                        if (write)
                        {
                            Utils.RestoreBackup(doorstopPath);
                            Utils.RestoreBackup(doorstopConfigPath);
                            Utils.RestoreBackup(libraryPaths);
                            Utils.RestoreBackup(gameConfigPath);
                            Log.Print("从游戏目录删除文件失败！");
                        }
                    }
                    break;
            }
        EXIT:
            if (write)
            {
                Utils.DeleteBackup(doorstopPath);
                Utils.DeleteBackup(doorstopConfigPath);
                Utils.DeleteBackup(libraryPaths);
                Utils.DeleteBackup(gameConfigPath);
            }
            return success;
        }

        private bool InjectAssembly(Actions action, ModuleDefMD assemblyDef, bool write = true)
        {
            var managerType = typeof(UnityModManager);
            var starterType = typeof(UnityModManagerStarter);
            var gameConfigPath = GameInfo.filepathInGame;

            var assemblyPath = Path.Combine(managedPath, assemblyDef.Name);
            var originalAssemblyPath = $"{assemblyPath}{Utils.FileSuffixCache}";

            var success = false;

            switch (action)
            {
                case Actions.Install:
                    {
                        try
                        {
                            Log.Print("=======================================");

                            if (!Directory.Exists(managerPath))
                                Directory.CreateDirectory(managerPath);

                            Utils.MakeBackup(assemblyPath);
                            Utils.MakeBackup(libraryPaths);

                            if (!IsDirty(assemblyDef))
                            {
                                File.Copy(assemblyPath, originalAssemblyPath, true);
                                MakeDirty(assemblyDef);
                            }

                            if (!InjectAssembly(Actions.Remove, injectedAssemblyDef, assemblyDef != injectedAssemblyDef))
                            {
                                Log.Print("安装管理器模块到游戏失败，不能卸载上一个版本！");
                                goto EXIT;
                            }

                            Log.Print($"正在注入文件“{Path.GetFileName(assemblyPath)}”……");

                            if (!Utils.TryGetEntryPoint(assemblyDef, entryPoint, out var methodDef, out var insertionPlace, true))
                            {
                                goto EXIT;
                            }

                            var starterDef = ModuleDefMD.Load(starterType.Module);
                            var starter = starterDef.Types.First(x => x.Name == starterType.Name);
                            starterDef.Types.Remove(starter);
                            assemblyDef.Types.Add(starter);

                            var instr = OpCodes.Call.ToInstruction(starter.Methods.First(x => x.Name == nameof(UnityModManagerStarter.Start)));
                            if (insertionPlace == "before")
                            {
                                methodDef.Body.Instructions.Insert(0, instr);
                            }
                            else
                            {
                                methodDef.Body.Instructions.Insert(methodDef.Body.Instructions.Count - 1, instr);
                            }

                            assemblyDef.Write(assemblyPath);
                            DoactionLibraries(Actions.Install);
                            DoactionGameConfig(Actions.Install);

                            Log.Print("安装管理器模块到游戏成功！");

                            success = true;
                        }
                        catch (Exception e)
                        {
                            Log.Print(e.ToString());
                            Utils.RestoreBackup(assemblyPath);
                            Utils.RestoreBackup(libraryPaths);
                            Utils.RestoreBackup(gameConfigPath);
                            Log.Print("安装管理器模块到游戏失败！");
                        }
                    }
                    break;

                case Actions.Remove:
                    {
                        try
                        {
                            if (write)
                            {
                                Log.Print("=======================================");
                            }

                            Utils.MakeBackup(gameConfigPath);

                            var v0_12_Installed = assemblyDef.Types.FirstOrDefault(x => x.Name == managerType.Name);
                            var newWayInstalled = assemblyDef.Types.FirstOrDefault(x => x.Name == starterType.Name);

                            if (v0_12_Installed != null || newWayInstalled != null)
                            {
                                if (write)
                                {
                                    Utils.MakeBackup(assemblyPath);
                                    Utils.MakeBackup(libraryPaths);
                                }

                                Log.Print("正在从游戏卸载管理器模块……");

                                Instruction instr = null;

                                if (newWayInstalled != null)
                                {
                                    instr = OpCodes.Call.ToInstruction(newWayInstalled.Methods.First(x => x.Name == nameof(UnityModManagerStarter.Start)));
                                }
                                else if (v0_12_Installed != null)
                                {
                                    instr = OpCodes.Call.ToInstruction(v0_12_Installed.Methods.First(x => x.Name == nameof(UnityModManager.Start)));
                                }

                                if (!string.IsNullOrEmpty(injectedEntryPoint))
                                {
                                    if (!Utils.TryGetEntryPoint(assemblyDef, injectedEntryPoint, out var methodDef, out _, true))
                                    {
                                        goto EXIT;
                                    }

                                    for (var i = 0; i < methodDef.Body.Instructions.Count; i++)
                                    {
                                        if (methodDef.Body.Instructions[i].OpCode != instr.OpCode ||
                                            methodDef.Body.Instructions[i].Operand != instr.Operand) continue;
                                        methodDef.Body.Instructions.RemoveAt(i);
                                        break;
                                    }
                                }

                                if (newWayInstalled != null)
                                    assemblyDef.Types.Remove(newWayInstalled);
                                else if (v0_12_Installed != null)
                                    assemblyDef.Types.Remove(v0_12_Installed);

                                if (!IsDirty(assemblyDef))
                                {
                                    MakeDirty(assemblyDef);
                                }

                                if (write)
                                {
                                    assemblyDef.Write(assemblyPath);
                                    DoactionLibraries(Actions.Remove);
                                    DoactionGameConfig(Actions.Remove);
                                    Log.Print("从游戏卸载管理器模块成功！");
                                }
                            }

                            success = true;
                        }
                        catch (Exception e)
                        {
                            Log.Print(e.ToString());
                            if (write)
                            {
                                Utils.RestoreBackup(assemblyPath);
                                Utils.RestoreBackup(libraryPaths);
                                Utils.RestoreBackup(gameConfigPath);
                                Log.Print("从游戏卸载管理器模块失败！");
                            }
                        }
                    }
                    break;
            }

        EXIT:
            if (!write) return success;
            Utils.DeleteBackup(assemblyPath);
            Utils.DeleteBackup(libraryPaths);
            Utils.DeleteBackup(gameConfigPath);
            return success;
        }

        private static bool IsDirty(ModuleDefMD assembly)
        {
            return assembly.Types.FirstOrDefault(x => x.FullName == typeof(IsDirty).FullName || x.Name == typeof(UnityModManager).Name) != null;
        }

        private static void MakeDirty(ModuleDefMD assembly)
        {
            var moduleDef = ModuleDefMD.Load(typeof(IsDirty).Module);
            var typeDef = moduleDef.Types.FirstOrDefault(x => x.FullName == typeof(IsDirty).FullName);
            moduleDef.Types.Remove(typeDef);
            assembly.Types.Add(typeDef);
        }

        private bool TestWritePermissions()
        {
            var success = true;
            success = Utils.IsDirectoryWritable(managedPath) && success;
            success = Utils.IsFileWritable(managerAssemblyPath) && success;
            success = Utils.IsFileWritable(GameInfo.filepathInGame) && success;
            success = libraryPaths.Aggregate(success, (current, file) => Utils.IsFileWritable(file) && current);

            if (selectedGameParams.InstallType == InstallType.DoorstopProxy)
            {
                success = Utils.IsFileWritable(doorstopPath) && success;
            }
            else
            {
                success = Utils.IsFileWritable(entryAssemblyPath) && success;
                if (injectedEntryAssemblyPath != entryAssemblyPath)
                    success = Utils.IsFileWritable(injectedEntryAssemblyPath) && success;
            }

            return success;
        }

        private static bool RestoreOriginal(string file, string backup)
        {
            try
            {
                File.Copy(backup, file, true);
                Log.Print("已还原游戏原始文件！");
                File.Delete(backup);
                return true;
            }
            catch (Exception e)
            {
                Log.Print(e.Message);
            }

            return false;
        }

        private static void DoactionLibraries(Actions action)
        {
            Log.Print(action == Actions.Install ? "正在安装管理器模块到游戏……" : "正在从游戏卸载管理器模块……");

            foreach (var path in libraryPaths)
            {
                var filename = Path.GetFileName(path);
                if (action == Actions.Install)
                {
                    if (File.Exists(path))
                    {
                        var source = new FileInfo(filename);
                        var dest = new FileInfo(path);
                        if (dest.LastWriteTimeUtc == source.LastWriteTimeUtc)
                            continue;
                    }

                    Log.Print($"  {filename}");
                    File.Copy(filename, path, true);
                }
                else
                {
                    if (!File.Exists(path)) continue;
                    Log.Print($"  {filename}");
                    File.Delete(path);
                }
            }
        }

        private void DoactionGameConfig(Actions action)
        {
            if (action == Actions.Install)
            {
                Log.Print("已创建配置文件“Config.xml”。");
                selectedGame.ExportToGame();
            }
            else if (File.Exists(GameInfo.filepathInGame))
            {
                Log.Print("已删除配置文件“Config.xml”。");
                File.Delete(GameInfo.filepathInGame);
            }
        }

        private void folderBrowserDialog_HelpRequest(object sender, EventArgs e)
        {
        }

        private void tabs_Changed(object sender, EventArgs e)
        {
            switch (tabControl.SelectedIndex)
            {
                case 1: // Mods
                    ReloadMods();
                    RefreshModList();
                    if (!_repositories.ContainsKey(selectedGame))
                        CheckModUpdates();
                    break;
            }
        }

        private void notesTextBox_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }

        private void statusStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
        }

        private void splitContainerMain_Panel2_Paint(object sender, PaintEventArgs e)
        {
        }

        private void splitContainerModsInstall_Panel2_Paint(object sender, PaintEventArgs e)
        {
        }
    }
}
