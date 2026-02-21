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

            accounts.AddRange(ResolveAccountsFromStaticContainersTc());

            if (accounts.Count == 0 && controlCenter != null)
                accounts.AddRange(ResolveAccountsFromControlCenterTc(controlCenter));

            return accounts
                .Where(IsAccountAvailable)
                .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(a => a.Name)
                .ToList();
        }

        private static List<Account> ResolveAccountsFromStaticContainersTc()
        {
            var result = new List<Account>();
            Type accountType = typeof(Account);

            string[] preferredMembers =
            {
                "All",
                "Accounts",
                "AllAccounts",
                "VisibleAccounts",
                "ConnectedAccounts"
            };

            foreach (string memberName in preferredMembers)
                result.AddRange(ConvertAnyToAccountsTc(ReadStaticMemberTc(accountType, memberName)));

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (PropertyInfo property in accountType.GetProperties(flags))
            {
                if (property.CanRead)
                    result.AddRange(ConvertAnyToAccountsTc(property.GetValue(null, null)));
            }

            foreach (FieldInfo field in accountType.GetFields(flags))
                result.AddRange(ConvertAnyToAccountsTc(field.GetValue(null)));

            return result;
        }

        private static List<Account> ResolveAccountsFromControlCenterTc(ControlCenter cc)
        {
            var result = new List<Account>();
            if (cc == null)
                return result;

            object[] roots = { cc, cc.DataContext };
            string[] memberNames =
            {
                "Accounts",
                "AllAccounts",
                "DisplayedAccounts",
                "VisibleAccounts",
                "ConnectedAccounts",
                "ActiveAccounts"
            };

            foreach (object root in roots)
            {
                if (root == null)
                    continue;

                foreach (string memberName in memberNames)
                    result.AddRange(ConvertAnyToAccountsTc(ReadInstanceMemberTc(root, memberName)));
            }

            return result;
        }

        private static IEnumerable<Account> ConvertAnyToAccountsTc(object source)
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

        private static bool IsAccountAvailable(Account account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Name))
                return false;

            bool? visible = ReadBooleanMemberTc(account, "IsVisible")
                            ?? ReadBooleanMemberTc(account, "Visible")
                            ?? ReadBooleanMemberTc(account, "IsDisplayed")
                            ?? ReadBooleanMemberTc(account, "Display");

            return visible ?? true;
        }

        private static bool? ReadBooleanMemberTc(object target, string memberName)
        {
            object value = ReadInstanceMemberTc(target, memberName);
            return value is bool flag ? flag : (bool?)null;
        }

        private static object ReadInstanceMemberTc(object target, string memberName)
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

        private static object ReadStaticMemberTc(Type type, string memberName)
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
