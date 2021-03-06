﻿using MassEffectModManagerCore.modmanager.helpers;
using MassEffectModManagerCore.modmanager.me3tweaks;
using MassEffectModManagerCore.ui;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using MassEffectModManagerCore.modmanager.localizations;
using MassEffectModManagerCore.modmanager.memoryanalyzer;

namespace MassEffectModManagerCore.modmanager.usercontrols
{
    /// <summary>
    /// Interaction logic for ModMakerPanel.xaml
    /// </summary>
    public partial class ModMakerPanel : MMBusyPanelBase
    {
        private bool KeepOpenWhenThreadFinishes;
        public bool ShowCloseButton { get; set; }
        public string ModMakerCode { get; set; }
        public OnlineContent.ServerModMakerModInfo SelectedTopMod { get; set; }
        public long CurrentTaskValue { get; private set; }
        public long CurrentTaskMaximum { get; private set; } = 100;
        public bool CurrentTaskIndeterminate { get; private set; }
        public long OverallValue { get; private set; }
        public long OverallMaximum { get; private set; } = 100;
        public bool OverallIndeterminate { get; private set; }
        public bool CompileInProgress { get; set; }
        public string DownloadAndModNameText { get; set; } = M3L.GetString(M3L.string_enterModMakerModCodeOrSelectFromTheTopMods);
        public string CurrentTaskString { get; set; }
        public ModMakerPanel()
        {
            MemoryAnalyzer.AddTrackedMemoryItem(@"ModMaker Panel", new WeakReference(this));
            DataContext = this;
            LoadCommands();
            InitializeComponent();
            GetTopMods();
        }

        private void GetTopMods()
        {
            NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"ModMaker-TopModsFetch");
            nbw.DoWork += (a, b) =>
            {
                b.Result = OnlineContent.FetchTopModMakerMods();
            };
            nbw.RunWorkerCompleted += (a, b) =>
            {
                if (b.Error == null && b.Result is List<OnlineContent.ServerModMakerModInfo> topMods)
                {
                    TopMods.ReplaceAll(topMods);
                }
            };
            nbw.RunWorkerAsync();
        }

        public ObservableCollectionExtended<OnlineContent.ServerModMakerModInfo> TopMods { get; } = new ObservableCollectionExtended<OnlineContent.ServerModMakerModInfo>();

        public ICommand DownloadCompileCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }
        public ICommand OpenModMakerCommand { get; private set; }

        private void LoadCommands()
        {
            DownloadCompileCommand = new GenericCommand(StartCompiler, CanStartCompiler);
            CloseCommand = new GenericCommand(ClosePanel, CanClose);
            OpenModMakerCommand = new GenericCommand(OpenModMaker);
        }

        private void OpenModMaker()
        {
            Utilities.OpenWebpage(@"https://me3tweaks.com/modmaker");
        }

        private void ClosePanel()
        {
            OnClosing(DataEventArgs.Empty);
        }

        public void OnSelectedTopModChanged()
        {
            if (SelectedTopMod != null)
            {
                ModMakerCode = SelectedTopMod.mod_id;
            }
        }

        private bool CanClose() => !CompileInProgress;

        private void StartCompiler()
        {
            CompileInProgress = true;
            NamedBackgroundWorker bw = new NamedBackgroundWorker(@"ModmakerCompiler");

            bw.DoWork += (a, b) =>
            {
                //Todo: Add checkbox to use local version instead
                if (int.TryParse(ModMakerCode, out var code))
                {
                    DownloadAndModNameText = @"Downloading mod delta from ME3Tweaks";
                    var normalEndpoint = OnlineContent.ModmakerModsEndpoint + code;
                    var lzmaEndpoint = normalEndpoint + @"&method=lzma";

                    string modDelta = null;

                    //Try LZMA first
                    try
                    {
                        var download = OnlineContent.DownloadToMemory(lzmaEndpoint, (done, total) =>
                        {
                            if (total != -1)
                            {
                                var suffix = $@"{(done * 100.0 / total).ToString(@"0")}%"; //do not localize
                                DownloadAndModNameText = M3L.GetString(M3L.string_downloadingModDeltaFromME3Tweaks) + suffix;
                            }
                            else
                            {
                                DownloadAndModNameText = M3L.GetString(M3L.string_downloadingModDeltaFromME3Tweaks);
                            }
                        });
                        if (download.errorMessage == null)
                        {
                            DownloadAndModNameText = M3L.GetString(M3L.string_decompressingDelta);
                            // OK
                            var decompressed = SevenZipHelper.LZMA.DecompressLZMAFile(download.result.ToArray());
                            modDelta = Encoding.UTF8.GetString(decompressed);
                            // File.WriteAllText(@"C:\users\mgamerz\desktop\decomp.txt", modDelta);
                        }
                        else
                        {
                            Log.Error(@"Error downloading lzma mod delta to memory: " + download.errorMessage);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error(@"Error downloading LZMA mod delta to memory: " + e.Message);
                    }

                    if (modDelta == null)
                    {
                        //failed to download LZMA.
                        var download = OnlineContent.DownloadToMemory(normalEndpoint, (done, total) =>
                        {
                            var suffix = $"{(done * 100.0 / total).ToString(@"0")}%"; //do not localize
                            DownloadAndModNameText = M3L.GetString(M3L.string_downloadingModDeltaFromME3Tweaks) + suffix;
                        });
                        if (download.errorMessage == null)
                        {
                            //OK
                            modDelta = Encoding.UTF8.GetString(download.result.ToArray());
                        }
                        else
                        {
                            Log.Error(@"Error downloading decompressed mod delta to memory: " + download.errorMessage);
                        }
                    }


                    if (modDelta != null)
                    {
                        KeepOpenWhenThreadFinishes = false;
                        var compiler = new ModMakerCompiler(code);
                        compiler.SetCurrentMaxCallback = SetCurrentMax;
                        compiler.SetCurrentValueCallback = SetCurrentProgressValue;
                        compiler.SetOverallMaxCallback = SetOverallMax;
                        compiler.SetOverallValueCallback = SetOverallValue;
                        compiler.SetCurrentTaskIndeterminateCallback = SetCurrentTaskIndeterminate;
                        compiler.SetCurrentTaskStringCallback = SetCurrentTaskString;
                        compiler.SetModNameCallback = SetModNameOrDownloadText;
                        compiler.SetCompileStarted = CompilationInProgress;
                        compiler.SetModNotFoundCallback = ModNotFound;
                        compiler.NotifySomeDLCIsMissing = NotifySomeDLCIsMissing;
                        Mod m = compiler.DownloadAndCompileMod(modDelta);
                        File.WriteAllText(System.IO.Path.Combine(Utilities.GetModmakerDefinitionsCache(), code + @".xml"), modDelta);
                        b.Result = m;
                    }
                }
            };
            bw.RunWorkerCompleted += (a, b) =>
            {
                CompileInProgress = false;
                if (!KeepOpenWhenThreadFinishes && b.Result is Mod m)
                {
                    OnClosing(new DataEventArgs(m));
                }
                else
                {
                    CloseProgressPanel();
                    ShowCloseButton = true;
                }
                CommandManager.InvalidateRequerySuggested();

            };
            bw.RunWorkerAsync();
        }

        private bool NotifySomeDLCIsMissing(List<string> listItems)
        {
            bool result = false;
            Application.Current.Dispatcher.Invoke(delegate
            {
                var missingDLC = string.Join("\n - ", listItems); //do not localize
                missingDLC = @" - " + missingDLC; //add first -
                result = M3L.ShowDialog(window,
                             M3L.GetString(M3L.string_interp_modmakerDlcMissing, missingDLC),
                             M3L.GetString(M3L.string_dlcMissing),
                             MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            });
            return result;
        }

        private void ModNotFound()
        {
            KeepOpenWhenThreadFinishes = true;
        }

        private void SetModNameOrDownloadText(string obj)
        {
            DownloadAndModNameText = obj;
        }

        private void SetCurrentProgressValue(int obj)
        {
            CurrentTaskValue = obj;
        }

        private void SetCurrentTaskString(string obj)
        {
            CurrentTaskString = obj;
        }

        private void SetCurrentTaskIndeterminate(bool obj)
        {
            CurrentTaskIndeterminate = obj;
        }

        private void SetOverallMax(int obj)
        {
            OverallMaximum = obj;
        }

        private void SetOverallValue(int obj)
        {
            OverallValue = obj;
        }

        private void SetCurrentMax(int obj)
        {
            CurrentTaskMaximum = obj;
        }

        private bool CanStartCompiler() => int.TryParse(ModMakerCode, out var _) && !CompileInProgress && Utilities.GetGameBackupPath(Mod.MEGame.ME3) != null;

        public override void HandleKeyPress(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && CanClose())
            {
                OnClosing(DataEventArgs.Empty);
            }
        }

        public override void OnPanelVisible()
        {
            if (Utilities.GetGameBackupPath(Mod.MEGame.ME3) == null)
            {
                M3L.ShowDialog(mainwindow, M3L.GetString(M3L.string_dialog_me3tweaksModMakerRequiresBackup), M3L.GetString(M3L.string_noBackupAvailable), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void CompilationInProgress()
        {
            //Close entry dialog
            Application.Current.Dispatcher.Invoke(delegate
            {
                Storyboard sb = this.FindResource(@"CloseInfoPanel") as Storyboard;
                if (sb.IsSealed)
                {
                    sb = sb.Clone();
                }
                DownloadInfoPanel.Height = DownloadInfoPanel.ActualHeight;
                Storyboard.SetTarget(sb, DownloadInfoPanel);
                sb.Begin();

                //Open Progress Panel
                sb = this.FindResource(@"OpenProgressPanel") as Storyboard;
                if (sb.IsSealed)
                {
                    sb = sb.Clone();
                }
                DownloadingProgressPanel.Height = DownloadingProgressPanel.ActualHeight;
                Storyboard.SetTarget(sb, DownloadingProgressPanel);
                sb.Begin();
            });
        }

        public void CloseProgressPanel()
        {
            //Close entry dialog
            Application.Current.Dispatcher.Invoke(delegate
            {

                //Open Progress Panel
                var sb = this.FindResource(@"CloseProgressPanel") as Storyboard;
                if (sb.IsSealed)
                {
                    sb = sb.Clone();
                }
                DownloadingProgressPanel.Height = DownloadingProgressPanel.ActualHeight;
                Storyboard.SetTarget(sb, DownloadingProgressPanel);
                sb.Begin();
            });
        }
    }
}
