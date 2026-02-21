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

        private static IList<Account> GetAvailableAccounts()
        {
            return GetAllAccountsSnapshot()
                .Where(IsAccountAvailable)
                .GroupBy(account => account.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(account => account.Name)
                .ToList();
        }

        private static IEnumerable<Account> GetAllAccountsSnapshot()
        {
            var accounts = new List<Account>();

            accounts.AddRange(EnumerateAccounts(Account.All));

            Type globalsType = typeof(Account).Assembly.GetType("NinjaTrader.Cbi.Globals");
            if (globalsType != null)
            {
                accounts.AddRange(EnumerateAccounts(ReadMemberValue(globalsType, "Accounts")));
                accounts.AddRange(EnumerateAccounts(ReadMemberValue(globalsType, "AllAccounts")));
            }

            Type connectionType = typeof(Account).Assembly.GetType("NinjaTrader.Cbi.Connection");
            if (connectionType != null)
            {
                object connections = ReadMemberValue(connectionType, "Connections")
                                     ?? ReadMemberValue(connectionType, "All")
                                     ?? ReadMemberValue(connectionType, "AllConnections");

                foreach (object connection in EnumerateObjects(connections))
                {
                    accounts.AddRange(EnumerateAccounts(ReadMemberValue(connection, "Accounts")));
                    accounts.AddRange(EnumerateAccounts(ReadMemberValue(connection, "AccountList")));
                }
            }

            return accounts;
        }

        private static bool IsAccountAvailable(Account account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Name))
                return false;

            if (IsSimulationAccount(account))
                return true;

            return IsConnectedAccount(account);
        }

        private static bool IsSimulationAccount(Account account)
        {
            string name = account?.Name?.Trim() ?? string.Empty;
            if (name.StartsWith("Sim", StringComparison.OrdinalIgnoreCase))
                return true;

            object isSimValue = ReadMemberValue(account, "IsSimAccount")
                                ?? ReadMemberValue(account, "IsSimulation")
                                ?? ReadMemberValue(account, "Simulation");

            if (isSimValue is bool boolValue)
                return boolValue;

            return false;
        }

        private static bool IsConnectedAccount(Account account)
        {
            object connection = ReadMemberValue(account, "Connection")
                                ?? ReadMemberValue(account, "AccountConnection");

            object statusValue = ReadMemberValue(connection, "Status")
                                 ?? ReadMemberValue(connection, "ConnectionStatus")
                                 ?? ReadMemberValue(account, "ConnectionStatus")
                                 ?? ReadMemberValue(account, "Status");

            string normalized = NormalizeStatus(statusValue);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized.EndsWith("connected", StringComparison.OrdinalIgnoreCase)
                    && !normalized.Contains("disconnected");

            object isConnected = ReadMemberValue(connection, "IsConnected")
                                 ?? ReadMemberValue(account, "IsConnected");

            if (isConnected is bool connected)
                return connected;

            // Für nicht-Sim-Konten ohne verwertbaren Verbindungsstatus konservativ ausblenden,
            // damit getrennte Broker-Konten nicht in der Liste hängen bleiben.
            return false;
        }

        private static string NormalizeStatus(object statusValue)
        {
            if (statusValue == null)
                return string.Empty;

            string normalized = statusValue.ToString().Trim().ToLowerInvariant();
            return normalized;
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
