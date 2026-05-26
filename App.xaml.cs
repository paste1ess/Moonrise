using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Win32;
using Moonrise.Services;
using System.Runtime.InteropServices;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private PlaybackService playbackService;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            System.AppDomain.CurrentDomain.FirstChanceException += (sender, eventArgs) =>
            {
                try
                {
                    var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[{System.DateTime.Now:O}] {eventArgs.Exception.GetType().FullName}: {eventArgs.Exception.Message}");
                    sb.AppendLine(eventArgs.Exception.StackTrace);
                    var inner = eventArgs.Exception.InnerException;
                    while (inner != null)
                    {
                        sb.AppendLine($"\n---> Inner Exception: {inner.GetType().FullName}: {inner.Message}");
                        sb.AppendLine(inner.StackTrace);
                        inner = inner.InnerException;
                    }
                    sb.AppendLine("\n========================================\n");
                    System.IO.File.AppendAllText(logPath, sb.ToString());
                }
                catch
                {
                }
            };
        }


        protected async override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            
            TaskService.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            _window = new MainWindow();
            SettingsService.Instance.Load();
            _ = DiscordRpcService.Instance;

            _window.Activate();
        }
    }
}
