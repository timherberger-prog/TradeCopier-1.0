#region Using declarations
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            // Alternative, robuste Quelle: direkte Account-Container statt VisualTree-Parsing.
            IEnumerable<Account> source = EnumerateStaticAccountSources();

            if (!source.Any() && controlCenter != null)
                source = EnumerateControlCenterAccountSources(controlCenter).SelectMany(ToAccounts);

            return source
                .Where(IsAccountAvailable)
                .GroupBy(account => account.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(account => account.Name)
                .ToList();
        }

        private IEnumerable<Account> GetDisplayedAccountsFromControlCenterSnapshot()
        {
            if (controlCenter == null)
                return Enumerable.Empty<Account>();

            var collected = new List<Account>();

            foreach (object source in EnumerateControlCenterAccountSources(controlCenter))
                collected.AddRange(ToAccounts(source));

            if (collected.Count == 0)
                collected.AddRange(GetAccountsFromVisualTree(controlCenter));

            return collected;
        }

        private IEnumerable<Account> GetAllAccountsSnapshot()
        {
            var collected = new List<Account>();

            collected.AddRange(ToAccounts(ReadStaticMemberValue(typeof(Account), "All")));
            collected.AddRange(ToAccounts(ReadStaticMemberValue(typeof(Account), "Accounts")));

            if (controlCenter != null)
            {
                foreach (object source in EnumerateControlCenterAccountSources(controlCenter))
                    collected.AddRange(ToAccounts(source));

                if (collected.Count == 0)
                    collected.AddRange(GetAccountsFromVisualTree(controlCenter));
            }

            return collected;
        }

        private static IEnumerable<object> EnumerateControlCenterAccountSources(ControlCenter cc)
        {
            if (cc == null)
                yield break;

            object dataContext = cc.DataContext;
            object[] roots = { cc, dataContext };
            string[] memberCandidates =
            {
                "Accounts",
                "AllAccounts",
                "DisplayedAccounts",
                "VisibleAccounts",
                "ConnectedAccounts",
                "ActiveAccounts"
            };

            foreach (object root in roots.Where(r => r != null))
            {
                foreach (string member in memberCandidates)
                {
                    object value = ReadMemberValue(root, member);
                    if (value != null)
                        yield return value;
                }
            }

            // Fallback: alle statischen IEnumerable-Member von Account durchprobieren.
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (PropertyInfo property in accountType.GetProperties(flags))
            {
                if (!property.CanRead)
                    continue;

                foreach (Account account in ToAccounts(property.GetValue(null, null)))
                    yield return account;
            }

            foreach (FieldInfo field in accountType.GetFields(flags))
            {
                var fromItems = ToAccounts(selector.Items);
                if (fromItems.Any())
                    return fromItems;

                return ToAccounts(selector.ItemsSource);
            }
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

        private static bool? ReadBooleanMember(object target, string memberName)
        {
            object value = ReadMemberValue(target, memberName);
            return value is bool flag ? flag : (bool?)null;
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

        private static object ReadStaticMemberValue(Type type, string memberName)
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
