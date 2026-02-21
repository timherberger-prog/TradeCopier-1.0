#region Using declarations
using System;
using System.Collections;
using System.Collections.Generic;
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
    public class TradeCopierAddonCleanV4 : AddOnBase
    {
        private NTMenuItem menuItem;
        private NTMenuItem toolsMenu;
        private TradeCopierEngine engine;
        private TradeCopierWindow window;
        private DispatcherTimer refreshTimer;
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
                refreshTimer = new DispatcherTimer();
                refreshTimer.Interval = TimeSpan.FromSeconds(2);
                refreshTimer.Tick += OnRefreshTimerTick;
            }
            else if (State == State.Terminated)
            {
                if (refreshTimer != null)
                {
                    refreshTimer.Tick -= OnRefreshTimerTick;
                    refreshTimer.Stop();
                    refreshTimer = null;
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

            toolsMenu = cc.MainMenu.OfType<NTMenuItem>().FirstOrDefault(item => item.Name == "ControlCenterMenuItemTools");
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
                MenuItem parent = menuItem.Parent as MenuItem;
                if (parent != null)
                    parent.Items.Remove(menuItem);

                menuItem = null;
                toolsMenu = null;

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
            window.Closed += OnTradeCopierClosed;
            window.LoadAccounts(BuildAccountSnapshot());

            if (refreshTimer != null)
                refreshTimer.Start();

            window.Show();
        }

        private void OnTradeCopierClosed(object sender, EventArgs e)
        {
            if (window != null)
                window.Closed -= OnTradeCopierClosed;

            window = null;

            if (refreshTimer != null)
                refreshTimer.Stop();
        }

        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            if (window == null)
                return;

            window.LoadAccounts(BuildAccountSnapshot());
        }

        private IList<Account> BuildAccountSnapshot()
        {
            var result = new List<Account>();

            AppendAccountsFromObject(result, ReadStaticValue(typeof(Account), "All"));
            AppendAccountsFromObject(result, ReadStaticValue(typeof(Account), "Accounts"));
            AppendAccountsFromObject(result, ReadStaticValue(typeof(Account), "AllAccounts"));
            AppendAccountsFromObject(result, ReadStaticValue(typeof(Account), "VisibleAccounts"));

            if (result.Count == 0 && controlCenter != null)
            {
                AppendAccountsFromObject(result, ReadInstanceValue(controlCenter, "Accounts"));
                AppendAccountsFromObject(result, ReadInstanceValue(controlCenter, "AllAccounts"));

                object dc = controlCenter.DataContext;
                AppendAccountsFromObject(result, ReadInstanceValue(dc, "Accounts"));
                AppendAccountsFromObject(result, ReadInstanceValue(dc, "AllAccounts"));
            }

            return result
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Name))
                .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(a => a.Name)
                .ToList();
        }

        private static void AppendAccountsFromObject(List<Account> destination, object source)
        {
            if (destination == null || source == null)
                return;

            IEnumerable<Account> typed = source as IEnumerable<Account>;
            if (typed != null)
            {
                destination.AddRange(typed);
                return;
            }

            IEnumerable list = source as IEnumerable;
            if (list == null)
                return;

            foreach (object item in list)
            {
                Account account = item as Account;
                if (account != null)
                    destination.Add(account);
            }
        }

        private static object ReadInstanceValue(object target, string member)
        {
            if (target == null || string.IsNullOrWhiteSpace(member))
                return null;

            Type t = target.GetType();
            PropertyInfo p = t.GetProperty(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead)
                return p.GetValue(target, null);

            FieldInfo f = t.GetField(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
                return f.GetValue(target);

            return null;
        }

        private static object ReadStaticValue(Type type, string member)
        {
            if (type == null || string.IsNullOrWhiteSpace(member))
                return null;

            PropertyInfo p = type.GetProperty(member, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead)
                return p.GetValue(null, null);

            FieldInfo f = type.GetField(member, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
                return f.GetValue(null);

            return null;
        }
    }
}
