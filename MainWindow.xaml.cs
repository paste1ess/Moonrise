using ComputeSharp.D2D1.WinUI;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Moonrise.Pages;
using Moonrise.Shaders;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Storage;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise
{
    public sealed partial class MainWindow : Window
    {
        private PixelShaderEffect<BackgroundShader> _shaderEffect;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint LoadImage(
        nint hInst, string lpszName, uint uType,
        int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint SetClassLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern nint GetModuleHandle(string? lpModuleName);

        private const int GCL_HICON = -14;
        private const int GCL_HICONSM = -34;
        private const uint IMAGE_ICON = 1;
        private const uint LR_DEFAULTSIZE = 0x00000040;
        private const uint LR_SHARED = 0x00008000;
        public MainWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            AppWindow.SetIcon("Assets/AppIcon.ico");
            SetTaskManagerIcon();
        }
        private void SetTaskManagerIcon()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var hInstance = GetModuleHandle(null);

            // large icon
            var hIconLarge = LoadImage(hInstance, "#32512", IMAGE_ICON, 32, 32, LR_SHARED);

            // small icon
            var hIconSmall = LoadImage(
                hInstance,
                "AppIcon",
                IMAGE_ICON,
                16, 16,
                LR_SHARED);

            if (hIconLarge != nint.Zero)
                SetClassLongPtr(hwnd, GCL_HICON, hIconLarge);

            if (hIconSmall != nint.Zero)
                SetClassLongPtr(hwnd, GCL_HICONSM, hIconSmall);
        }

        //private void ShaderCanvas_CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
        //{
        //    _shaderEffect = new PixelShaderEffect<BackgroundShader>();
        //}

        //private void ShaderCanvas_Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
        //{
        //    _shaderEffect.ConstantBuffer = new BackgroundShader((float)args.Timing.TotalTime.TotalSeconds);
        //}

        //private void ShaderCanvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        //{
        //    args.DrawingSession.DrawImage(_shaderEffect);
        //}

        private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                NavFrame.Navigate(typeof(SettingsPage));
            }
            else if (args.SelectedItem is NavigationViewItem item)
            {
                switch (item.Tag)
                {
                    case "home":
                        NavFrame.Navigate(typeof(HomePage));
                        break;
                    case "tracks":
                        NavFrame.Navigate(typeof(TracksPage));
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
                }
            }
            if (args.SelectedItemContainer != null)
            {
                PlayerNavIndicator.Visibility = Visibility.Collapsed;
                PlayerNavButton.ClearValue(Button.BackgroundProperty);
            }
        }

        private void PlayerNavButton_Click(object sender, RoutedEventArgs e)
        {
            if (NavView.SelectedItem != null)
            {
                NavView.SelectedItem = null;
                PlayerNavIndicator.Visibility = Visibility.Visible;
                PlayerNavButton.Background = (Brush)Application.Current.Resources["NavigationViewItemBackgroundSelected"];
                NavFrame.Navigate(typeof(PlayerPage));
            }
        }
    }
}
