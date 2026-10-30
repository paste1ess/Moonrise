using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;

namespace Moonrise.Controls
{
    public sealed partial class LyricItem : UserControl
    {
        private const float MaxBlurAmount = 4f;
        private const float FutureOpacity = 0.4f;
        private const float HiddenSourceOpacity = 0.004f;

        private Compositor? _compositor;
        private Visual? _sourceWrapperVisual;
        private Visual? _textSourceVisual;
        private CompositionVisualSurface? _visualSurface;
        private CompositionSurfaceBrush? _surfaceBrush;
        private CompositionEffectBrush? _effectBrush;
        private SpriteVisual? _spriteVisual;

        private bool _isLoaded;
        private bool _updateQueued;
        private bool _snapNextUpdate = true;
        private bool _wasActive;
        private bool _wasPassed;
        private int _stateVersion;

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(LyricItem), new PropertyMetadata("", OnTextChanged));

        public bool Active
        {
            get => (bool)GetValue(ActiveProperty);
            set => SetValue(ActiveProperty, value);
        }

        public static readonly DependencyProperty ActiveProperty =
            DependencyProperty.Register(nameof(Active), typeof(bool), typeof(LyricItem), new PropertyMetadata(false, OnStatePropertyChanged));

        public bool Passed
        {
            get => (bool)GetValue(PassedProperty);
            set => SetValue(PassedProperty, value);
        }

        public static readonly DependencyProperty PassedProperty =
            DependencyProperty.Register(nameof(Passed), typeof(bool), typeof(LyricItem), new PropertyMetadata(false, OnStatePropertyChanged));

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LyricItem)d;
            control._snapNextUpdate = true;
            control.QueueStateUpdate();
        }

        private static void OnStatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LyricItem)d;
            control.QueueStateUpdate();
        }

        public LyricItem()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            EnsureComposition();
            UpdateVisualState(animate: false);
            _snapNextUpdate = false;
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
        }

        private void TextSourceHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSurfaceSize();
        }

        private void EnsureComposition()
        {
            if (_compositor != null) return;

            _sourceWrapperVisual = ElementCompositionPreview.GetElementVisual(SourceWrapper);
            _textSourceVisual = ElementCompositionPreview.GetElementVisual(TextSourceHost);
            _compositor = _textSourceVisual.Compositor;

            _visualSurface = _compositor.CreateVisualSurface();
            _visualSurface.SourceOffset = Vector2.Zero;

            _surfaceBrush = _compositor.CreateSurfaceBrush(_visualSurface);
            _surfaceBrush.Stretch = CompositionStretch.None;
            _surfaceBrush.HorizontalAlignmentRatio = 0f;
            _surfaceBrush.VerticalAlignmentRatio = 0f;

            var blurEffect = new GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = 0f,
                BorderMode = EffectBorderMode.Soft,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter("source")
            };

            var factory = _compositor.CreateEffectFactory(blurEffect, new[] { "Blur.BlurAmount" });
            _effectBrush = factory.CreateBrush();
            _effectBrush.SetSourceParameter("source", _surfaceBrush);

            _spriteVisual = _compositor.CreateSpriteVisual();
            _spriteVisual.Brush = _effectBrush;
            _spriteVisual.Opacity = 0f;

            ElementCompositionPreview.SetElementChildVisual(BlurHost, _spriteVisual);
        }

        private void AttachSurface()
        {
            if (_visualSurface == null || _textSourceVisual == null) return;
            if (_visualSurface.SourceVisual != _textSourceVisual)
            {
                _visualSurface.SourceVisual = _textSourceVisual;
            }
            UpdateSurfaceSize();
        }

        private void DetachSurface()
        {
            if (_visualSurface == null) return;
            _visualSurface.SourceVisual = null;
            _visualSurface.SourceSize = Vector2.Zero;
        }

        private void UpdateSurfaceSize()
        {
            if (_visualSurface == null || _spriteVisual == null || _visualSurface.SourceVisual == null) return;

            float width = (float)TextSourceHost.ActualWidth;
            float height = (float)TextSourceHost.ActualHeight;
            if (width <= 0 || height <= 0) return;

            var size = new Vector2(width, height);
            _visualSurface.SourceSize = size;
            _spriteVisual.Size = size;
        }

        private void QueueStateUpdate()
        {
            if (!_isLoaded)
            {
                return;
            }

            bool isActive = Active;
            bool isPassed = !isActive && Passed;
            if (!_snapNextUpdate && isActive == _wasActive && isPassed == _wasPassed)
            {
                return;
            }

            bool animate = !_snapNextUpdate;
            _snapNextUpdate = false;
            UpdateVisualState(animate);
        }

        private void UpdateVisualState(bool animate)
        {
            EnsureComposition();
            if (_compositor == null || _sourceWrapperVisual == null || _spriteVisual == null || _effectBrush == null)
                return;

            bool isActive = Active;
            bool isPassed = !isActive && Passed;
            int version = ++_stateVersion;

            if (!animate)
            {
                ActiveStoryboard.Stop();
                InactiveStoryboard.Stop();
                LyricScale.ScaleX = isActive ? 1.25 : 1.0;
                LyricScale.ScaleY = isActive ? 1.25 : 1.0;

                _sourceWrapperVisual.StopAnimation("Opacity");
                _spriteVisual.StopAnimation("Opacity");
                _effectBrush.Properties.StopAnimation("Blur.BlurAmount");

                if (isActive)
                {
                    AttachSurface();
                    _sourceWrapperVisual.Opacity = 1.0f;
                    _spriteVisual.Opacity = 0f;
                    _effectBrush.Properties.InsertScalar("Blur.BlurAmount", 0f);
                }
                else if (isPassed)
                {
                    DetachSurface();
                    _sourceWrapperVisual.Opacity = 0f;
                    _spriteVisual.Opacity = 0f;
                    _effectBrush.Properties.InsertScalar("Blur.BlurAmount", MaxBlurAmount);
                }
                else
                {
                    DetachSurface();
                    _sourceWrapperVisual.Opacity = FutureOpacity;
                    _spriteVisual.Opacity = 0f;
                    _effectBrush.Properties.InsertScalar("Blur.BlurAmount", 0f);
                }

                _wasActive = isActive;
                _wasPassed = isPassed;
                return;
            }

            if (isActive)
            {
                ActiveStoryboard.Begin();
            }
            else
            {
                InactiveStoryboard.Begin();
            }

            if (isPassed)
            {
                AttachSurface();
                float startOpacity = _wasActive ? 1.0f : (_wasPassed ? 0f : FutureOpacity);

                _sourceWrapperVisual.StopAnimation("Opacity");
                _sourceWrapperVisual.Opacity = HiddenSourceOpacity;

                _spriteVisual.Opacity = startOpacity;
                _effectBrush.Properties.InsertScalar("Blur.BlurAmount", 0f);

                var duration = TimeSpan.FromMilliseconds(450);
                var blurEase = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
                var fadeEase = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.3f, 0f), new Vector2(0.2f, 1f));

                var blurAnim = _compositor.CreateScalarKeyFrameAnimation();
                blurAnim.InsertKeyFrame(0f, 0f);
                blurAnim.InsertKeyFrame(1f, MaxBlurAmount, blurEase);
                blurAnim.Duration = duration;

                var opacityAnim = _compositor.CreateScalarKeyFrameAnimation();
                opacityAnim.InsertKeyFrame(0f, startOpacity);
                opacityAnim.InsertKeyFrame(1f, 0f, fadeEase);
                opacityAnim.Duration = duration;

                var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                _effectBrush.Properties.StartAnimation("Blur.BlurAmount", blurAnim);
                _spriteVisual.StartAnimation("Opacity", opacityAnim);
                batch.End();
                batch.Completed += (_, _) =>
                {
                    if (_stateVersion == version && Passed && !Active && _sourceWrapperVisual != null)
                    {
                        _sourceWrapperVisual.Opacity = 0f;
                        DetachSurface();
                    }
                };
            }
            else
            {
                float targetOpacity = isActive ? 1.0f : FutureOpacity;
                var duration = TimeSpan.FromMilliseconds(350);
                var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

                if (isActive)
                {
                    AttachSurface();
                }

                if (_wasPassed)
                {
                    AttachSurface();
                    _sourceWrapperVisual.StopAnimation("Opacity");
                    _sourceWrapperVisual.Opacity = HiddenSourceOpacity;

                    var blurAnim = _compositor.CreateScalarKeyFrameAnimation();
                    blurAnim.InsertKeyFrame(1f, 0f, ease);
                    blurAnim.Duration = duration;

                    var opacityAnim = _compositor.CreateScalarKeyFrameAnimation();
                    opacityAnim.InsertKeyFrame(1f, targetOpacity, ease);
                    opacityAnim.Duration = duration;

                    var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                    _effectBrush.Properties.StartAnimation("Blur.BlurAmount", blurAnim);
                    _spriteVisual.StartAnimation("Opacity", opacityAnim);
                    batch.End();
                    batch.Completed += (_, _) =>
                    {
                        if (_stateVersion == version && !Passed && _sourceWrapperVisual != null && _spriteVisual != null)
                        {
                            _sourceWrapperVisual.Opacity = Active ? 1.0f : FutureOpacity;
                            _spriteVisual.Opacity = 0f;
                            if (!Active)
                            {
                                DetachSurface();
                            }
                        }
                    };
                }
                else
                {
                    _spriteVisual.StopAnimation("Opacity");
                    _effectBrush.Properties.StopAnimation("Blur.BlurAmount");
                    _spriteVisual.Opacity = 0f;
                    _effectBrush.Properties.InsertScalar("Blur.BlurAmount", 0f);

                    var opacityAnim = _compositor.CreateScalarKeyFrameAnimation();
                    opacityAnim.InsertKeyFrame(1f, targetOpacity, ease);
                    opacityAnim.Duration = duration;

                    _sourceWrapperVisual.StartAnimation("Opacity", opacityAnim);
                    if (!isActive)
                    {
                        DetachSurface();
                    }
                }
            }

            _wasActive = isActive;
            _wasPassed = isPassed;
        }
    }
}
