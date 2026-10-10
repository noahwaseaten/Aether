using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Xaml;
using SmartHunter.Core;
using SmartHunter.Core.Helpers;
using SmartHunter.Game;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Windows;
using XamlReader = System.Windows.Markup.XamlReader;

namespace SmartHunter
{
    public partial class App : Application
    {
        MhwOverlay m_Overlay;
        bool m_Started;
        FileContainer m_SkinFile;

        string m_LastSkinFileName;

        // Two copies would draw every widget twice and double the sync traffic
        static System.Threading.Mutex s_SingleInstance;
        public const string SingleInstanceName = "Aether-MHW-Overlay";

        // Started by "Open with the game": closes with the game, then waits for the next one
        public static bool OpenedForGame { get; private set; }

        public static void ReleaseSingleInstance()
        {
            s_SingleInstance?.ReleaseMutex();
            s_SingleInstance?.Dispose();
            s_SingleInstance = null;
        }

        public App()
        {
            // Startup crashes happen before the log exists; leave a trace next to the exe
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                File.AppendAllText(Path.Combine(FileContainer.GetFullPath(), "Crash.txt"), $"[{DateTime.Now}] {e.ExceptionObject}\r\n\r\n");
            // An error on the UI thread used to close Aether mid-hunt. Keep running, and say so in the window.
            DispatcherUnhandledException += (s, e) =>
            {
                Log.WriteException(e.Exception);
                Problems.Report("ui", "Something went wrong in Aether, but it kept running. If a widget looks wrong, restart Aether.");
                e.Handled = true;
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            //var culture = new System.Globalization.CultureInfo("es-ES");
            //System.Globalization.CultureInfo.CurrentCulture = culture;
            //System.Globalization.CultureInfo.CurrentUICulture = culture;

            if (Array.IndexOf(e.Args, "--selftest") >= 0)
            {
                Environment.Exit(SelfTest.Run());
            }

            // Settings > Apps > Uninstall runs the installed exe with this
            if (Array.IndexOf(e.Args, "--uninstall") >= 0)
            {
                Installer.Uninstall();
                Shutdown();
                return;
            }

            // Started from Downloads or anywhere else: install (or update) the copy in %LocalAppData%\Aether and start that
            if (Installer.InstallAndHandOver(e.Args))
            {
                Shutdown();
                return;
            }

            if (Array.IndexOf(e.Args, "--wait") >= 0)
            {
                if (!AutoStart.WaitForGame(Array.IndexOf(e.Args, "--skip-running") >= 0))
                {
                    Shutdown();
                    return;
                }
                OpenedForGame = true;
            }

            var mutex = new System.Threading.Mutex(false, SingleInstanceName);
            bool owned;
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(5)); } // a restarting copy may still be closing
            catch (System.Threading.AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                MessageBox.Show("Aether is already running.", "Aether");
                Shutdown();
                return;
            }
            s_SingleInstance = mutex;
            m_Started = true;

            // Initialize the console view model first thing so we can see any problems that may occur
            var consoleViewModel = ConsoleViewModel.Instance;

            Log.WriteLine($"Started {Assembly.GetExecutingAssembly().GetName().Version}");
            Installer.Register();
            //Log.WriteLine($"Culture: {System.Globalization.CultureInfo.CurrentCulture.Name}");

            SetPerMonitorDpiAwareness();

            // Stay out of the game's way: lower CPU priority, and cap animations at 30 fps. Transparent overlay
            // windows are re-composited on the CPU every animation frame, so 60 fps pulses cost real time.
            try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal; } catch { }
            System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(System.Windows.Media.Animation.Timeline), new FrameworkPropertyMetadata { DefaultValue = 30 });

            ConfigHelper.EnsureConfigs();
            if (ConfigHelper.Main.Values.UseSoftwareRendering)
            {
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            }
            ConfigHelper.Main.Loaded += Config_Loaded;

            m_SkinFile = new FileContainer(ConfigHelper.Main.Values.SkinFileName);
            m_SkinFile.Changed += (s1, e1) => { LoadSkin(); };
            LoadSkin();

            UpdateViewModel.Instance.Start();

            m_Overlay = new MhwOverlay(new ConsoleWindow(), new TeamWidgetWindow(), new MonsterWidgetWindow(), new PlayerWidgetWindow(), new DebugWidgetWindow(), new CalloutWidgetWindow(), new RecapWidgetWindow());

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (!m_Started)
            {
                base.OnExit(e); // a waiter that stopped, or a second copy: nothing was logged, so don't log the end
                return;
            }
            Log.WriteLine("Ended");
            if (OpenedForGame && AutoStart.IsEnabled)
            {
                AutoStart.StartWaiter();
            }
            base.OnExit(e);
        }

        private void Config_Loaded(object sender, EventArgs e)
        {
            if (m_LastSkinFileName != ConfigHelper.Main.Values.SkinFileName)
            {
                LoadSkin();
            }
        }

        void LoadSkin()
        {
            if (ConfigHelper.Main.Values.Debug.UseInternalSkin)
            {
                return;
            }

            var skinFileName = ConfigHelper.Main.Values.SkinFileName;
            // No skin file next to the exe is the normal case: the built-in look is already loaded
            if (!File.Exists(FileContainer.GetFullPathFileName(skinFileName)))
            {
                return;
            }

            m_SkinFile.TryChangeFileName(skinFileName);

            try
            {
                ResourceDictionary resourceDictionary = null;

                using (var streamReader = new StreamReader(m_SkinFile.FullPathFileName, Encoding.UTF8))
                {
                    LoadExternalAssemblies();
                    var xmlReaderSettings = new XamlXmlReaderSettings
                    {
                        LocalAssembly = Assembly.GetExecutingAssembly()
                    };

                    using (var xamlReader = new XamlXmlReader(streamReader.BaseStream, XamlReader.GetWpfSchemaContext(), xmlReaderSettings))
                    {
                        resourceDictionary = XamlReader.Load(xamlReader) as ResourceDictionary;
                    }
                }

                if (resourceDictionary != null)
                {
                    Resources.MergedDictionaries.Clear();
                    Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Ui/Resources/Icons.xaml") });
                    Resources.MergedDictionaries.Add(resourceDictionary);

                    if (m_Overlay != null)
                    {
                        m_Overlay.RefreshWidgetsLayout();
                    }

                    Log.WriteLine($"{m_SkinFile.FileName} loaded");
                }
            }
            catch (Exception ex)
            {
                Log.WriteException(ex);
            }

            m_LastSkinFileName = skinFileName;
        }

        void LoadExternalAssemblies()
        {
            // External dependencies should be loaded into AppDomain before they can be used in dynamically loaded XAML (Default.xaml)
            Assembly.Load(typeof(OxyPlot.Wpf.Plot).Assembly.GetName());
            Assembly.Load(typeof(Microsoft.Xaml.Behaviors.Behavior).Assembly.GetName());
        }

        void SetPerMonitorDpiAwareness()
        {
            // Win 8.1 added support for per monitor dpi
            if (Environment.OSVersion.Version >= new Version(6, 3, 0))
            {
                // Win 10 creators update added support for per monitor v2
                if (Environment.OSVersion.Version >= new Version(10, 0, 15063))
                {
                    WindowsApi.SetProcessDpiAwarenessContext((int)WindowsApi.DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                }
                else
                {
                    WindowsApi.SetProcessDpiAwareness(WindowsApi.PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE);
                }
            }
            else
            {
                WindowsApi.SetProcessDPIAware();
            }
        }
    }
}
