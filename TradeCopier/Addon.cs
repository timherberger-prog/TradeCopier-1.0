#region Using declarations
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            {
                Name = "Trade Copier";
            }
            else if (State == State.Active)
            {
                engine = new TradeCopierEngine();
                accountSyncTimer = new DispatcherTimer();
                accountSyncTimer.Interval = TimeSpan.FromSeconds(2);
                accountSyncTimer.Tick += OnRefreshTimerTick;
            }
            else if (State == State.Terminated)
            {
                if (accountSyncTimer != null)
                {
                    accountSyncTimer.Tick -= OnRefreshTimerTick;
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

            toolsMenu = cc.MainMenu.OfType<NTMenuItem>().FirstOrDefault(i => i.Name == "ControlCenterMenuItemTools");
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
            MenuItem parent = menuItem.Parent as MenuItem;
            if (parent != null)
                parent.Items.Remove(menuItem);

            menuItem = null;
            toolsMenu = null;

            if (ReferenceEquals(controlCenter, window))
                controlCenter = null;
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
            window.LoadAccounts(GetAvailableAccountsV3());

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

        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            if (window == null)
                return;

            window.LoadAccounts(GetAvailableAccountsV3());
        }

        // Compatibility entry-point name retained for NinjaScript cache collisions.
        private IList<Account> GetAvailableAccountsV3()
        {
            List<Account> collected = EnumerateStaticAccountSources().ToList();

            if (collected.Count == 0 && controlCenter != null)
            {
                foreach (object source in EnumerateControlCenterAccountSources(controlCenter))
                    collected.AddRange(ToAccounts(source));
            }

            return collected
                .Where(IsAccountAvailable)
                .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(a => a.Name)
                .ToList();
        }

        private IEnumerable<Account> EnumerateStaticAccountSources()
        {
            Type accountType = typeof(Account);
            string[] names = { "All", "Accounts", "AllAccounts", "VisibleAccounts", "ConnectedAccounts" };

            foreach (string n in names)
                foreach (Account a in ToAccounts(ReadStaticMemberValue(accountType, n)))
                    yield return a;
        }

        private IEnumerable<object> EnumerateControlCenterAccountSources(ControlCenter cc)
        {
            if (cc == null)
                yield break;

            object[] roots = { cc, cc.DataContext };
            string[] memberNames = { "Accounts", "AllAccounts", "DisplayedAccounts", "VisibleAccounts", "ConnectedAccounts", "ActiveAccounts" };

            foreach (object root in roots)
            {
                if (root == null)
                    continue;

                foreach (string member in memberNames)
                {
                    object value = ReadMemberValue(root, member);
                    if (value != null)
                        yield return value;
                }
            }
        }

        private List<Account> CollectAccountsFromControlCenter(ControlCenter cc)
        {
            var result = new List<Account>();
            foreach (object source in EnumerateControlCenterAccountSources(cc))
                result.AddRange(ToAccounts(source));
            return result;
        }

        private static IEnumerable<Account> ToAccounts(object source)
        {
            if (source == null)
                return Enumerable.Empty<Account>();

            ICollectionView view = source as ICollectionView;
            if (view != null)
                return view.Cast<object>().OfType<Account>();

            IEnumerable<Account> typed = source as IEnumerable<Account>;
            if (typed != null)
                return typed;

            IEnumerable raw = source as IEnumerable;
            if (raw == null)
                return Enumerable.Empty<Account>();

            return raw.Cast<object>().OfType<Account>();
        }

        private static IEnumerable<Account> ConvertSourceToAccountsTcV2(object source)
        {
            return ToAccounts(source);
        }

        private static bool IsAccountAvailable(Account account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Name))
                return false;

            bool? visible = ReadBooleanMember(account, "IsVisible")
                            ?? ReadBooleanMember(account, "Visible")
                            ?? ReadBooleanMember(account, "IsDisplayed")
                            ?? ReadBooleanMember(account, "Display");

            return visible ?? true;
        }

        private static bool IsAccountAvailableTcV2(Account account)
        {
            return IsAccountAvailable(account);
        }

        private static bool? ReadBooleanMember(object target, string memberName)
        {
            object value = ReadMemberValue(target, memberName);
            return value is bool ? (bool)value : (bool?)null;
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
            if (field != null)
                return field.GetValue(target);

            return null;
        }

        private static object ReadStaticMemberValue(Type type, string memberName)
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

        private static object ReadStaticMemberTcV2(Type type, string memberName)
        {
            return ReadStaticMemberValue(type, memberName);
        }

        private static IEnumerable<Account> GetAccountsFromVisualTree(DependencyObject root)
        {
            // kept only as compatibility stub for stale NinjaScript references
            return Enumerable.Empty<Account>();
        }
    }
}
