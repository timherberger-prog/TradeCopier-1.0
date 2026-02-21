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

                accountSyncTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
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

                if (menuItem != null)
                    menuItem.Click -= OnMenuClick;

                if (engine != null)
                {
                    engine.Dispose();
                    engine = null;
                }
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            var cc = window as ControlCenter;
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
            if (!(window is ControlCenter) || menuItem == null)
                return;

            menuItem.Click -= OnMenuClick;

            MenuItem parentMenu = menuItem.Parent as MenuItem;
            if (parentMenu != null)
                parentMenu.Items.Remove(menuItem);

            if (ReferenceEquals(controlCenter, window))
                controlCenter = null;

            menuItem = null;
            toolsMenu = null;
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
            window.LoadAccounts(GetAvailableAccounts());

            if (accountSyncTimer != null)
                accountSyncTimer.Start();

            window.Show();
        }

        private void OnTradeCopierClosed(object sender, EventArgs e)
        {
            if (window != null)
                window.Closed -= OnTradeCopierClosed;

            window = null;

            if (accountSyncTimer != null)
                accountSyncTimer.Stop();
        }

        private void OnAccountSyncTick(object sender, EventArgs e)
        {
            if (window == null)
                return;

            window.LoadAccounts(GetAvailableAccounts());
        }

        private IList<Account> GetAvailableAccounts()
        {
            return EnumerateAccountCandidates()
                .Where(IsAccountAvailable)
                .GroupBy(account => account.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(account => account.Name)
                .ToList();
        }

        private IEnumerable<Account> EnumerateAccountCandidates()
        {
            foreach (Account account in EnumerateStaticAccountSources())
                yield return account;

            foreach (Account account in EnumerateControlCenterAccountSources(controlCenter))
                yield return account;
        }

        private IEnumerable<Account> EnumerateStaticAccountSources()
        {
            Type accountType = typeof(Account);

            foreach (string memberName in new[] { "All", "AllAccounts", "Accounts" })
            {
                object value = ReadStaticMemberValue(accountType, memberName);
                foreach (Account account in ToAccounts(value))
                    yield return account;
            }
        }

        private IEnumerable<Account> EnumerateControlCenterAccountSources(ControlCenter cc)
        {
            if (cc == null)
                yield break;

            foreach (Account account in ToAccounts(ReadInstanceMemberValue(cc, "Accounts")))
                yield return account;

            foreach (Account account in ToAccounts(ReadInstanceMemberValue(cc.DataContext, "Accounts")))
                yield return account;

            foreach (Account account in ToAccounts(ReadInstanceMemberValue(cc.DataContext, "AllAccounts")))
                yield return account;
        }

        private static IEnumerable<Account> ToAccounts(object source)
        {
            if (source == null)
                yield break;

            var singleAccount = source as Account;
            if (singleAccount != null)
            {
                yield return singleAccount;
                yield break;
            }

            var enumerable = source as IEnumerable;
            if (enumerable == null)
                yield break;

            foreach (object item in enumerable)
            {
                var account = item as Account;
                if (account != null)
                    yield return account;
            }
        }

        private static bool IsAccountAvailable(Account account)
        {
            return account != null && !string.IsNullOrWhiteSpace(account.Name);
        }

        private static object ReadStaticMemberValue(Type type, string memberName)
        {
            if (type == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead)
                return property.GetValue(null, null);

            FieldInfo field = type.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null ? field.GetValue(null) : null;
        }

        private static object ReadInstanceMemberValue(object target, string memberName)
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
    }
}
