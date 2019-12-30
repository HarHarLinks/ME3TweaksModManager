﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using MassEffectModManagerCore.GameDirectories;
using MassEffectModManagerCore.gamefileformats.sfar;
using MassEffectModManagerCore.modmanager.helpers;
using MassEffectModManagerCore.modmanager.objects;
using MassEffectModManagerCore.ui;
using ME3Explorer.Unreal;
using Serilog;

namespace MassEffectModManagerCore.modmanager.usercontrols
{
    /// <summary>
    /// In-Window content container for AutoTOC. Most of this class was ported from Mod Manager Command Line Tools.
    /// </summary>
    public partial class AutoTOC : MMBusyPanelBase
    {
        private const string SFAR_SUBPATH = @"CookedPCConsole\Default.sfar";
        private const long TESTPATCH_16_SIZE = 2455091L;
        private static Dictionary<string, long> sfarSizeMap;

        private enum AutoTOCMode
        {
            MODE_GAMEWIDE,
            MODE_MOD
        }

        private AutoTOCMode mode;
        private Mod modModeMod;
        private GameTarget gameWideModeTarget;

        public int Percent { get; private set; }
        public string ActionText { get; private set; }

        public AutoTOC(GameTarget target)
        {
            if (target == null) throw new Exception(@"Null target specified for AutoTOC");
            DataContext = this;
            this.gameWideModeTarget = target;
            InitializeComponent();
        }

        public AutoTOC(Mod mod)
        {
            //TODO: Implement this. Possibly make it static.
            DataContext = this;
            if (mod.Game != Mod.MEGame.ME3) throw new Exception(@"AutoTOC cannot be run on mods not designed for Mass Effect 3.");
            this.modModeMod = mod;
            InitializeComponent();

        }

        private void RunModAutoTOC()
        {
            //Implement mod-only autotoc, for deployments
        }

        private bool RunGameWideAutoTOC()
        {
            Debug.WriteLine(@"FULL AUTOTOC MODE - Updating Unpacked and SFAR TOCs");

            //get toc target folders, ensuring we clean up the inputs a bit.
            string baseDir = Path.GetFullPath(Path.Combine(gameWideModeTarget.TargetPath, @"BIOGame"));
            string dlcDirRoot = MEDirectories.DLCPath(gameWideModeTarget);
            if (!Directory.Exists(dlcDirRoot))
            {
                Log.Error(@"Specified game directory does not appear to be a Mass Effect 3 root game directory (DLC folder missing).");
                return false;
            }

            var tocTargets = (new DirectoryInfo(dlcDirRoot)).GetDirectories().Select(x => x.FullName).Where(x => Path.GetFileName(x).StartsWith(@"DLC_", StringComparison.OrdinalIgnoreCase)).ToList();
            tocTargets.Add(baseDir);
            tocTargets.Add(Path.Combine(gameWideModeTarget.TargetPath, @"BIOGame\Patches\PCConsole\Patch_001.sfar"));

            //Debug.WriteLine("Found TOC Targets:");
            tocTargets.ForEach(x => Debug.WriteLine(x));
            //Debug.WriteLine("=====Generating TOC Files=====");
            int done = 0;

            foreach (var tocTarget in tocTargets)
            {
                string sfar = Path.Combine(tocTarget, SFAR_SUBPATH);
                if (tocTarget.EndsWith(@".sfar"))
                {
                    //TestPatch
                    var watch = Stopwatch.StartNew();
                    DLCPackage dlc = new DLCPackage(tocTarget);
                    var tocResult = dlc.UpdateTOCbin();
                    watch.Stop();
                    if (tocResult == DLCPackage.DLCTOCUpdateResult.RESULT_UPDATE_NOT_NECESSARY)
                    {
                        Log.Information($@"TOC is already up to date in {tocTarget}");
                    }
                    else if (tocResult == DLCPackage.DLCTOCUpdateResult.RESULT_UPDATED)
                    {
                        var elapsedMs = watch.ElapsedMilliseconds;
                        Log.Information($@"{tocTarget} - Ran SFAR TOC, took {elapsedMs}ms");
                    }
                }
                else if (ME3Directory.OfficialDLCNames.ContainsKey(Path.GetFileName(tocTarget)))
                {
                    //Official DLC
                    if (File.Exists(sfar))
                    {
                        if (new FileInfo(sfar).Length == 32) //DLC is unpacked for sure
                        {
                            CreateUnpackedTOC(tocTarget);
                        }
                        else
                        {
                            //AutoTOC it - SFAR is not unpacked
                            var watch = System.Diagnostics.Stopwatch.StartNew();

                            DLCPackage dlc = new DLCPackage(sfar);
                            var tocResult = dlc.UpdateTOCbin();
                            watch.Stop();
                            if (tocResult == DLCPackage.DLCTOCUpdateResult.RESULT_ERROR_NO_ENTRIES)
                            {
                                Log.Information($@"No DLC entries in SFAR... Suspicious. Creating empty TOC for {tocTarget}");
                                CreateUnpackedTOC(tocTarget);
                            }
                            else if (tocResult == DLCPackage.DLCTOCUpdateResult.RESULT_UPDATE_NOT_NECESSARY)
                            {
                                Log.Information($@"TOC is already up to date in {tocTarget}");
                            }
                            else if (tocResult == DLCPackage.DLCTOCUpdateResult.RESULT_UPDATED)
                            {
                                var elapsedMs = watch.ElapsedMilliseconds;
                                Log.Information($@"{Path.GetFileName(tocTarget)} - Ran SFAR TOC, took {elapsedMs}ms");
                            }
                        }
                    }

                }
                else
                {
                    //TOC it unpacked style
                    // Console.WriteLine(foldername + ", - UNPACKED TOC");
                    CreateUnpackedTOC(tocTarget);
                }

                done++;
                Percent = (int)Math.Floor(done * 100.0 / tocTargets.Count);
            }
            return true;
        }

        private void CreateUnpackedTOC(string dlcDirectory)
        {
            Debug.WriteLine(@"Creating unpacked toc for" + dlcDirectory);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            MemoryStream ms = TOCCreator.CreateTOCForDirectory(dlcDirectory);
            if (ms != null)
            {
                string tocPath = Path.Combine(dlcDirectory, @"PCConsoleTOC.bin");
                File.WriteAllBytes(tocPath, ms.ToArray());
                ms.Close();
                watch.Stop();
                var elapsedMs = watch.ElapsedMilliseconds;
                Log.Information($@"{Path.GetFileName(dlcDirectory)} - {dlcDirectory} Ran Unpacked TOC, took {elapsedMs}ms");
            }
            else
            {
                Log.Warning(@"Did not create TOC for " + dlcDirectory);
                watch.Stop();
            }
        }

        /// <summary>
        /// Gets the sizes of each SFAR in an unmodified state.
        /// This does not account for 1.6 TESTPATCH.
        /// </summary>
        /// <returns></returns>
        //static Dictionary<string, long> GetSFARSizeMap()
        //{
        //    if (sfarSizeMap == null)
        //    {
        //        sfarSizeMap = new Dictionary<string, long>();
        //        sfarSizeMap[@"DLC_CON_MP1"] = 220174473L;
        //        sfarSizeMap[@"DLC_CON_MP2"] = 139851674L;
        //        sfarSizeMap[@"DLC_CON_MP3"] = 198668075L;
        //        sfarSizeMap[@"DLC_CON_MP4"] = 441856666L;
        //        sfarSizeMap[@"DLC_CON_MP5"] = 208777784L;

        //        sfarSizeMap[@"DLC_UPD_Patch01"] = 208998L;
        //        sfarSizeMap[@"DLC_UPD_Patch02"] = 302772L;
        //        sfarSizeMap[@"DLC_TestPatch"] = 2455154L; //1.6 also has a version

        //        sfarSizeMap[@"DLC_HEN_PR"] = 594778936L;
        //        sfarSizeMap[@"DLC_CON_END"] = 1919137514L;

        //        sfarSizeMap[@"DLC_EXP_Pack001"] = 1561239503L;
        //        sfarSizeMap[@"DLC_EXP_Pack002"] = 1849136836L;
        //        sfarSizeMap[@"DLC_EXP_Pack003"] = 1886013531L;
        //        sfarSizeMap[@"DLC_EXP_Pack003_Base"] = 1896814656L;
        //        sfarSizeMap[@"DLC_CON_APP01"] = 53878606L;
        //        sfarSizeMap[@"DLC_CON_GUN01"] = 18708500L;
        //        sfarSizeMap[@"DLC_CON_GUN02"] = 17134896L;
        //        sfarSizeMap[@"DLC_CON_DH1"] = 284862077L;
        //        sfarSizeMap[@"DLC_OnlinePassHidCE"] = 56321927L;
        //    }
        //    return sfarSizeMap;
        //}

        public override void HandleKeyPress(object sender, KeyEventArgs e)
        {
            //autocloses
        }

        public override void OnPanelVisible()
        {
            NamedBackgroundWorker bw = new NamedBackgroundWorker(@"AutoTOC");
            bw.DoWork += (a, b) =>
            {
                if (mode == AutoTOCMode.MODE_GAMEWIDE)
                {
                    RunGameWideAutoTOC();
                }
            };
            bw.RunWorkerCompleted += (a, b) =>
            {
                OnClosing(DataEventArgs.Empty);
            };
            bw.RunWorkerAsync();
        }
    }
}
