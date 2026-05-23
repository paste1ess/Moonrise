using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Services;

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
                if (eventArgs.Exception is System.OperationCanceledException || eventArgs.Exception is System.Threading.Tasks.TaskCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"[FirstChanceException] Canceled Exception: {eventArgs.Exception.Message}\nStack Trace:\n{eventArgs.Exception.StackTrace}\n");
                }
            };
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected async override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            TaskService.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            _window = new MainWindow();
            SettingsService.Instance.Load();

            //playbackService = PlaybackService.Instance;

            //await LibraryService.Instance.HardScanLibrary(@"M:\music\");

            _window.Activate();
        }
    }
}
