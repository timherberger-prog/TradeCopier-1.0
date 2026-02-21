#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
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
        }

        public void Stop()
        {
            IsEnabled = false;
        }

        public void Dispose()
        {
            lock (sync)
            {
                IsEnabled = false;
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
            if (follower == null || instrument == null || quantity <= 0)
                return;

            // Marktorder als erste robuste Version.
            follower.CreateOrder(
                instrument,
                action,
                OrderType.Market,
                TimeInForce.Day,
                quantity,
                0,
                0,
                string.Empty,
                "LeadCopyEntry");
        }

        private void FlattenFollowers(Instrument instrument)
        {
            foreach (var follower in followerAccounts)
            {
                if (follower == null)
                    continue;

                Position pos = follower.Positions.FirstOrDefault(p => p.Instrument == instrument);
                if (pos == null || pos.Quantity.ApproxCompare(0) == 0)
                    continue;

                OrderAction exitAction = pos.Quantity > 0 ? OrderAction.Sell : OrderAction.BuyToCover;

                follower.CreateOrder(
                    instrument,
                    exitAction,
                    OrderType.Market,
                    TimeInForce.Day,
                    Math.Abs(pos.Quantity),
                    0,
                    0,
                    string.Empty,
                    "LeadProtectionFlatten");
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
