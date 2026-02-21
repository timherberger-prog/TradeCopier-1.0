#region Using declarations
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.TradeCopier
{
    public class TradeCopierWindow : NTWindow
    {
        private readonly TradeCopierEngine engine;
        private ComboBox leadAccountCombo;
        private ListBox followerAccountsList;
        private CheckBox enabledCheck;
        private Border statusBar;
        private TextBlock statusText;
        private string configuredLeadName;
        private HashSet<string> configuredFollowerNames = new HashSet<string>();

        public TradeCopierWindow(TradeCopierEngine engine)
        {
            this.engine = engine;

            Caption = "Trade Copier";
            Width = 460;
            Height = 420;

            Content = BuildLayout();

            engine.StatusChanged += OnEngineStatusChanged;
            Closed += (_, __) => engine.StatusChanged -= OnEngineStatusChanged;

            UpdateStatusBar(engine.IsEnabled);
            enabledCheck.IsChecked = engine.IsEnabled;
        }

        public void LoadAccounts(IList<Account> accounts)
        {
            var accountList = (accounts ?? new List<Account>()).Where(a => a != null).ToList();

            var selectedLeadName = (leadAccountCombo.SelectedItem as Account)?.Name;
            var selectedFollowerNames = followerAccountsList.SelectedItems
                .Cast<Account>()
                .Select(a => a.Name)
                .ToHashSet();

            leadAccountCombo.ItemsSource = accountList;
            leadAccountCombo.DisplayMemberPath = nameof(Account.Name);
            leadAccountCombo.SelectedItem = accountList.FirstOrDefault(a => a.Name == selectedLeadName);

            followerAccountsList.ItemsSource = accountList;
            followerAccountsList.DisplayMemberPath = nameof(Account.Name);
            followerAccountsList.SelectedItems.Clear();

            foreach (var account in accountList.Where(a => selectedFollowerNames.Contains(a.Name)))
                followerAccountsList.SelectedItems.Add(account);

            RefreshEngineAccountBindings(accountList);
        }

        private void RefreshEngineAccountBindings(IList<Account> accountList)
        {
            if (string.IsNullOrWhiteSpace(configuredLeadName))
                return;

            var lead = accountList.FirstOrDefault(a => a.Name == configuredLeadName);
            var followers = accountList
                .Where(a => configuredFollowerNames.Contains(a.Name) && a != lead)
                .ToList();

            engine.Configure(lead, followers);
        }

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            statusBar = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 10)
            };
            statusText = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };
            statusBar.Child = statusText;
            Grid.SetRow(statusBar, 0);
            root.Children.Add(statusBar);

            var leadLabel = new TextBlock { Text = "Lead-Konto" };
            Grid.SetRow(leadLabel, 1);
            root.Children.Add(leadLabel);

            leadAccountCombo = new ComboBox { Margin = new Thickness(0, 6, 0, 12) };
            Grid.SetRow(leadAccountCombo, 2);
            root.Children.Add(leadAccountCombo);

            followerAccountsList = new ListBox
            {
                SelectionMode = SelectionMode.Multiple,
                Margin = new Thickness(0, 6, 0, 12)
            };
            Grid.SetRow(followerAccountsList, 3);
            root.Children.Add(followerAccountsList);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            enabledCheck = new CheckBox { Content = "Copier aktiv", Margin = new Thickness(0, 0, 10, 0) };
            enabledCheck.Checked += (_, __) => engine.Start();
            enabledCheck.Unchecked += (_, __) => engine.Stop();

            var applyButton = new Button { Content = "Übernehmen", MinWidth = 120 };
            applyButton.Click += OnApply;

            footer.Children.Add(enabledCheck);
            footer.Children.Add(applyButton);

            Grid.SetRow(footer, 4);
            root.Children.Add(footer);

            return root;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            var lead = leadAccountCombo.SelectedItem as Account;
            var followers = followerAccountsList.SelectedItems.Cast<Account>().ToList();

            configuredLeadName = lead?.Name;
            configuredFollowerNames = followers
                .Select(a => a.Name)
                .ToHashSet();

            engine.Configure(lead, followers);

            if (enabledCheck.IsChecked == true)
                engine.Start();
        }

        private void OnEngineStatusChanged(bool isEnabled)
        {
            Dispatcher.InvokeAsync(() =>
            {
                enabledCheck.IsChecked = isEnabled;
                UpdateStatusBar(isEnabled);
            });
        }

        private void UpdateStatusBar(bool isEnabled)
        {
            statusText.Text = isEnabled ? "Status: Copier aktiv" : "Status: Copier gestoppt";
            statusBar.Background = isEnabled
                ? new SolidColorBrush(Color.FromRgb(46, 125, 50))
                : new SolidColorBrush(Color.FromRgb(198, 40, 40));
        }
    }
}
