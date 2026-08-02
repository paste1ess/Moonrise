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

            services.AddSingleton<IArtService, ArtService>();
            services.AddSingleton<IDiscordRpcService>(DiscordRpcService.Instance);

            TaskService.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            _window = new MainWindow();
            SettingsService.Instance.Load();

            Services = services.BuildServiceProvider();
            _ = Services.GetRequiredService<IArtService>();
            _ = Services.GetRequiredService<IDiscordRpcService>();

            _window.Activate();
        }
    }
}
