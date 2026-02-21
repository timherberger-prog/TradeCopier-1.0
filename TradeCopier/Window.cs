#region Using declarations
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        public TradeCopierWindow(TradeCopierEngine engine)
        {
            this.engine = engine;

            Caption = "Trade Copier";
            Width = 460;
            Height = 420;

            Content = BuildLayout();
        }

        public void LoadAccounts(IList<Account> accounts)
        {
            leadAccountCombo.ItemsSource = accounts;
            leadAccountCombo.DisplayMemberPath = nameof(Account.Name);

            followerAccountsList.ItemsSource = accounts;
            followerAccountsList.DisplayMemberPath = nameof(Account.Name);
        }

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var leadLabel = new TextBlock { Text = "Lead-Konto" };
            Grid.SetRow(leadLabel, 0);
            root.Children.Add(leadLabel);

            leadAccountCombo = new ComboBox { Margin = new Thickness(0, 6, 0, 12) };
            Grid.SetRow(leadAccountCombo, 1);
            root.Children.Add(leadAccountCombo);

            followerAccountsList = new ListBox
            {
                SelectionMode = SelectionMode.Multiple,
                Margin = new Thickness(0, 6, 0, 12)
            };
            Grid.SetRow(followerAccountsList, 2);
            root.Children.Add(followerAccountsList);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            enabledCheck = new CheckBox { Content = "Copier aktiv", Margin = new Thickness(0, 0, 10, 0) };
            enabledCheck.Checked += (_, __) => engine.Start();
            enabledCheck.Unchecked += (_, __) => engine.Stop();

            var applyButton = new Button { Content = "Übernehmen", MinWidth = 120 };
            applyButton.Click += OnApply;

            footer.Children.Add(enabledCheck);
            footer.Children.Add(applyButton);

            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            return root;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            var lead = leadAccountCombo.SelectedItem as Account;
            var followers = followerAccountsList.SelectedItems.Cast<Account>().ToList();

            engine.Configure(lead, followers);

            if (enabledCheck.IsChecked == true)
                engine.Start();
        }
    }
}
