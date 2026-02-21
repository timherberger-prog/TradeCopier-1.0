#region Using declarations
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.TradeCopier
{
    public class TradeCopierAddonV3 : AddOnBase
    {
        private NTMenuItem menuItem;
        private NTMenuItem toolsMenu;
        private TradeCopierEngine engine;
        private TradeCopierWindow window;
        private DispatcherTimer accountSyncTimer;
        private ControlCenter controlCenter;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trade Copier";
            }
            else if (State == State.Active)
            {
                engine = new TradeCopierEngine();
                accountSyncTimer = new DispatcherTimer();
                accountSyncTimer.Interval = TimeSpan.FromSeconds(2);
                accountSyncTimer.Tick += OnAccountSyncTick;
            }
            else if (State == State.Terminated)
            {
                if (accountSyncTimer != null)
                {
                    accountSyncTimer.Tick -= OnAccountSyncTick;
                    accountSyncTimer.Stop();
                    accountSyncTimer = null;
                }

                if (engine != null)
                    engine.Dispose();
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            ControlCenter cc = window as ControlCenter;
            if (cc == null)
                return;

            controlCenter = cc;

            if (menuItem != null)
                return;

            menuItem = new NTMenuItem();
            menuItem.Header = "Trade Copier";
            menuItem.Style = Application.Current.TryFindResource("MainMenuItem") as Style;
            menuItem.Click += OnMenuClick;

            toolsMenu = cc.MainMenu
                .OfType<NTMenuItem>()
                .FirstOrDefault(item => item.Name == "ControlCenterMenuItemTools");

            if (toolsMenu != null)
                toolsMenu.Items.Add(menuItem);
            else
                cc.MainMenu.Add(menuItem);
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (menuItem == null)
                return;

            if (window is ControlCenter)
            {
                menuItem.Click -= OnMenuClick;
                MenuItem parentMenu = menuItem.Parent as MenuItem;
                if (parentMenu != null)
                    parentMenu.Items.Remove(menuItem);

                toolsMenu = null;
                menuItem = null;

                if (ReferenceEquals(controlCenter, window))
                    controlCenter = null;
            }
        }

        private void OnMenuClick(object sender, RoutedEventArgs e)
        {
            if (window != null)
            {
                window.Activate();
                return;
            }

            window = new TradeCopierWindow(engine);
            window.Closed += OnTradeCopierWindowClosed;

            window.LoadAccounts(GetAvailableAccountsV3());

            if (accountSyncTimer != null)
                accountSyncTimer.Start();

            window.Show();
        }

        private void OnTradeCopierWindowClosed(object sender, EventArgs e)
        {
            if (window != null)
                window.Closed -= OnTradeCopierWindowClosed;

            window = null;

            if (accountSyncTimer != null)
                accountSyncTimer.Stop();
        }

        private void OnAccountSyncTick(object sender, EventArgs e)
        {
            if (window == null)
                return;

            window.LoadAccounts(GetAvailableAccountsV3());
        }

        private IList<Account> GetAvailableAccountsV3()
        {
            var accounts = new List<Account>();
            Type accountType = typeof(Account);

            string[] staticNames = { "All", "Accounts", "AllAccounts", "VisibleAccounts", "ConnectedAccounts" };
            for (int i = 0; i < staticNames.Length; i++)
            {
                object source = ReadStaticMemberUniqueV3(accountType, staticNames[i]);
                accounts.AddRange(ConvertToAccountsUniqueV3(source));
            }

            if (accounts.Count == 0 && controlCenter != null)
            {
                string[] memberNames = { "Accounts", "AllAccounts", "DisplayedAccounts", "VisibleAccounts", "ConnectedAccounts", "ActiveAccounts" };
                object[] roots = { controlCenter, controlCenter.DataContext };

                for (int r = 0; r < roots.Length; r++)
                {
                    object root = roots[r];
                    if (root == null)
                        continue;

                    for (int m = 0; m < memberNames.Length; m++)
                    {
                        object source = ReadInstanceMemberUniqueV3(root, memberNames[m]);
                        accounts.AddRange(ConvertToAccountsUniqueV3(source));
                    }
                }
            }

            return accounts
                .Where(a => IsAccountAvailableUniqueV3(a))
                .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(a => a.Name)
                .ToList();
        }

        private static IEnumerable<Account> ConvertToAccountsUniqueV3(object source)
        {
            if (source == null)
                return Enumerable.Empty<Account>();

            ICollectionView view = source as ICollectionView;
            if (view != null)
                return view.Cast<object>().OfType<Account>();

            IEnumerable<Account> typed = source as IEnumerable<Account>;
            if (typed != null)
                return typed;

            IEnumerable enumerable = source as IEnumerable;
            if (enumerable == null)
                return Enumerable.Empty<Account>();

            return enumerable.Cast<object>().OfType<Account>();
        }

        private static bool IsAccountAvailableUniqueV3(Account account)
        {
            if (account == null)
                return false;

            if (string.IsNullOrWhiteSpace(account.Name))
                return false;

            object isVisible = ReadInstanceMemberUniqueV3(account, "IsVisible");
            if (isVisible is bool)
                return (bool)isVisible;

            object visible = ReadInstanceMemberUniqueV3(account, "Visible");
            if (visible is bool)
                return (bool)visible;

            object isDisplayed = ReadInstanceMemberUniqueV3(account, "IsDisplayed");
            if (isDisplayed is bool)
                return (bool)isDisplayed;

            object display = ReadInstanceMemberUniqueV3(account, "Display");
            if (display is bool)
                return (bool)display;

            return true;
        }

        private static object ReadInstanceMemberUniqueV3(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            Type type = target.GetType();

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead)
                return property.GetValue(target, null);

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(target);

            return null;
        }

        private static object ReadStaticMemberUniqueV3(Type type, string memberName)
        {
            if (type == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead)
                return property.GetValue(null, null);

            FieldInfo field = type.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(null);

            return null;
        }
    }
}
