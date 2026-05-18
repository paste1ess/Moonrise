// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Controls;
using Moonrise.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private SettingsService _settings = SettingsService.Instance;
        public SettingsPage()
        {
            InitializeComponent();
        }
    }
}
