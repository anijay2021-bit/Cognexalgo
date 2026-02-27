using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Brokers.AngelOne;

/// <summary>End-to-end login test runner — validates TOTP, login, profile, funds, feed, tick flow.</summary>
public class LoginTestRunner
{
    private readonly IBrokerFactory _brokerFactory;
    private readonly IMarketDataService _marketData;
    private readonly ILogger<LoginTestRunner> _logger;

    public LoginTestRunner(IBrokerFactory brokerFactory, IMarketDataService marketData, ILogger<LoginTestRunner> logger)
    {
        _brokerFactory = brokerFactory;
        _marketData = marketData;
        _logger = logger;
    }

    /// <summary>Runs the full login + feed validation sequence.</summary>
    public async Task<LoginTestResult> RunFullLoginTestAsync(AccountCredential cred)
    {
        var result = new LoginTestResult { AccountName = cred.AccountName };
        var overallSw = System.Diagnostics.Stopwatch.StartNew();

        // Step 1: TOTP generation test
        var step1 = new TestStepResult { StepName = "TOTP Generation" };
        try
        {
            var totp = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(cred.TOTPSecret));
            var code = totp.ComputeTotp();
            step1.Passed = code.Length == 6 && long.TryParse(code, out _);
            step1.Detail = $"Generated: {code} (6-digit: {step1.Passed})";
            step1.Elapsed = TimeSpan.FromMilliseconds(5);
        }
        catch (Exception ex)
        {
            step1.Passed = false;
            step1.Detail = $"TOTP error: {ex.Message}";
        }
        result.Steps.Add(step1);

        // Step 2: Login
        var step2 = new TestStepResult { StepName = "Broker Login" };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var broker = _brokerFactory.Create(cred.BrokerType);
            var loginOk = await broker.LoginAsync(cred);
            sw.Stop();
            step2.Elapsed = sw.Elapsed;
            step2.Passed = loginOk && !string.IsNullOrEmpty(cred.JWTToken);
            step2.Detail = loginOk ? $"Token: {cred.JWTToken?[..Math.Min(20, cred.JWTToken?.Length ?? 0)]}..." : "Login returned false";
        }
        catch (Exception ex)
        {
            sw.Stop();
            step2.Elapsed = sw.Elapsed;
            step2.Passed = false;
            step2.Detail = $"Login error: {ex.Message}";
        }
        result.Steps.Add(step2);

        if (!step2.Passed)
        {
            // Can't continue without login
            result.TotalElapsed = overallSw.Elapsed;
            return result;
        }

        // Step 3: Get Profile
        var step3 = new TestStepResult { StepName = "Get Profile" };
        sw.Restart();
        try
        {
            var broker = _brokerFactory.Create(cred.BrokerType);
            var profile = await broker.GetProfileAsync(cred.JWTToken!);
            sw.Stop();
            step3.Elapsed = sw.Elapsed;
            step3.Passed = !string.IsNullOrEmpty(profile.ClientId);
            step3.Detail = $"Client: {profile.ClientId} | Name: {profile.Name}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            step3.Elapsed = sw.Elapsed;
            step3.Passed = false;
            step3.Detail = $"Profile error: {ex.Message}";
        }
        result.Steps.Add(step3);

        // Step 4: Get Funds
        var step4 = new TestStepResult { StepName = "Get Funds" };
        sw.Restart();
        try
        {
            var broker = _brokerFactory.Create(cred.BrokerType);
            var funds = await broker.GetFundsAsync(cred.JWTToken!);
            sw.Stop();
            step4.Elapsed = sw.Elapsed;
            step4.Passed = funds.AvailableMargin >= 0;
            step4.Detail = $"Margin: ₹{funds.AvailableMargin:N2} | Used: ₹{funds.UsedMargin:N2}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            step4.Elapsed = sw.Elapsed;
            step4.Passed = false;
            step4.Detail = $"Funds error: {ex.Message}";
        }
        result.Steps.Add(step4);

        // Step 5: WebSocket Connect
        var step5 = new TestStepResult { StepName = "WebSocket Connect" };
        sw.Restart();
        try
        {
            await _marketData.ConnectAsync(cred);
            // Wait up to 10s for connection
            for (int i = 0; i < 20; i++)
            {
                if (_marketData.IsConnected) break;
                await Task.Delay(500);
            }
            sw.Stop();
            step5.Elapsed = sw.Elapsed;
            step5.Passed = _marketData.IsConnected;
            step5.Detail = _marketData.IsConnected ? "WebSocket connected" : "Connection timed out (10s)";
        }
        catch (Exception ex)
        {
            sw.Stop();
            step5.Elapsed = sw.Elapsed;
            step5.Passed = false;
            step5.Detail = $"WS error: {ex.Message}";
        }
        result.Steps.Add(step5);

        // Step 6: Subscribe and receive first tick
        var step6 = new TestStepResult { StepName = "Receive First Tick (NIFTY)" };
        sw.Restart();
        if (_marketData.IsConnected)
        {
            try
            {
                var tickReceived = new TaskCompletionSource<bool>();
                using var sub = _marketData.TickStream.Subscribe(t =>
                {
                    if (!tickReceived.Task.IsCompleted)
                        tickReceived.TrySetResult(true);
                });
                await _marketData.SubscribeAsync(new System.Collections.Generic.List<(Exchange, string)> { (Exchange.NSE, "26000") }, SubscriptionMode.LTP);

                var completed = await Task.WhenAny(tickReceived.Task, Task.Delay(15_000));
                sw.Stop();
                step6.Elapsed = sw.Elapsed;
                step6.Passed = completed == tickReceived.Task;
                step6.Detail = step6.Passed ? $"First tick in {sw.ElapsedMilliseconds}ms" : "No tick within 15s";
            }
            catch (Exception ex)
            {
                sw.Stop();
                step6.Elapsed = sw.Elapsed;
                step6.Passed = false;
                step6.Detail = $"Tick error: {ex.Message}";
            }
        }
        else
        {
            step6.Passed = false;
            step6.Detail = "Skipped — WebSocket not connected";
        }
        result.Steps.Add(step6);

        result.TotalElapsed = overallSw.Elapsed;
        return result;
    }
}

/// <summary>Result of a full login test sequence.</summary>
public class LoginTestResult
{
    public string AccountName { get; set; } = "";
    public List<TestStepResult> Steps { get; set; } = new();
    public TimeSpan TotalElapsed { get; set; }
    public bool AllPassed => Steps.All(s => s.Passed);
    public int PassCount => Steps.Count(s => s.Passed);
    public int FailCount => Steps.Count(s => !s.Passed);
}

public class TestStepResult
{
    public string StepName { get; set; } = "";
    public bool Passed { get; set; }
    public string Detail { get; set; } = "";
    public TimeSpan Elapsed { get; set; }
}
