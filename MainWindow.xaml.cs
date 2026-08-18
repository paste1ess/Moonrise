using ComputeSharp.D2D1.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Moonrise.Models;
using Moonrise.Pages;
using Moonrise.Services;
using Moonrise.Shaders;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.System;
using WinRT.Interop;

namespace Moonrise
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }
        private readonly ISettingsService _settings = App.Services.GetRequiredService<ISettingsService>();
        private PixelShaderEffect<BackgroundShader>? _shaderEffect;
        private CancellationTokenSource? _shaderFadeCts;
        private bool _isLightTheme;
        private bool _isWindowFocused;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private float _shaderTime = 0f;
        private DateTime _lastTick = DateTime.UtcNow;
        private readonly DispatcherTimer _shaderTimer = new();
        private CanvasRenderTarget? _offscreen;
        private float _lastWidth, _lastHeight;
        private float _shaderSpeedMultiplier = 80f / 120f;
        private readonly IAudioPeakService _peakService = App.Services.GetRequiredService<IAudioPeakService>();
        private volatile SoftwareBitmap? _currentBackgroundSoftwareBitmap;
        private SoftwareBitmap? _lastRenderedBitmap;
        private CanvasBitmap? _backgroundCanvasBitmap;

        public PlaybackService PlaybackService => PlaybackService.Instance;
        public IToastService ToastService => App.Services.GetRequiredService<IToastService>();


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
            Instance = this;
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
            SetTaskManagerIcon();

            Activated += MainWindow_Activated;
            Closed += (s, e) =>
            {
                _shaderFadeCts?.Cancel();
                _shaderFadeCts?.Dispose();
                _shaderFadeCts = null;

                _peakService.Dispose();

                BackgroundCanvas.Draw -= BackgroundCanvas_Draw;
                _backgroundCanvasBitmap?.Dispose();
                _backgroundCanvasBitmap = null;
                _lastRenderedBitmap = null;
                _currentBackgroundSoftwareBitmap = null;

                _shaderTimer.Stop();
                ShaderCanvas.Draw -= ShaderCanvas_Draw;
                _offscreen?.Dispose();
                _offscreen = null;
                _shaderEffect = null;
            };

            _isLightTheme = Application.Current.RequestedTheme == ApplicationTheme.Light;
            ((FrameworkElement)Content).ActualThemeChanged += (s, _) =>
            {
                _isLightTheme = s.ActualTheme == ElementTheme.Light;
                UpdateBackgroundCanvas();
            };

            _shaderTimer.Interval = _settings.BackgroundShadersBoostFps
                    ? TimeSpan.FromSeconds(1.0 / 60.0)
                    : TimeSpan.FromSeconds(1.0 / 12.0);
            _shaderTimer.Tick += (_, _) =>
            {
                var now = DateTime.UtcNow;
                float delta = (float)(now - _lastTick).TotalSeconds;
                _lastTick = now;
                float peak = _peakService.GetVolumePeak();
                float dynamicSpeed = (_shaderSpeedMultiplier * 0.7f) + (peak * 1.35f);
                _shaderTime += delta * (_isWindowFocused ? 1f : 1f / 18f) * dynamicSpeed;
                ShaderCanvas.Invalidate();
            };

            if (_settings.BackgroundShadersEnabled)
            {
                _shaderEffect = new PixelShaderEffect<BackgroundShader>();
                ShaderCanvas.Draw += ShaderCanvas_Draw;
                ShaderCanvas.Opacity = 1;
                ShaderCanvas.Visibility = Visibility.Visible;
                _shaderTimer.Start();
            }
            else
            {
                ShaderCanvas.Opacity = 0;
                ShaderCanvas.Visibility = Visibility.Collapsed;
            }

            BackgroundCanvas.Draw += BackgroundCanvas_Draw;
            BackgroundCanvas.CreateResources += (_, _) =>
            {
                _backgroundCanvasBitmap?.Dispose();
                _backgroundCanvasBitmap = null;
                _lastRenderedBitmap = null;
                BackgroundCanvas.Invalidate();
            };

            PlaybackService.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PlaybackService.CurrentTrackBackgroundBitmap))
                {
                    _currentBackgroundSoftwareBitmap = PlaybackService.Instance.CurrentTrackBackgroundBitmap;
                    DispatcherQueue.TryEnqueue(UpdateBackgroundCanvas);
                }
                if (e.PropertyName == nameof(PlaybackService.CurrentTrack) ||
                    e.PropertyName == nameof(PlaybackService.CurrentPlaybackState))
                {
                    DispatcherQueue.TryEnqueue(UpdateShaderSpeedMultiplier);
                    DispatcherQueue.TryEnqueue(UpdateBackgroundCanvas);
                }
            };

            UpdateShaderSpeedMultiplier();

            _settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ISettingsService.BackgroundShadersEnabled))
                {
                    _shaderFadeCts?.Cancel();
                    _shaderFadeCts?.Dispose();

                    if (_settings.BackgroundShadersEnabled)
                    {
                        _shaderFadeCts = null;
                        ShaderCanvas.Visibility = Visibility.Visible;
                        if (_shaderEffect == null)
                        {
                            _shaderEffect = new PixelShaderEffect<BackgroundShader>();
                            ShaderCanvas.Draw += ShaderCanvas_Draw;
                            _lastTick = DateTime.UtcNow;
                            _shaderTimer.Start();
                        }
                        ShaderCanvas.Opacity = 1;
                    }
                    else
                    {
                        _shaderFadeCts = new CancellationTokenSource();
                        ShaderCanvas.Opacity = 0;
                        _ = StopShaderAfterFadeAsync(_shaderFadeCts.Token);
                    }

                    UpdateBackgroundCanvas();
                }
                else if (e.PropertyName == nameof(ISettingsService.BackgroundShadersBoostFps))
                {
                    if (_isWindowFocused)
                        _shaderTimer.Interval = _settings.BackgroundShadersBoostFps
                            ? TimeSpan.FromSeconds(1.0 / 60.0)
                            : TimeSpan.FromSeconds(1.0 / 12.0);
                }
            };

            NavView.SelectedItem = NavView.MenuItems[0];
        }

        private void UpdateShaderSpeedMultiplier()
        {
            var state = PlaybackService.Instance.CurrentPlaybackState;
            var track = PlaybackService.Instance.CurrentTrack;

            if (state != PlaybackState.Playing || track == null)
            {
                _shaderSpeedMultiplier = (80f / 100f) * 1.2f;
            }
            else if (track.Bpm.HasValue && track.Bpm.Value > 0)
            {
                _shaderSpeedMultiplier = (track.Bpm.Value / 100f) * 1.2f;
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
                _shaderTimer.Stop();
                _isWindowFocused = false;
            }
            else
            {
                _isWindowFocused = true;

                if (_settings.BackgroundShadersEnabled)
                {
                    _shaderTimer.Interval = _settings.BackgroundShadersBoostFps
                        ? TimeSpan.FromSeconds(1.0 / 60.0)
                        : TimeSpan.FromSeconds(1.0 / 12.0);

                    _lastTick = DateTime.UtcNow;
                    _shaderTimer.Start();
                }
            }
            UpdateBackgroundCanvas();
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

        private void BackgroundCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_backgroundCanvasBitmap is null && _currentBackgroundSoftwareBitmap is null) return;

            try
            {
                var bitmap = _currentBackgroundSoftwareBitmap;
                if (bitmap != _lastRenderedBitmap)
                {
                    _backgroundCanvasBitmap?.Dispose();
                    _backgroundCanvasBitmap = bitmap != null
                        ? CanvasBitmap.CreateFromSoftwareBitmap(sender, bitmap)
                        : null;
                    _lastRenderedBitmap = bitmap;
                }
                if (_backgroundCanvasBitmap != null)
                {
                    args.DrawingSession.DrawImage(
                        _backgroundCanvasBitmap,
                        new Rect(0, 0, sender.ActualWidth, sender.ActualHeight),
                        _backgroundCanvasBitmap.Bounds,
                        1.0f,
                        CanvasImageInterpolation.Cubic);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error drawing background canvas: {ex.Message}");
            }
        }

        private void UpdateBackgroundCanvas()
        {
            try
            {
                BackgroundCanvas.Opacity = ComputeBackgroundOpacity();
                BackgroundCanvas.Invalidate();
            }
            catch
            {

            }
        }

        private double ComputeBackgroundOpacity()
        {
            if (!_settings.BackgroundShadersEnabled ||
                !_isWindowFocused ||
                PlaybackService.Instance.CurrentPlaybackState != PlaybackState.Playing ||
                _currentBackgroundSoftwareBitmap == null)
                return 0.0;
            return _isLightTheme ? 0.25 : 0.45;
        }

        private void ShaderCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_shaderEffect is null) return;

            float centerX = (float)(_lastWidth * 0.5);
            float centerY = (float)(_lastHeight * 0.5);
            float2 currentCenter = new float2(centerX, centerY);


            _shaderEffect.ConstantBuffer = new BackgroundShader(
                _shaderTime,
                _isLightTheme,
                currentCenter
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

        private async Task StopShaderAfterFadeAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(350, token);
                if (token.IsCancellationRequested) return;

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_settings.BackgroundShadersEnabled)
                    {
                        ShaderCanvas.Visibility = Visibility.Collapsed;
                        _shaderTimer.Stop();
                        ShaderCanvas.Draw -= ShaderCanvas_Draw;
                        _offscreen?.Dispose();
                        _offscreen = null;
                        _shaderEffect = null;
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        public string PlaybackStateToGlyph(PlaybackState state)
    => state == PlaybackState.Playing ? "\uE769" : "\uE768";

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
                        NavFrame.Navigate(typeof(AlbumsPage));
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

        public void NavigateToPlayer()
        {
            if (NavView.SelectedItem != null)
            {
                NavView.SelectedItem = null;
                PlayerNavIndicator.Visibility = Visibility.Visible;
                PlayerNavButton.Background = (Brush)Application.Current.Resources["NavigationViewItemBackgroundSelected"];
                NavFrame.Navigate(typeof(PlayerPage));
            }
        }

        private void PlayerNavButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToPlayer();
        }

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.F11)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }

        private void RewindButton_Click(object sender, RoutedEventArgs e)
        {
            PlaybackService.Back();
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaybackService.CurrentPlaybackState == PlaybackState.Paused)
                PlaybackService.Play();
            else if (PlaybackService.CurrentPlaybackState == PlaybackState.Playing)
                PlaybackService.Pause();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            PlaybackService.Next();
        }

        private void Toast_CloseButtonClick(InfoBar sender, object args)
        {
            if (sender.Tag is ToastModel toast)
            {
                ToastService.Dismiss(toast);
            }
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var width = e.NewSize.Width;
            var nowPlayingVisible = width >= 800 ? Visibility.Visible : Visibility.Collapsed;
            NowPlayingPanel.Visibility = nowPlayingVisible;
            NowPlayingSeparator.Visibility = nowPlayingVisible;
            PlaybackButtonsPanel.Visibility = width >= 600 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ToggleFullScreen()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                if (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                {
                    appWindow.SetPresenter(AppWindowPresenterKind.Default);
                }
                else
                {
                    appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                }
            }
        }
    }
}