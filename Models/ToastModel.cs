using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;

namespace Moonrise.Models
{
    public partial class ToastModel : ObservableObject
    {
        public Guid Id { get; } = Guid.NewGuid();

        [ObservableProperty]
        public partial string Title { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string Message { get; set; } = string.Empty;

        [ObservableProperty]
        public partial InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;

        [ObservableProperty]
        public partial bool IsOpen { get; set; } = true;

        [ObservableProperty]
        public partial bool IsClosable { get; set; } = true;

        [ObservableProperty]
        public partial bool ShowProgressBar { get; set; } = false;

        [ObservableProperty]
        public partial bool IsIndeterminate { get; set; } = true;

        [ObservableProperty]
        public partial double Progress { get; set; } = 0.0;

        [ObservableProperty]
        public partial double ProgressBarOpacity { get; set; } = 0.0;

        public Visibility ProgressBarVisibility => ShowProgressBar ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CloseButtonVisibility => IsClosable ? Visibility.Visible : Visibility.Collapsed;

        public string SeverityGlyph => Severity switch
        {
            InfoBarSeverity.Success => "\uE73E",
            InfoBarSeverity.Warning => "\uE7BA",
            InfoBarSeverity.Error => "\uEA39",
            _ => "\uE895"
        };

        public Brush SeverityBrush
        {
            get
            {
                string key = Severity switch
                {
                    InfoBarSeverity.Success => "SystemFillColorSuccessBrush",
                    InfoBarSeverity.Warning => "SystemFillColorCautionBrush",
                    InfoBarSeverity.Error => "SystemFillColorCriticalBrush",
                    _ => "AccentFillColorDefaultBrush"
                };
                if (Application.Current.Resources.TryGetValue(key, out var res) && res is Brush brush)
                {
                    return brush;
                }
                return (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            }
        }

        partial void OnShowProgressBarChanged(bool value)
        {
            ProgressBarOpacity = value ? 1.0 : 0.0;
            OnPropertyChanged(nameof(ProgressBarVisibility));
        }

        partial void OnIsClosableChanged(bool value)
        {
            OnPropertyChanged(nameof(CloseButtonVisibility));
        }

        partial void OnSeverityChanged(InfoBarSeverity value)
        {
            OnPropertyChanged(nameof(SeverityGlyph));
            OnPropertyChanged(nameof(SeverityBrush));
        }
    }
}
