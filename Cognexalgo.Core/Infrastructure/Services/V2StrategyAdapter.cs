using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Infrastructure.Persistence;
using Cognexalgo.Core.Infrastructure.Services;
using Newtonsoft.Json;

// Alias to disambiguate Domain.Entities from Models namespace
using V2Strategy = Cognexalgo.Core.Domain.Entities.Strategy;
using V2StrategyLeg = Cognexalgo.Core.Domain.Entities.StrategyLeg;
using LegacyConfig = Cognexalgo.Core.Models.HybridStrategyConfig;

namespace Cognexalgo.Core.Infrastructure.Services
{
    /// <summary>
    /// Bridges legacy HybridStrategyConfig → V2 Strategy entity.
    /// Called from MainViewModel after legacy save to persist to V2 DB.
    /// </summary>
    public class V2StrategyAdapter
    {
        private readonly V2Bridge _bridge;

        public V2StrategyAdapter(V2Bridge bridge)
        {
            _bridge = bridge;
        }

        /// <summary>
        /// After a legacy strategy is saved, call this to create the V2 mirror.
        /// Returns the V2 strategy ID.
        /// </summary>
        public async Task<string?> SyncToV2Async(LegacyConfig legacyConfig)
        {
            if (_bridge == null || !_bridge.IsInitialized) return null;

            try
            {
                // Map legacy → V2 StrategyType  
                var stratType = (legacyConfig.StrategyType?.ToUpper()) switch
                {
                    "CALENDAR" => StrategyType.STRD,
                    "HYBRID" => StrategyType.CSTM,
                    "CUSTOM" => StrategyType.CSTM,
                    "STRADDLE" => StrategyType.STRD,
                    "STRANGLE" => StrategyType.STNG,
                    "IRON_CONDOR" => StrategyType.CNDL,
                    _ => StrategyType.CSTM
                };

                // Parse parameters JSON
                var parameters = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(legacyConfig.Parameters))
                {
                    try { parameters = JsonConvert.DeserializeObject<Dictionary<string, string>>(legacyConfig.Parameters) ?? new(); }
                    catch { }
                }

                using var scope = _bridge.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Generate V2 strategy ID
                string dateStr = DateTime.Now.ToString("yyyyMMdd");
                string typeCode = stratType.ToString().Length >= 3 
                    ? stratType.ToString().Substring(0, 3).ToUpper() 
                    : stratType.ToString().ToUpper();
                int seqCount = db.Strategies.Count(s => s.StrategyId.StartsWith($"STR-{dateStr}")) + 1;
                string strategyId = $"STR-{dateStr}-{typeCode}-{seqCount:D3}";

                // Determine underlying from legs or parameters
                string underlying = parameters.GetValueOrDefault("Symbol", "NIFTY");
                if (string.IsNullOrEmpty(underlying) && legacyConfig.Legs?.Count > 0)
                    underlying = legacyConfig.Legs[0].Index ?? "NIFTY";

                // Create V2 Strategy entity (fields match Strategy.cs exactly)
                var v2Strategy = new V2Strategy
                {
                    StrategyId = strategyId,
                    Name = legacyConfig.Name ?? "Unnamed",
                    Type = stratType,
                    Status = StrategyStatus.Active,
                    TradingMode = TradingMode.PaperTrade,
                    UnderlyingSymbol = underlying,
                    CreatedAt = DateTime.UtcNow,
                    IsTemplate = false,
                    SignalConfigJson = parameters.GetValueOrDefault("EntryRules", "[]"),
                    RmsConfigJson = JsonConvert.SerializeObject(new Domain.ValueObjects.RmsConfig
                    {
                        MaxProfit = legacyConfig.MaxProfitPercent > 0 
                            ? legacyConfig.MaxProfitPercent * 100M : 10000M,
                        MaxLoss = legacyConfig.MaxLossPercent > 0 
                            ? legacyConfig.MaxLossPercent * 100M : 5000M,
                        TrailingSL = decimal.TryParse(
                            parameters.GetValueOrDefault("TrailingStopDistance", "0"), out var tsd) ? tsd : 0,
                        TrailingIsPercent = parameters.GetValueOrDefault("TrailingStopIsPercent", "") == "True",
                        TimeBasedExitTime = parameters.GetValueOrDefault("ExitTime", "15:15")
                    }),
                    ExecutionConfigJson = JsonConvert.SerializeObject(new Domain.ValueObjects.ExecutionConfig
                    {
                        EntryTime = parameters.GetValueOrDefault("CalendarEntryTime", "09:20"),
                        ExitTime = parameters.GetValueOrDefault("ExitTime", "15:15"),
                        SlippagePercent = 0.05
                    }),
                    MetricsJson = "{}"
                };

                // Map legacy legs → V2 StrategyLegs
                if (legacyConfig.Legs != null)
                {
                    int legNum = 0;
                    foreach (var leg in legacyConfig.Legs)
                    {
                        legNum++;
                        v2Strategy.Legs.Add(new V2StrategyLeg
                        {
                            LegId = $"{strategyId}-L{legNum}",
                            StrategyId = strategyId,
                            LegNumber = legNum,
                            TradingSymbol = "", // Populated at execution time
                            SymbolToken = leg.SymbolToken ?? "",
                            Exchange = "NFO",
                            InstrumentType = InstrumentType.OPTIDX,
                            OptionType = leg.OptionType.ToString(),
                            Direction = leg.Action == Models.ActionType.Buy 
                                ? Direction.BUY : Direction.SELL,
                            Quantity = leg.TotalLots > 0 ? leg.TotalLots * 25 : 25,
                            Lots = leg.TotalLots > 0 ? leg.TotalLots : 1,
                            StrikePrice = (decimal)leg.CalculatedStrike,
                            EntryPrice = (decimal)leg.EntryPrice,
                            ExitPrice = (decimal)leg.ExitPrice,
                            Status = LegStatus.PENDING,
                            StopLossPrice = (decimal)leg.StopLossPrice,
                            TargetPrice = (decimal)leg.TargetPrice,
                            Pnl = 0
                        });
                    }
                }

                // Save to V2 database  
                db.Strategies.Add(v2Strategy);
                await db.SaveChangesAsync();

                _bridge.Logger.Info("V2Adapter", 
                    $"Strategy synced: {strategyId} ({legacyConfig.Name})", strategyId);

                return strategyId;
            }
            catch (Exception ex)
            {
                _bridge.Logger.Error("V2Adapter", 
                    $"Failed to sync strategy: {ex.Message}", ex);
                return null;
            }
        }
    }
}
