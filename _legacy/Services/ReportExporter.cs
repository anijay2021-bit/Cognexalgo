using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Services
{
    public class ReportExporter
    {
        public string ExportToCsv(IEnumerable<Order> orders)
        {
            var sb = new StringBuilder();
            
            // Header
            sb.AppendLine("Timestamp,OrderId,Strategy,Symbol,Transaction,Qty,EntryPrice,ExitPrice,PotentialProfit,ProtectedProfit,ActualProfit,Efficiency%");

            foreach (var order in orders)
            {
                // Note: Entry/Exit prices might need more logic if we track them separately in the model, 
                // but for now we export what we have.
                string line = string.Format("{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3},{4},{5},{6:N2},{7:N2},{8:N2},{9:N2},{10:N2},{11:N1}",
                    order.Timestamp,
                    order.OrderId,
                    order.StrategyName,
                    order.Symbol,
                    order.TransactionType,
                    order.Qty,
                    0, // Entry Price (Placeholder or if added to model)
                    order.Price,
                    order.PotentialProfit,
                    order.ProtectedProfit,
                    order.ActualProfit,
                    order.ProtectedProfit > 0 ? (order.ActualProfit / order.ProtectedProfit) * 100 : 0
                );
                sb.AppendLine(line);
            }

            return sb.ToString();
        }

        public void SaveToFile(string csvContent, string filePath)
        {
            File.WriteAllText(filePath, csvContent);
        }
    }
}
