#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
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
            window.Closed += (_, __) =>
            {
                window = null;
                accountSyncTimer?.Stop();
            };

            window.LoadAccounts(GetAvailableAccounts());
            accountSyncTimer?.Start();

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
            IList<Account> controlCenterAccounts = GetAccountsFromControlCenter();
            if (controlCenterAccounts.Count > 0)
                return controlCenterAccounts;

            return EnumerateAccounts(Account.All)
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .GroupBy(account => account.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(account => account.Name)
                .ToList();
        }

        private IList<Account> GetAccountsFromControlCenter()
        {
            if (controlCenter == null)
                return new List<Account>();

            var accounts = new List<Account>();

            accounts.AddRange(EnumerateAccounts(ReadMemberValue(controlCenter, "Accounts")));
            accounts.AddRange(EnumerateAccounts(ReadMemberValue(controlCenter, "AllAccounts")));

            object selector = ReadMemberValue(controlCenter, "AccountSelector")
                              ?? ReadMemberValue(controlCenter, "accountSelector")
                              ?? ReadMemberValue(controlCenter, "AccountSelection")
                              ?? ReadMemberValue(controlCenter, "accountSelection");

            accounts.AddRange(EnumerateAccounts(selector));
            accounts.AddRange(EnumerateAccounts(ReadMemberValue(selector, "Accounts")));
            accounts.AddRange(EnumerateAccounts(ReadMemberValue(selector, "ItemsSource")));
            accounts.AddRange(EnumerateAccounts(ReadMemberValue(selector, "Items")));

            if (accounts.Count == 0)
            {
                foreach (MemberInfo member in controlCenter.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!member.Name.ToLowerInvariant().Contains("account"))
                        continue;

                    object value = ReadMemberValueSafe(controlCenter, member.Name);
                    accounts.AddRange(EnumerateAccounts(value));
                }
            }

            return accounts
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .GroupBy(account => account.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(account => account.Name)
                .ToList();
        }

        private static IEnumerable<Account> EnumerateAccounts(object source)
        {
            return EnumerateObjects(source).OfType<Account>();
        }

        private static IEnumerable<object> EnumerateObjects(object source)
        {
            if (source == null)
                yield break;

            if (source is string)
                yield break;

            if (source is System.Collections.IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                    yield return item;

                yield break;
            }

            yield return source;
        }

        private static object ReadMemberValueSafe(object target, string memberName)
        {
            try
            {
                return ReadMemberValue(target, memberName);
            }
            catch
            {
                return null;
            }
        }

        private static object ReadMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead)
                return property.GetValue(target, null);

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(target);
        }

        private static object ReadMemberValue(Type type, string memberName)
        {
            if (type == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead)
                return property.GetValue(null, null);

            FieldInfo field = type.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(null);
        }
    }
}
