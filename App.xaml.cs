using Microsoft.Extensions.DependencyInjection;
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
        public static IServiceProvider Services { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }


        protected async override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var services = new ServiceCollection();

            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IArtService, ArtService>();
            services.AddSingleton<IDiscordRpcService, DiscordRpcService>();
            services.AddSingleton<ITaskService>(_ => new TaskService(dispatcher));
            services.AddSingleton<IAudioPeakService, AudioPeakService>();
            services.AddSingleton<ILibraryService, LibraryService>();

            Services = services.BuildServiceProvider();

            var settings = Services.GetRequiredService<ISettingsService>();
            settings.Load();

            _ = Services.GetRequiredService<ITaskService>();
            _ = Services.GetRequiredService<IDiscordRpcService>();

            _window = new MainWindow();

            var library = Services.GetRequiredService<ILibraryService>();
            library.Initialize();

            _window.Activate();
        }
    }
}
