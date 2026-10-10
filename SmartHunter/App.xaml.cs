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
        FileContainer m_SkinFile;

        string m_LastSkinFileName;

        // Two copies would draw every widget twice and double the sync traffic
        static System.Threading.Mutex s_SingleInstance;

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
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Crash.txt"), $"[{DateTime.Now}] {e.ExceptionObject}\r\n\r\n");
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

            var mutex = new System.Threading.Mutex(false, "Aether-MHW-Overlay");
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

            // Initialize the console view model first thing so we can see any problems that may occur
            var consoleViewModel = ConsoleViewModel.Instance;

            Log.WriteLine($"Started {Assembly.GetExecutingAssembly().GetName().Version}");
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
            Log.WriteLine("Ended");
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
