using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Services
{
    public class CalendarPerformanceMetrics
    {
        public int    TotalTrades       { get; set; }
        public double WinRate           { get; set; }   // 0–100 %
        public double MaxDrawdown       { get; set; }   // ₹, negative
        public double AvgWeeklyRollPnL  { get; set; }   // ₹
        public double TotalHedgeCost    { get; set; }   // ₹, positive = cost paid
        public double MarginBenefit     { get; set; }   // EstHedgeCost × 3
        public double NetHedgeBenefit   { get; set; }   // MarginBenefit − TotalHedgeCost
    }

    public class CalendarPerformanceService
    {
        private static readonly HashSet<string> TradingEvents =
            new() { "SL_HIT", "FLIP_BUY", "FLIP_SELL", "EXIT", "ROLL" };

        // ── Metrics ──────────────────────────────────────────────────────────

        public CalendarPerformanceMetrics CalculateMetrics(
            IReadOnlyList<CalendarPerformanceRecord> records)
        {
            if (records == null || records.Count == 0)
                return new CalendarPerformanceMetrics();

            var trades = records.Where(r => TradingEvents.Contains(r.EventType)).ToList();
            int total  = trades.Count;
            int wins   = trades.Count(r => r.PnL > 0);

            double winRate = total > 0 ? (wins * 100.0 / total) : 0;

            // Max drawdown: biggest peak-to-trough in cumulative PnL series
            double peak = double.MinValue, maxDrawdown = 0;
            foreach (var r in records)
            {
                if (r.CumulativePnL > peak) peak = r.CumulativePnL;
                double dd = r.CumulativePnL - peak;
                if (dd < maxDrawdown) maxDrawdown = dd;
            }

            var rolls        = records.Where(r => r.EventType == "ROLL").ToList();
            double avgRoll   = rolls.Count > 0 ? rolls.Average(r => r.PnL) : 0;

            double hedgeCost = records.Where(r => r.EventType == "HEDGE_BUY")
                                      .Sum(r => r.HedgeCost);
            double margin    = hedgeCost * 3.0;
            double net       = margin - hedgeCost;

            return new CalendarPerformanceMetrics
            {
                TotalTrades      = total,
                WinRate          = Math.Round(winRate, 1),
                MaxDrawdown      = Math.Round(maxDrawdown, 2),
                AvgWeeklyRollPnL = Math.Round(avgRoll, 2),
                TotalHedgeCost   = Math.Round(hedgeCost, 2),
                MarginBenefit    = Math.Round(margin, 2),
                NetHedgeBenefit  = Math.Round(net, 2)
            };
        }

        // ── CSV Export ───────────────────────────────────────────────────────

        public string ExportToCsv(
            IReadOnlyList<CalendarPerformanceRecord> records,
            CalendarPerformanceMetrics metrics,
            string strategyName)
        {
            var sb = new StringBuilder();

            // Summary header
            sb.AppendLine($"Calendar Strategy Performance Report — {strategyName}");
            sb.AppendLine($"Generated:,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("SUMMARY METRICS");
            sb.AppendLine($"Total Trades,{metrics.TotalTrades}");
            sb.AppendLine($"Win Rate %,{metrics.WinRate:N1}");
            sb.AppendLine($"Max Drawdown ₹,{metrics.MaxDrawdown:N2}");
            sb.AppendLine($"Avg Weekly Roll P&L ₹,{metrics.AvgWeeklyRollPnL:N2}");
            sb.AppendLine($"Total Hedge Cost ₹,{metrics.TotalHedgeCost:N2}");
            sb.AppendLine($"Estimated Margin Benefit ₹,{metrics.MarginBenefit:N2}");
            sb.AppendLine($"Net Hedge Benefit ₹,{metrics.NetHedgeBenefit:N2}");
            sb.AppendLine();

            // Trade log
            sb.AppendLine("TRADE LOG");
            sb.AppendLine("Date,Time,EventType,LegDescription,EntryPrice,ExitPrice,P&L ₹,Cumulative P&L ₹,WasHedged,HedgeCost ₹");
            foreach (var r in records)
            {
                sb.AppendLine(string.Format("{0},{1},{2},{3},{4:N2},{5:N2},{6:N2},{7:N2},{8},{9:N2}",
                    r.Date.ToString("yyyy-MM-dd"),
                    r.Date.ToString("HH:mm:ss"),
                    r.EventType,
                    $"\"{r.LegDescription}\"",
                    r.EntryPrice,
                    r.ExitPrice,
                    r.PnL,
                    r.CumulativePnL,
                    r.WasHedged ? "Yes" : "No",
                    r.HedgeCost));
            }

            return sb.ToString();
        }

        public void SaveToCsv(
            IReadOnlyList<CalendarPerformanceRecord> records,
            CalendarPerformanceMetrics metrics,
            string strategyName,
            string filePath)
        {
            File.WriteAllText(filePath, ExportToCsv(records, metrics, strategyName), Encoding.UTF8);
        }
    }
}
