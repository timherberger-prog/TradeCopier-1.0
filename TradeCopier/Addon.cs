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
    public class TradeCopierAddon : AddOnBase
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
                Name = "Trade Copier";
            else if (State == State.Active)
            {
                engine = new TradeCopierEngine();
                accountSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
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

                engine?.Dispose();
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

            menuItem = new NTMenuItem
            {
                Header = "Trade Copier",
                Style = Application.Current.TryFindResource("MainMenuItem") as Style
            };

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
                (menuItem.Parent as System.Windows.Controls.MenuItem)?.Items.Remove(menuItem);
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
            window.Closed += (s, a) =>
            {
                window = null;
                if (accountSyncTimer != null)
                    accountSyncTimer.Stop();
            };

            window.LoadAccounts(GetAvailableAccounts());
            if (accountSyncTimer != null)
                accountSyncTimer.Start();

            window.Show();
        }

        private void OnAccountSyncTick(object sender, EventArgs e)
        {
            if (window == null)
                return;

            window.LoadAccounts(GetAvailableAccounts());
        }

        private IList<Account> GetAvailableAccounts()
        {
            var accounts = new List<Account>();
            Type accountType = typeof(Account);

            foreach (string staticName in new[] { "All", "Accounts", "AllAccounts", "VisibleAccounts", "ConnectedAccounts" })
                accounts.AddRange(ConvertSourceToAccountsTcV2(ReadStaticMemberTcV2(accountType, staticName)));

            if (accounts.Count == 0 && controlCenter != null)
            {
                object[] roots = { controlCenter, controlCenter.DataContext };
                string[] members = { "Accounts", "AllAccounts", "DisplayedAccounts", "VisibleAccounts", "ConnectedAccounts", "ActiveAccounts" };

                foreach (object root in roots)
                {
                    if (root == null)
                        continue;

                    foreach (string member in members)
                        accounts.AddRange(ConvertSourceToAccountsTcV2(ReadInstanceMemberTcV2(root, member)));
                }
            }

            return accounts
                .Where(IsAccountAvailableTcV2)
                .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(a => a.Name)
                .ToList();
        }

        private static IEnumerable<Account> ConvertSourceToAccountsTcV2(object source)
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

        private static bool IsAccountAvailableTcV2(Account account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Name))
                return false;

            object v1 = ReadInstanceMemberTcV2(account, "IsVisible");
            object v2 = ReadInstanceMemberTcV2(account, "Visible");
            object v3 = ReadInstanceMemberTcV2(account, "IsDisplayed");
            object v4 = ReadInstanceMemberTcV2(account, "Display");

            bool? visible = (v1 is bool b1) ? b1
                          : (v2 is bool b2) ? b2
                          : (v3 is bool b3) ? b3
                          : (v4 is bool b4) ? b4
                          : (bool?)null;

            return visible ?? true;
        }

        private static object ReadInstanceMemberTcV2(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead)
                return property.GetValue(target, null);

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null ? field.GetValue(target) : null;
        }

        private static object ReadStaticMemberTcV2(Type type, string memberName)
        {
            if (type == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead)
                return property.GetValue(null, null);

            FieldInfo field = type.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null ? field.GetValue(null) : null;
        }
    }
}
