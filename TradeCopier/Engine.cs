#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.TradeCopier
{
    /// <summary>
    /// Core logik für das Trade-Copying:
    /// - Lead-Konto steuert Entry-Richtung und Positionsgröße
    /// - Follow-Konten bekommen nur Entry/Exit in gleicher Richtung
    /// - Schutzorders (SL/TP) bleiben exklusiv am Lead-Konto
    /// - Bei Schutz-Exit am Lead werden Follow-Konten aktiv geflattet
    /// </summary>
    public class TradeCopierEngine
    {
        public event Action<bool> StatusChanged;

        private readonly object sync = new object();
        private readonly HashSet<string> copiedOrderIds = new HashSet<string>();

        private Account leadAccount;
        private readonly List<Account> followerAccounts = new List<Account>();

        public bool IsEnabled { get; private set; }

        public void Configure(Account lead, IEnumerable<Account> followers)
        {
            lock (sync)
            {
                UnsubscribeInternal();

                leadAccount = lead;
                followerAccounts.Clear();

                if (followers != null)
                    followerAccounts.AddRange(followers.Where(a => a != null && a != lead));

                SubscribeInternal();
            }
        }

        public void Start()
        {
            IsEnabled = true;
            StatusChanged?.Invoke(IsEnabled);
        }

        public void Stop()
        {
            IsEnabled = false;
            StatusChanged?.Invoke(IsEnabled);
        }

        public void Dispose()
        {
            lock (sync)
            {
                IsEnabled = false;
                StatusChanged?.Invoke(IsEnabled);
                UnsubscribeInternal();
                followerAccounts.Clear();
                leadAccount = null;
                copiedOrderIds.Clear();
            }
        }

        private void SubscribeInternal()
        {
            if (leadAccount == null)
                return;

            leadAccount.OrderUpdate += OnLeadOrderUpdate;
            leadAccount.ExecutionUpdate += OnLeadExecutionUpdate;
        }

        private void UnsubscribeInternal()
        {
            if (leadAccount == null)
                return;

            leadAccount.OrderUpdate -= OnLeadOrderUpdate;
            leadAccount.ExecutionUpdate -= OnLeadExecutionUpdate;
        }

        private void OnLeadOrderUpdate(object sender, OrderEventArgs e)
        {
            if (!IsEnabled || leadAccount == null || e?.Order == null)
                return;

            // Schutz-Order am Lead ausgeführt -> Follow-Konten flatten.
            if (IsLeadProtectionExit(e.Order))
                FlattenFollowers(e.Order.Instrument);
        }

        private void OnLeadExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            if (!IsEnabled || leadAccount == null || e?.Execution?.Order == null)
                return;

            Order leadOrder = e.Execution.Order;

            // Nur primäre Entries spiegeln. Schutzorders werden nicht kopiert.
            if (!IsEntryOrder(leadOrder) || IsProtectionOrder(leadOrder))
                return;

            string correlationKey = BuildCorrelationKey(e.Execution);
            lock (sync)
            {
                if (copiedOrderIds.Contains(correlationKey))
                    return;
                copiedOrderIds.Add(correlationKey);
            }

            int quantity = Math.Abs(e.Execution.Quantity);
            if (quantity <= 0)
                return;

            foreach (var follower in followerAccounts)
                SubmitFollowerEntry(follower, e.Execution.Instrument, leadOrder.OrderAction, quantity);
        }

        private void SubmitFollowerEntry(Account follower, Instrument instrument, OrderAction action, int quantity)
        {
            SubmitMarketOrder(follower, instrument, action, quantity, "LeadCopyEntry");
        }

        private void FlattenFollowers(Instrument instrument)
        {
            foreach (var follower in followerAccounts)
            {
                if (follower == null)
                    continue;

                Position pos = follower.Positions.FirstOrDefault(p => p.Instrument == instrument);
                if (pos == null || pos.Quantity == 0)
                    continue;

                OrderAction exitAction = pos.Quantity > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
                SubmitMarketOrder(follower, instrument, exitAction, Math.Abs(pos.Quantity), "LeadProtectionFlatten");
            }
        }

        private static void SubmitMarketOrder(Account account, Instrument instrument, OrderAction action, int quantity, string signalName)
        {
            if (account == null || instrument == null || quantity <= 0)
                return;

            Order order = CreateOrderCompat(account, instrument, action, quantity, signalName);
            if (order == null)
                return;

            SubmitOrderCompat(account, order);
        }

        private static Order CreateOrderCompat(Account account, Instrument instrument, OrderAction action, int quantity, string signalName)
        {
            MethodInfo[] methods = typeof(Account)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == "CreateOrder")
                .ToArray();

            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();
                object[] args = new object[parameters.Length];
                bool failed = false;

                for (int i = 0; i < parameters.Length; i++)
                {
                    ParameterInfo parameter = parameters[i];
                    string parameterName = parameter.Name?.ToLowerInvariant() ?? string.Empty;
                    Type parameterType = parameter.ParameterType;

                    object value = null;
                    if (parameterType == typeof(Instrument))
                        value = instrument;
                    else if (parameterType == typeof(OrderAction))
                        value = action;
                    else if (parameterType == typeof(OrderType))
                        value = OrderType.Market;
                    else if (parameterType == typeof(TimeInForce))
                        value = TimeInForce.Day;
                    else if (parameterType == typeof(int) && parameterName.Contains("quantity"))
                        value = quantity;
                    else if (parameterType == typeof(double))
                        value = 0d;
                    else if (parameterType == typeof(string) && (parameterName.Contains("name") || parameterName.Contains("signal")))
                        value = signalName;
                    else if (parameterType == typeof(string))
                        value = string.Empty;
                    else if (parameterType == typeof(DateTime))
                        value = DateTime.MinValue;
                    else if (parameterType.IsEnum)
                        value = Enum.GetValues(parameterType).GetValue(0);
                    else if (parameter.HasDefaultValue)
                        value = parameter.DefaultValue;
                    else if (!parameterType.IsValueType)
                        value = null;
                    else
                        failed = true;

                    if (failed)
                        break;

                    args[i] = value;
                }

                if (failed)
                    continue;

                try
                {
                    return method.Invoke(account, args) as Order;
                }
                catch
                {
                    // Nächsten Overload probieren.
                }
            }

            return null;
        }

        private static void SubmitOrderCompat(Account account, Order order)
        {
            MethodInfo[] methods = typeof(Account)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == "Submit")
                .ToArray();

            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();
                object[] args;

                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Order))
                    args = new object[] { order };
                else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Order[]))
                    args = new object[] { new[] { order } };
                else
                    continue;

                try
                {
                    method.Invoke(account, args);
                    return;
                }
                catch
                {
                    // Nächsten Overload probieren.
                }
            }
        }

        private static bool IsEntryOrder(Order order)
        {
            return order.OrderAction == OrderAction.Buy
                || order.OrderAction == OrderAction.SellShort;
        }

        private static bool IsLeadProtectionExit(Order order)
        {
            if (!IsProtectionOrder(order))
                return false;

            return order.OrderState == OrderState.Filled || order.OrderState == OrderState.PartFilled;
        }

        private static bool IsProtectionOrder(Order order)
        {
            // Heuristik: OCO gesetzt oder expliziter Name für TP/SL.
            if (!string.IsNullOrWhiteSpace(order.Oco))
                return true;

            string name = order.Name?.ToLowerInvariant() ?? string.Empty;
            return name.Contains("stop") || name.Contains("target") || name.Contains("take");
        }

        private static string BuildCorrelationKey(Execution execution)
        {
            return $"{execution.Order?.OrderId}:{execution.ExecutionId}:{execution.Time:O}";
        }
    }
}
