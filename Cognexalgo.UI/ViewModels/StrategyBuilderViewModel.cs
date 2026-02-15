using System;
using Cognexalgo.Core;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;

namespace Cognexalgo.UI.ViewModels
{
    public partial class StrategyBuilderViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _strategyName;

        [ObservableProperty]
        private string _symbol = "NIFTY"; // Default

        [ObservableProperty]
        private string _timeframe = "5min";

        [ObservableProperty]
        private ExpiryType _selectedExpiryType = ExpiryType.Weekly;

        public IEnumerable<ExpiryType> ExpiryTypes => Enum.GetValues(typeof(ExpiryType)).Cast<ExpiryType>();

        [ObservableProperty]
        private string _selectedProductType = "MIS"; // Default to Intraday

        public ObservableCollection<string> ProductTypes { get; } = new ObservableCollection<string> { "MIS", "NRML" };

        public ObservableCollection<RuleViewModel> EntryRules { get; } = new ObservableCollection<RuleViewModel>();
        public ObservableCollection<RuleViewModel> ExitRules { get; } = new ObservableCollection<RuleViewModel>();

        // Selections for Rule Creation
        [ObservableProperty]
        private IndicatorType _selectedIndicator = IndicatorType.LTP;

        [ObservableProperty]
        private Comparator _selectedComparator = Comparator.GREATER_THAN;

        [ObservableProperty]
        private double _staticValue = 0;

        [ObservableProperty]
        private string _action = "BUY_CE"; // Default Action

        private readonly TradingEngine _engine;
        private readonly Action _onSave;

        [ObservableProperty]
        private int _leftPeriod = 14;

        [ObservableProperty]
        private ValueSource _selectedSourceType = ValueSource.StaticValue;

        partial void OnSelectedSourceTypeChanged(ValueSource value)
        {
            if (value == ValueSource.TrendFilter)
            {
                SelectedRightIndicator = IndicatorType.EMA;
                RightPeriod = 200;
            }
        }

        [ObservableProperty]
        private IndicatorType _selectedRightIndicator = IndicatorType.EMA;

        [ObservableProperty]
        private int _rightPeriod = 14;

        public StrategyBuilderViewModel(TradingEngine engine, Action onSave)
        {
            _engine = engine;
            _onSave = onSave;
            _strategyName = string.Empty; // Initialize
        }

        private string BuildDescription()
        {
            var leftPart = $"{SelectedIndicator}({LeftPeriod})";
            var op = SelectedComparator.ToString();
            var rightPart = SelectedSourceType == ValueSource.StaticValue 
                ? StaticValue.ToString() 
                : $"{SelectedRightIndicator}({RightPeriod})"; // Handles Indicator & TrendFilter

            return $"{leftPart} {op} {rightPart} -> {Action}";
        }

        [RelayCommand]
        public void AddEntryRule()
        {
            var rule = new RuleViewModel
            {
                Description = BuildDescription(),
                Condition = new Condition
                {
                    Indicator = SelectedIndicator,
                    Period = LeftPeriod,
                    Operator = SelectedComparator,
                    SourceType = SelectedSourceType,
                    StaticValue = StaticValue,
                    RightIndicator = SelectedRightIndicator,
                    RightPeriod = RightPeriod   
                },
                Action = Action
            };
            EntryRules.Add(rule);
        }

        [RelayCommand]
        public void AddExitRule()
        {
             var leftPart = $"{SelectedIndicator}({LeftPeriod})";
             var op = SelectedComparator.ToString();
             var rightPart = SelectedSourceType == ValueSource.StaticValue 
                ? StaticValue.ToString() 
                : $"{SelectedRightIndicator}({RightPeriod})";

            var rule = new RuleViewModel
            {
                Description = $"{leftPart} {op} {rightPart} -> EXIT",
                Condition = new Condition
                {
                    Indicator = SelectedIndicator,
                    Period = LeftPeriod,
                    Operator = SelectedComparator,
                    SourceType = SelectedSourceType,
                    StaticValue = StaticValue,
                    RightIndicator = SelectedRightIndicator,
                    RightPeriod = RightPeriod
                },
                Action = "EXIT" 
            };
            ExitRules.Add(rule);
        }

        // ========== HYBRID COMMANDS ==========

        [RelayCommand]
        public void AddPosition()
        {
            // Parse strike mode and create leg
            StrikeSelectionMode mode = SelectedStrikeMode switch
            {
                "ATMPoint" => StrikeSelectionMode.ATMPoint,
                "ATMPercent" => StrikeSelectionMode.ATMPercent,
                "StraddleWidth" => StrikeSelectionMode.StraddleWidth,
                "ClosestPremium" => StrikeSelectionMode.ClosestPremium,
                _ => StrikeSelectionMode.ATMPoint
            };

            var leg = new StrategyLeg
            {
                Mode = mode,
                ATMOffset = SelectedATMPoint,
                ATMPercentOffset = ParseATMPercent(SelectedATMPercent),
                StraddleMultiplier = StraddleMultiplier,
                TargetPremium = TargetPremiumValue,
                PremiumOperator = SelectedPremiumOperator,
                WaitForMatch = WaitForMatch,
                Index = SelectedIndex,
                OptionType = SelectedOptionType == "Call" ? OptionType.Call : OptionType.Put,
                Action = SelectedAction == "Buy" ? ActionType.Buy : ActionType.Sell,
                TotalLots = TotalLots,
                ProductType = SelectedProductType,
                ExpiryType = SelectedExpiryType.ToString()
            };

            HybridLegs.Add(leg);
        }

        [RelayCommand]
        public void RemovePosition(StrategyLeg leg)
        {
            HybridLegs.Remove(leg);
        }

        [RelayCommand]
        public void SelectCall()
        {
            SelectedOptionType = "Call";
        }

        [RelayCommand]
        public void SelectPut()
        {
            SelectedOptionType = "Put";
        }

        [RelayCommand]
        public void SelectBuy()
        {
            SelectedAction = "Buy";
        }

        [RelayCommand]
        public void SelectSell()
        {
            SelectedAction = "Sell";
        }

        private double ParseATMPercent(string percentStr)
        {
            if (percentStr == "ATM") return 0.0;

            // Parse "ATM+1.5%" or "ATM-2%" format
            string numPart = percentStr.Replace("ATM", "").Replace("%", "").Trim();
            if (double.TryParse(numPart, out double value))
                return value;

            return 0.0;
        }

        // ========== END HYBRID COMMANDS ==========

        public IEnumerable<IndicatorType> IndicatorTypes => Enum.GetValues(typeof(IndicatorType)).Cast<IndicatorType>();
        public IEnumerable<Comparator> Comparators => Enum.GetValues(typeof(Comparator)).Cast<Comparator>();
        public IEnumerable<ValueSource> ValueSources => Enum.GetValues(typeof(ValueSource)).Cast<ValueSource>();

        public ObservableCollection<string> Symbols { get; } = new ObservableCollection<string> { "NIFTY", "BANKNIFTY" };
        public ObservableCollection<string> Timeframes { get; } = new ObservableCollection<string> { "1min", "3min", "5min", "10min", "15min", "30min", "60min" };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsRuleBuilderVisible))]
        [NotifyPropertyChangedFor(nameof(IsCalendarVisible))]
        [NotifyPropertyChangedFor(nameof(IsHybridVisible))]
        private string _selectedStrategyType = "Dynamic (Rule Based)";

        public ObservableCollection<string> StrategyTypes { get; } = new ObservableCollection<string> { "Dynamic (Rule Based)", "Calendar Straddle", "Hybrid" };

        public bool IsRuleBuilderVisible => SelectedStrategyType == "Dynamic (Rule Based)" || SelectedStrategyType == "Hybrid";
        public bool IsCalendarVisible => SelectedStrategyType == "Calendar Straddle";
        public bool IsHybridVisible => SelectedStrategyType == "Hybrid";

        [ObservableProperty]
        private string _calendarEntryTime = "09:30";

        [ObservableProperty]
        private string _calendarExitTime = "15:15";

        [ObservableProperty]
        private int _lotSize = 1;

        [ObservableProperty]
        private string _selectedChartType = "Spot";

        [ObservableProperty]
        private string _selectedStartDay = "Tuesday";

        [ObservableProperty]
        private string _weeklyExitTime = "15:15";

        [ObservableProperty]
        private string _monthlyExitTime = "15:20";

        [ObservableProperty]
        private double _buyStopLossPercentage = 0;

        [ObservableProperty]
        private bool _useCandleClose = false;

        public ObservableCollection<string> ChartTypes { get; } = new ObservableCollection<string> { "Spot", "Future" };
        public ObservableCollection<string> DaysOfWeek { get; } = new ObservableCollection<string> { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };

        // --- NEW FEATURES ---
        [ObservableProperty]
        private bool _isMatchAllConditions = true;

        [ObservableProperty]
        private TargetType _selectedTargetType = TargetType.Percentage;

        [ObservableProperty]
        private double _targetValue = 1.0;

        [ObservableProperty]
        private StopLossType _selectedStopLossType = StopLossType.Percentage;

        [ObservableProperty]
        private double _stopLossValue = 1.0;

        [ObservableProperty]
        private int _atrPeriod = 14;

        [ObservableProperty]
        private double _atrMultiplier = 2.0;

        // Phase 1: Advanced Exit Features
        [ObservableProperty]
        private bool _enableTrailingStop = false;

        [ObservableProperty]
        private double _trailingStopDistance = 1.0;

        [ObservableProperty]
        private bool _trailingStopIsPercent = true;

        [ObservableProperty]
        private bool _enableTimeBasedExit = false;

        [ObservableProperty]
        private string _exitTime = "15:15";

        [ObservableProperty]
        private bool _enableBreakevenStop = false;

        [ObservableProperty]
        private double _breakevenTriggerPercent = 1.0;

        // Phase 2: Advanced Exit Features
        [ObservableProperty]
        private bool _enablePartialExits = false;

        public ObservableCollection<PartialExitLevel> PartialExitLevels { get; } = new ObservableCollection<PartialExitLevel>();

        [ObservableProperty]
        private bool _enableProfitProtection = false;

        [ObservableProperty]
        private double _profitProtectionPercent = 50.0;

        [ObservableProperty]
        private double _profitProtectionTrigger = 2.0;

        [RelayCommand]
        public void AddPartialExitLevel()
        {
            PartialExitLevels.Add(new PartialExitLevel { Percentage = 50, TargetPercent = 1.5 });
        }

        [RelayCommand]
        public void RemovePartialExitLevel(PartialExitLevel level)
        {
            PartialExitLevels.Remove(level);
        }

        public IEnumerable<TargetType> TargetTypes => Enum.GetValues(typeof(TargetType)).Cast<TargetType>();
        public IEnumerable<StopLossType> StopLossTypes => Enum.GetValues(typeof(StopLossType)).Cast<StopLossType>();

        // ========== HYBRID STRATEGY PROPERTIES ==========

        // Strike Selection Mode
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsATMPointMode))]
        [NotifyPropertyChangedFor(nameof(IsATMPercentMode))]
        [NotifyPropertyChangedFor(nameof(IsStraddleWidthMode))]
        [NotifyPropertyChangedFor(nameof(IsClosestPremiumMode))]
        private string _selectedStrikeMode = "ATMPoint";

        public bool IsATMPointMode => SelectedStrikeMode == "ATMPoint";
        public bool IsATMPercentMode => SelectedStrikeMode == "ATMPercent";
        public bool IsStraddleWidthMode => SelectedStrikeMode == "StraddleWidth";
        public bool IsClosestPremiumMode => SelectedStrikeMode == "ClosestPremium";

        // ATM Point Options
        public ObservableCollection<string> ATMPointOptions { get; } = new ObservableCollection<string>
        {
            "ATM-200", "ATM-150", "ATM-100", "ATM-50", "ATM", "ATM+50", "ATM+100", "ATM+150", "ATM+200"
        };

        [ObservableProperty]
        private string _selectedATMPoint = "ATM";

        // ATM Percent Options
        public ObservableCollection<string> ATMPercentOptions { get; } = new ObservableCollection<string>
        {
            "ATM-3.75%", "ATM-3.5%", "ATM-3.25%", "ATM-3%", "ATM-2.75%", "ATM-2.5%", "ATM-2.25%", "ATM-2%",
            "ATM-1.75%", "ATM-1.5%", "ATM-1.25%", "ATM-1%", "ATM-0.75%", "ATM-0.5%", "ATM-0.25%",
            "ATM", 
            "ATM+0.25%", "ATM+0.5%", "ATM+0.75%", "ATM+1%", "ATM+1.25%", "ATM+1.5%", "ATM+1.75%", "ATM+2%",
            "ATM+2.25%", "ATM+2.5%", "ATM+2.75%", "ATM+3%", "ATM+3.25%", "ATM+3.5%", "ATM+3.75%"
        };

        [ObservableProperty]
        private string _selectedATMPercent = "ATM";

        // Straddle Width
        [ObservableProperty]
        private double _straddleMultiplier = 1.0;

        // Closest Premium
        public ObservableCollection<string> PremiumOperators { get; } = new ObservableCollection<string> { "~", ">=", "<=" };

        [ObservableProperty]
        private string _selectedPremiumOperator = "~";

        [ObservableProperty]
        private double _targetPremiumValue = 25.0;

        [ObservableProperty]
        private bool _waitForMatch = false;

        // Common Hybrid Fields
        public ObservableCollection<string> Indices { get; } = new ObservableCollection<string> { "NIFTY", "BANKNIFTY" };

        [ObservableProperty]
        private string _selectedIndex = "NIFTY";

        [ObservableProperty]
        private string _selectedOptionType = "Call";

        [ObservableProperty]
        private string _selectedAction = "Buy";

        [ObservableProperty]
        private int _totalLots = 1;

        // Legs Collection
        public ObservableCollection<StrategyLeg> HybridLegs { get; } = new ObservableCollection<StrategyLeg>();

        // ========== END HYBRID PROPERTIES ==========

        // --- TEMPLATES ---
        public ObservableCollection<string> Templates { get; } = new ObservableCollection<string> { "Custom", "4 EMA Trend", "SuperTrend" };

        [ObservableProperty]
        private string _selectedTemplate = "Custom";

        partial void OnSelectedTemplateChanged(string value)
        {
            if (value == "4 EMA Trend")
            {
                Load4EmaTrendTemplate();
            }
            else if (value == "Custom")
            {
                EntryRules.Clear();
            }
        }

        private void Load4EmaTrendTemplate()
        {
            EntryRules.Clear();
            IsMatchAllConditions = true;

            // --- BULLISH LEG (BUY_CE) ---
            // 1. Trend: Price > EMA 200
            AddRule(IndicatorType.LTP, 1, Comparator.GREATER_THAN, ValueSource.TrendFilter, 0, IndicatorType.EMA, 200, "BUY_CE");
            // 2. Trend: EMA 21 > EMA 200
            AddRule(IndicatorType.EMA, 21, Comparator.GREATER_THAN, ValueSource.TrendFilter, 0, IndicatorType.EMA, 200, "BUY_CE");
            // 3. Stack: EMA 5 > EMA 9
            AddRule(IndicatorType.EMA, 5, Comparator.GREATER_THAN, ValueSource.Indicator, 0, IndicatorType.EMA, 9, "BUY_CE");
            // 4. Stack: EMA 9 > EMA 13
            AddRule(IndicatorType.EMA, 9, Comparator.GREATER_THAN, ValueSource.Indicator, 0, IndicatorType.EMA, 13, "BUY_CE");
            // 5. Stack: EMA 13 > EMA 21
            AddRule(IndicatorType.EMA, 13, Comparator.GREATER_THAN, ValueSource.Indicator, 0, IndicatorType.EMA, 21, "BUY_CE");
            // 6. Stack: Price > EMA 5
            AddRule(IndicatorType.LTP, 1, Comparator.GREATER_THAN, ValueSource.Indicator, 0, IndicatorType.EMA, 5, "BUY_CE");

            // --- BEARISH LEG (BUY_PE) ---
            // 1. Trend: Price < EMA 200
            AddRule(IndicatorType.LTP, 1, Comparator.LESS_THAN, ValueSource.TrendFilter, 0, IndicatorType.EMA, 200, "BUY_PE");
            // 2. Trend: EMA 21 < EMA 200
            AddRule(IndicatorType.EMA, 21, Comparator.LESS_THAN, ValueSource.TrendFilter, 0, IndicatorType.EMA, 200, "BUY_PE");
            // 3. Stack: EMA 5 < EMA 9
            AddRule(IndicatorType.EMA, 5, Comparator.LESS_THAN, ValueSource.Indicator, 0, IndicatorType.EMA, 9, "BUY_PE");
            // 4. Stack: EMA 9 < EMA 13
            AddRule(IndicatorType.EMA, 9, Comparator.LESS_THAN, ValueSource.Indicator, 0, IndicatorType.EMA, 13, "BUY_PE");
            // 5. Stack: EMA 13 < EMA 21
            AddRule(IndicatorType.EMA, 13, Comparator.LESS_THAN, ValueSource.Indicator, 0, IndicatorType.EMA, 21, "BUY_PE");
            // 6. Stack: Price < EMA 5
            AddRule(IndicatorType.LTP, 1, Comparator.LESS_THAN, ValueSource.Indicator, 0, IndicatorType.EMA, 5, "BUY_PE");
        }

        private void AddRule(IndicatorType left, int leftPeriod, Comparator op, ValueSource source, double statVal, IndicatorType right, int rightPeriod, string action)
        {
            var cond = new Condition
            {
                Indicator = left,
                Period = leftPeriod,
                Operator = op,
                SourceType = source,
                StaticValue = statVal,
                RightIndicator = right,
                RightPeriod = rightPeriod
            };

            var rightPart = source == ValueSource.StaticValue ? statVal.ToString() : $"{right}({rightPeriod})";
            var desc = $"{left}({leftPeriod}) {op} {rightPart} -> {action}";

            EntryRules.Add(new RuleViewModel { Description = desc, Condition = cond, Action = action });
        }

        private int? _existingId = null;

        public void LoadStrategy(HybridStrategyConfig config)
        {
            _existingId = config.Id;
            StrategyName = config.Name;
            
            // Determine Strategy Type
            if (config.StrategyType == "CALENDAR")
            {
                SelectedStrategyType = "Calendar Straddle";
            }
            else if (config.StrategyType == "HYBRID")
            {
                SelectedStrategyType = "Hybrid"; 
            }
            else
            {
                SelectedStrategyType = "Dynamic (Rule Based)";
            }
            
            // Load Base Settings
            LotSize = config.Legs.FirstOrDefault()?.TotalLots ?? 1;
            SelectedProductType = config.ProductType;
            SelectedExpiryType = Enum.TryParse<ExpiryType>(config.ExpiryType, out var expiry) ? expiry : ExpiryType.Weekly;
            
            // Load Legs
            HybridLegs.Clear();
            foreach(var leg in config.Legs)
            {
                HybridLegs.Add(leg);
            }

            // Load Parameters
            if (!string.IsNullOrEmpty(config.Parameters))
            {
                try 
                {
                    var paramsDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(config.Parameters);
                    if (paramsDict != null)
                    {
                        // Global Settings
                        if (paramsDict.ContainsKey("Symbol")) Symbol = paramsDict["Symbol"];
                        if (paramsDict.ContainsKey("Timeframe")) Timeframe = paramsDict["Timeframe"];
                        if (paramsDict.ContainsKey("IsMatchAllConditions")) IsMatchAllConditions = bool.Parse(paramsDict["IsMatchAllConditions"]);

                        // Calendar Settings
                        if (paramsDict.ContainsKey("CalendarEntryTime")) CalendarEntryTime = paramsDict["CalendarEntryTime"];
                        if (paramsDict.ContainsKey("WeeklyExitTime")) WeeklyExitTime = paramsDict["WeeklyExitTime"];
                        if (paramsDict.ContainsKey("MonthlyExitTime")) MonthlyExitTime = paramsDict["MonthlyExitTime"];
                        if (paramsDict.ContainsKey("BuyStopLossPercentage")) BuyStopLossPercentage = Convert.ToDouble(paramsDict["BuyStopLossPercentage"]);
                        if (paramsDict.ContainsKey("ChartType")) SelectedChartType = paramsDict["ChartType"];
                        if (paramsDict.ContainsKey("StartDay")) SelectedStartDay = paramsDict["StartDay"];
                        if (paramsDict.ContainsKey("UseCandleClose")) UseCandleClose = bool.Parse(paramsDict["UseCandleClose"]);

                        // RMS Settings
                        if (paramsDict.ContainsKey("SelectedTargetType")) SelectedTargetType = Enum.Parse<TargetType>(paramsDict["SelectedTargetType"]);
                        if (paramsDict.ContainsKey("TargetValue")) TargetValue = double.Parse(paramsDict["TargetValue"]);
                        if (paramsDict.ContainsKey("SelectedStopLossType")) SelectedStopLossType = Enum.Parse<StopLossType>(paramsDict["SelectedStopLossType"]);
                        if (paramsDict.ContainsKey("StopLossValue")) StopLossValue = double.Parse(paramsDict["StopLossValue"]);
                        if (paramsDict.ContainsKey("AtrPeriod")) AtrPeriod = int.Parse(paramsDict["AtrPeriod"]);
                        if (paramsDict.ContainsKey("AtrMultiplier")) AtrMultiplier = double.Parse(paramsDict["AtrMultiplier"]);

                        // Phase 1: Advanced Exit Features
                        if (paramsDict.ContainsKey("EnableTrailingStop")) EnableTrailingStop = bool.Parse(paramsDict["EnableTrailingStop"]);
                        if (paramsDict.ContainsKey("TrailingStopDistance")) TrailingStopDistance = double.Parse(paramsDict["TrailingStopDistance"]);
                        if (paramsDict.ContainsKey("TrailingStopIsPercent")) TrailingStopIsPercent = bool.Parse(paramsDict["TrailingStopIsPercent"]);
                        if (paramsDict.ContainsKey("EnableTimeBasedExit")) EnableTimeBasedExit = bool.Parse(paramsDict["EnableTimeBasedExit"]);
                        if (paramsDict.ContainsKey("ExitTime")) ExitTime = paramsDict["ExitTime"];
                        if (paramsDict.ContainsKey("EnableBreakevenStop")) EnableBreakevenStop = bool.Parse(paramsDict["EnableBreakevenStop"]);
                        if (paramsDict.ContainsKey("BreakevenTriggerPercent")) BreakevenTriggerPercent = double.Parse(paramsDict["BreakevenTriggerPercent"]);

                        // Phase 2: Advanced Exit Features
                        if (paramsDict.ContainsKey("EnablePartialExits")) EnablePartialExits = bool.Parse(paramsDict["EnablePartialExits"]);
                        if (paramsDict.ContainsKey("PartialExitLevels"))
                        {
                            var levels = JsonConvert.DeserializeObject<List<PartialExitLevel>>(paramsDict["PartialExitLevels"]);
                            if (levels != null)
                            {
                                PartialExitLevels.Clear();
                                foreach (var level in levels)
                                    PartialExitLevels.Add(level);
                            }
                        }
                        if (paramsDict.ContainsKey("EnableProfitProtection")) EnableProfitProtection = bool.Parse(paramsDict["EnableProfitProtection"]);
                        if (paramsDict.ContainsKey("ProfitProtectionPercent")) ProfitProtectionPercent = double.Parse(paramsDict["ProfitProtectionPercent"]);
                        if (paramsDict.ContainsKey("ProfitProtectionTrigger")) ProfitProtectionTrigger = double.Parse(paramsDict["ProfitProtectionTrigger"]);

                        // Rules - Load as Rule objects, then convert to RuleViewModel
                        if (paramsDict.ContainsKey("EntryRules"))
                        {
                            var rules = JsonConvert.DeserializeObject<List<Rule>>(paramsDict["EntryRules"]);
                            if (rules != null)
                            {
                                EntryRules.Clear();
                                foreach(var rule in rules)
                                {
                                    // If rule has multiple conditions, it was saved with "Match All" logic
                                    // We need to create separate RuleViewModel for each condition
                                    foreach(var condition in rule.Conditions)
                                    {
                                        var ruleVm = new RuleViewModel
                                        {
                                            Action = rule.Action,
                                            Condition = condition,
                                            Description = GenerateRuleDescription(condition, rule.Action)
                                        };
                                        EntryRules.Add(ruleVm);
                                    }
                                }
                            }
                        }
                        if (paramsDict.ContainsKey("ExitRules"))
                        {
                            var rules = JsonConvert.DeserializeObject<List<Rule>>(paramsDict["ExitRules"]);
                            if (rules != null)
                            {
                                ExitRules.Clear();
                                foreach(var rule in rules)
                                {
                                    foreach(var condition in rule.Conditions)
                                    {
                                        var ruleVm = new RuleViewModel
                                        {
                                            Action = rule.Action,
                                            Condition = condition,
                                            Description = GenerateRuleDescription(condition, rule.Action)
                                        };
                                        ExitRules.Add(ruleVm);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                     System.Diagnostics.Debug.WriteLine($"Error loading parameters: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        public async Task SaveStrategy()
        {
            if (string.IsNullOrWhiteSpace(StrategyName)) return;

            // Null Check for Legs (Hybrid Only)
            if (SelectedStrategyType == "Hybrid" && (HybridLegs == null || !HybridLegs.Any()))
            {
                System.Windows.MessageBox.Show("Please add at least one position (leg) to save the strategy.", "Validation Error");
                return;
            }

            // Create EntryRules Logic (Group if Match All)
            string entryRulesJson;
            if (IsMatchAllConditions && EntryRules.Any())
            {
                // Group ALL conditions into ONE rule using the Action of the first rule
                var singleRule = new Rule
                {
                    Action = EntryRules.First().Action,
                    Conditions = EntryRules.Select(r => r.Condition).ToList()
                };
                entryRulesJson = JsonConvert.SerializeObject(new List<Rule> { singleRule });
            }
            else
            {
                // Standard: Each RuleViewModel becomes a Rule with 1 Condition (OR logic)
                var rules = EntryRules.Select(r => new Rule
                {
                    Action = r.Action,
                    Conditions = new List<Condition> { r.Condition }
                }).ToList();
                entryRulesJson = JsonConvert.SerializeObject(rules);
            }

            // Create ExitRules Logic (Always OR logic for simplicity in UI for now)
            var exitRules = ExitRules.Select(r => new Rule
            {
                Action = "EXIT",
                Conditions = new List<Condition> { r.Condition }
            }).ToList();

            // Create Parameters Dictionary
            var paramsDict = new Dictionary<string, string>
            {
                { "Symbol", Symbol },
                { "Timeframe", Timeframe },
                { "IsMatchAllConditions", IsMatchAllConditions.ToString() },

                { "CalendarEntryTime", CalendarEntryTime },
                { "WeeklyExitTime", WeeklyExitTime },
                { "MonthlyExitTime", MonthlyExitTime },
                { "BuyStopLossPercentage", BuyStopLossPercentage.ToString() },
                { "ChartType", SelectedChartType },
                { "StartDay", SelectedStartDay },
                { "UseCandleClose", UseCandleClose.ToString() },

                { "SelectedTargetType", SelectedTargetType.ToString() },
                { "TargetValue", TargetValue.ToString() },
                { "SelectedStopLossType", SelectedStopLossType.ToString() },
                { "StopLossValue", StopLossValue.ToString() },
                { "AtrPeriod", AtrPeriod.ToString() },
                { "AtrMultiplier", AtrMultiplier.ToString() },

                // Phase 1: Advanced Exit Features
                { "EnableTrailingStop", EnableTrailingStop.ToString() },
                { "TrailingStopDistance", TrailingStopDistance.ToString() },
                { "TrailingStopIsPercent", TrailingStopIsPercent.ToString() },
                { "EnableTimeBasedExit", EnableTimeBasedExit.ToString() },
                { "ExitTime", ExitTime },
                { "EnableBreakevenStop", EnableBreakevenStop.ToString() },
                { "BreakevenTriggerPercent", BreakevenTriggerPercent.ToString() },

                // Phase 2: Advanced Exit Features
                { "EnablePartialExits", EnablePartialExits.ToString() },
                { "PartialExitLevels", JsonConvert.SerializeObject(PartialExitLevels) },
                { "EnableProfitProtection", EnableProfitProtection.ToString() },
                { "ProfitProtectionPercent", ProfitProtectionPercent.ToString() },
                { "ProfitProtectionTrigger", ProfitProtectionTrigger.ToString() },

                { "EntryRules", entryRulesJson },
                { "ExitRules", JsonConvert.SerializeObject(exitRules) }
            };

            // Create Hybrid Configuration
            var config = new HybridStrategyConfig
            {
                Id = _existingId ?? 0,
                Name = StrategyName,
                IsActive = true, 
                ProductType = SelectedProductType,
                ExpiryType = SelectedExpiryType.ToString(),
                StrategyType = SelectedStrategyType switch {
                    "Calendar Straddle" => "CALENDAR",
                    "Hybrid" => "HYBRID",
                    _ => "DYNAMIC"
                },
                Legs = HybridLegs.ToList(),
                Parameters = JsonConvert.SerializeObject(paramsDict), // Save JSON Parameters
                
                // Optional settings mapping
                AutoExecute = true,
                MaxProfitPercent = (int)TargetValue,
                MaxLossPercent = (int)StopLossValue
            };

            await _engine.StrategyRepository.SaveHybridStrategyAsync(config, "User");
            _onSave?.Invoke();
        }

        private string GenerateRuleDescription(Condition condition, string action)
        {
            var leftPart = $"{condition.Indicator}({condition.Period})";
            
            string rightPart;
            if (condition.SourceType == ValueSource.StaticValue)
            {
                rightPart = condition.StaticValue.ToString();
            }
            else
            {
                rightPart = $"{condition.RightIndicator}({condition.RightPeriod})";
            }
            
            return $"{leftPart} {condition.Operator} {rightPart} -> {action}";
        }
    }

    public class RuleViewModel
    {
        public string Description { get; set; }
        public Condition Condition { get; set; }
        public string Action { get; set; }
    }
}
