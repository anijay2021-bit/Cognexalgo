using System.Threading.Tasks;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Strategies
{
    public interface IStrategy
    {
        string Name { get; }
        bool IsActive { get; set; }
        Task OnTickAsync(TickerData ticker); // Live Data Hook
        decimal Pnl { get; }
        void Start();
        void Stop();
    }

    public abstract class StrategyBase : IStrategy
    {
        public string Name { get; protected set; }
        public bool IsActive { get; set; }
        public decimal Pnl { get; protected set; } = 0;

        protected TradingEngine _engine;

        public StrategyBase(TradingEngine engine, string name)
        {
            _engine = engine;
            Name = name;
        }

        public virtual void Start()
        {
            IsActive = true;
            // Subscribe to engine events if needed
        }

        public virtual void Stop()
        {
            IsActive = false;
        }

        public abstract Task OnTickAsync(TickerData ticker);

        public virtual void RecalculatePnl(TickerData data) 
        {
            // Base implementation does nothing
        }

        // Signal Event
        public event System.Action<Signal> OnSignalGenerated;

        protected void BroadcastSignal(Signal signal)
        {
            OnSignalGenerated?.Invoke(signal);
        }
    }
}
