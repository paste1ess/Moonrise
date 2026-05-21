using ComputeSharp.D2D1.WinUI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Moonrise.Pages;
using Moonrise.Services;
using Moonrise.Shaders;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Storage;
using WinRT.Interop;

namespace Moonrise
{
    public sealed partial class MainWindow : Window
    {
        private SettingsService _settings = SettingsService.Instance;
        private PixelShaderEffect<BackgroundShader>? _shaderEffect;
        private bool _isLightTheme;
        private bool _isWindowFocused;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private float _shaderTime = 0f;
        private DateTime _lastTick = DateTime.UtcNow;
        private readonly DispatcherTimer _shaderTimer = new();
        private CanvasRenderTarget? _offscreen;
        private float _lastWidth, _lastHeight;
        private float _shaderSpeedMultiplier = 80f / 120f;

        public PlaybackService PlaybackService => PlaybackService.Instance;
        public Visibility CheckBackgroundVisibility(PlaybackState state, BitmapImage artwork, bool isWindowFocused)
        {
            return (isWindowFocused && state == PlaybackState.Playing && artwork != null)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }


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

            Activated += MainWindow_Activated;

            _isLightTheme = Application.Current.RequestedTheme == ApplicationTheme.Light;
            ((FrameworkElement)Content).ActualThemeChanged += (s, _) => _isLightTheme = s.ActualTheme == ElementTheme.Light;

            _shaderTimer.Interval = SettingsService.Instance.BackgroundShadersBoostFps
                    ? TimeSpan.FromSeconds(1.0 / 60.0)
                    : TimeSpan.FromSeconds(1.0 / 12.0);
            _shaderTimer.Tick += (_, _) =>
            {
                var now = DateTime.UtcNow;
                float delta = (float)(now - _lastTick).TotalSeconds;
                _lastTick = now;
                _shaderTime += delta * (_isWindowFocused ? 1f : 1f / 18f) * _shaderSpeedMultiplier;
                ShaderCanvas.Invalidate();
            };

            if (SettingsService.Instance.BackgroundShadersEnabled)
            {
                _shaderEffect = new PixelShaderEffect<BackgroundShader>();
                ShaderCanvas.Draw += ShaderCanvas_Draw;
                _shaderTimer.Start();
            }

            PlaybackService.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PlaybackService.CurrentTrack) ||
                    e.PropertyName == nameof(PlaybackService.CurrentPlaybackState))
                {
                    DispatcherQueue.TryEnqueue(UpdateShaderSpeedMultiplier);
                }
            };

            UpdateShaderSpeedMultiplier();

            SettingsService.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SettingsService.BackgroundShadersEnabled))
                {
                    ShaderCanvas.Visibility = SettingsService.Instance.BackgroundShadersEnabled
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                    if (SettingsService.Instance.BackgroundShadersEnabled)
                    {
                        _shaderEffect = new PixelShaderEffect<BackgroundShader>();
                        ShaderCanvas.Draw += ShaderCanvas_Draw;
                        _lastTick = DateTime.UtcNow;
                        _shaderTimer.Start();
                    }
                    else
                    {
                        _shaderTimer.Stop();
                        ShaderCanvas.Draw -= ShaderCanvas_Draw;
                        _offscreen?.Dispose();
                        _offscreen = null;
                        _shaderEffect = null;
                    }
                }
                else if (e.PropertyName == nameof(SettingsService.BackgroundShadersBoostFps))
                {
                    if (_isWindowFocused)
                        _shaderTimer.Interval = SettingsService.Instance.BackgroundShadersBoostFps
                            ? TimeSpan.FromSeconds(1.0 / 60.0)
                            : TimeSpan.FromSeconds(1.0 / 12.0);
                }
            };

            NavFrame.Navigate(typeof(HomePage));
        }

        private void UpdateShaderSpeedMultiplier()
        {
            var state = PlaybackService.Instance.CurrentPlaybackState;
            var track = PlaybackService.Instance.CurrentTrack;

            if (state != PlaybackState.Playing || track == null)
            {
                _shaderSpeedMultiplier = 80f / 100f;
            }
            else if (track.Bpm.HasValue && track.Bpm.Value > 0)
            {
                _shaderSpeedMultiplier = track.Bpm.Value / 100f;
            }
            else
            {
                _shaderSpeedMultiplier = 1f;
            }
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                _shaderTimer.Interval = TimeSpan.FromSeconds(1.0 / 0.5);
                _isWindowFocused = false;
            }
            else
            {
                _shaderTimer.Interval = SettingsService.Instance.BackgroundShadersBoostFps
                    ? TimeSpan.FromSeconds(1.0 / 60.0)
                    : TimeSpan.FromSeconds(1.0 / 12.0);
                _isWindowFocused = true;
            }
            Bindings.Update();
        }

        private void SetTaskManagerIcon()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var hInstance = GetModuleHandle(null);

            var hIconLarge = LoadImage(hInstance, "#32512", IMAGE_ICON, 32, 32, LR_SHARED);

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

        private void ShaderCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_shaderEffect is null) return;


            _shaderEffect.ConstantBuffer = new BackgroundShader(
                _shaderTime,
                _isLightTheme
            );

            float renderScale = 0.1f;
            float width = (float)sender.ActualWidth * renderScale;
            float height = (float)sender.ActualHeight * renderScale;

            if (width < 1 || height < 1)
                return;

            if (_offscreen == null || _lastWidth != width || _lastHeight != height)
            {
                _offscreen?.Dispose();
                _offscreen = new CanvasRenderTarget(sender, width, height);
                _lastWidth = width;
                _lastHeight = height;
            }

            using (var ds = _offscreen.CreateDrawingSession())
            {
                ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                ds.DrawImage(_shaderEffect);
            }

            args.DrawingSession.DrawImage(
                _offscreen,
                new Rect(0, 0, sender.ActualWidth, sender.ActualHeight),
                _offscreen.Bounds,
                1.0f,
                CanvasImageInterpolation.Linear,
                CanvasComposite.Copy
            );
        }

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
                    case "albums":
                        NavFrame.Navigate(typeof(TracksPage));
                        break;
                    case "artists":
                        NavFrame.Navigate(typeof(TracksPage));
                        break;
                    case "favorites":
                        NavFrame.Navigate(typeof(TracksPage));
                        break;
                    case "playlists":
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