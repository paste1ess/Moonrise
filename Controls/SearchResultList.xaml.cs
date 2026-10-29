using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Moonrise.Services;
using System;
using System.Collections.Generic;

namespace Moonrise.Controls
{
    public sealed partial class SearchResultList : UserControl
    {
        public event EventHandler<SearchResult>? ResultClicked;

        public List<SearchResult> Results
        {
            get => (List<SearchResult>)GetValue(ResultsProperty);
            set => SetValue(ResultsProperty, value);
        }

        public static readonly DependencyProperty ResultsProperty =
            DependencyProperty.Register(
                nameof(Results),
                typeof(List<SearchResult>),
                typeof(SearchResultList),
                new PropertyMetadata(null));

        public SearchResultList()
        {
            InitializeComponent();
        }

        private void ResultsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SearchResult result)
            {
                ResultClicked?.Invoke(this, result);
            }
        }
    }
}
