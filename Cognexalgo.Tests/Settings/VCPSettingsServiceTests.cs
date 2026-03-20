using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Cognexalgo.Core.Domain.Patterns;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services.Settings;
using Xunit;

namespace Cognexalgo.Tests.Settings
{
    public sealed class VCPSettingsServiceTests : IDisposable
    {
        private readonly string _dir;

        public VCPSettingsServiceTests()
        {
            // Unique temp directory per test class instance — xUnit creates one instance per test
            _dir = Path.Combine(Path.GetTempPath(), $"VCPSettingsTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
        }

        private string SettingsPath => Path.Combine(_dir, "vcp_settings.json");

        private VCPSettingsService CreateService() =>
            new(NullLogger<VCPSettingsService>.Instance, SettingsPath);

        // ── Load tests ───────────────────────────────────────────────────────

        [Fact]
        public void Test_Load_ReturnsDefaults_WhenFileNotExists()
        {
            var svc = CreateService();

            var settings = svc.Load();

            settings.Should().NotBeNull();
            settings.TradingMode.Should().Be(VCPTradingMode.PaperTrade);
            settings.Timeframe.Should().Be(VCPTimeframe.FifteenMin);
            settings.MaxConcurrentTrades.Should().Be(2);
            settings.RiskAmountPerTrade.Should().Be(1_000m);
            settings.NiftyLotSize.Should().Be(65);
            settings.BankNiftyLotSize.Should().Be(30);
            settings.Watchlist.Should().BeEquivalentTo(new[] { "NIFTY", "BANKNIFTY" });
        }

        [Fact]
        public void Test_Load_ReturnsDefaults_WhenJsonMalformed()
        {
            File.WriteAllText(SettingsPath, "{ this is not valid json !!!");

            var svc      = CreateService();
            var settings = svc.Load();

            settings.Should().NotBeNull();
            settings.TradingMode.Should().Be(VCPTradingMode.PaperTrade,
                "malformed JSON should fall back to defaults");
        }

        [Fact]
        public void Test_Load_ClampsMaxTrades_WhenValueExceedsFour()
        {
            var json = JsonSerializer.Serialize(new VCPSettings
            {
                MaxConcurrentTrades = 99,
                Watchlist = new List<string> { "NIFTY" },
            });
            File.WriteAllText(SettingsPath, json);

            var settings = CreateService().Load();

            settings.MaxConcurrentTrades.Should().Be(4,
                "MaxConcurrentTrades > 4 must be clamped to 4");
        }

        [Fact]
        public void Test_Load_SetsNiftyLotSize_To65_WhenValueIsZero()
        {
            var json = JsonSerializer.Serialize(new VCPSettings
            {
                NiftyLotSize = 0,
                Watchlist    = new List<string> { "NIFTY" },
            });
            File.WriteAllText(SettingsPath, json);

            var settings = CreateService().Load();

            settings.NiftyLotSize.Should().Be(65,
                "NiftyLotSize=0 must be overridden to 65");
        }

        [Fact]
        public void Test_Load_SetsBankNiftyLotSize_To30_WhenValueIsZero()
        {
            var json = JsonSerializer.Serialize(new VCPSettings
            {
                BankNiftyLotSize = 0,
                Watchlist        = new List<string> { "NIFTY" },
            });
            File.WriteAllText(SettingsPath, json);

            var settings = CreateService().Load();

            settings.BankNiftyLotSize.Should().Be(30,
                "BankNiftyLotSize=0 must be overridden to 30");
        }

        [Fact]
        public void Test_Load_ResetsRRTargets_WhenTarget1GreaterThanOrEqualTarget2()
        {
            var json = JsonSerializer.Serialize(new VCPSettings
            {
                Target1RR = 4.0m,
                Target2RR = 2.0m,
                Watchlist = new List<string> { "NIFTY" },
            });
            File.WriteAllText(SettingsPath, json);

            var settings = CreateService().Load();

            settings.Target1RR.Should().Be(1.5m);
            settings.Target2RR.Should().Be(3.0m);
        }

        [Fact]
        public void Test_Load_ResetsWatchlist_WhenEmpty()
        {
            var json = JsonSerializer.Serialize(new VCPSettings
            {
                Watchlist = new List<string>(),
            });
            File.WriteAllText(SettingsPath, json);

            var settings = CreateService().Load();

            settings.Watchlist.Should().BeEquivalentTo(new[] { "NIFTY", "BANKNIFTY" },
                "empty watchlist must be reset to defaults");
        }

        // ── Save tests ───────────────────────────────────────────────────────

        [Fact]
        public void Test_Save_WritesFile_Successfully()
        {
            var svc = CreateService();
            var toSave = new VCPSettings
            {
                TradingMode         = VCPTradingMode.LiveTrade,
                MaxConcurrentTrades = 3,
                RiskAmountPerTrade  = 2_500m,
                NiftyLotSize        = 65,
                BankNiftyLotSize    = 30,
                Watchlist           = new List<string> { "RELIANCE", "NIFTY" },
            };

            svc.Save(toSave);

            File.Exists(SettingsPath).Should().BeTrue("Save should create the settings file");

            var loaded = svc.Load();
            loaded.TradingMode.Should().Be(VCPTradingMode.LiveTrade);
            loaded.MaxConcurrentTrades.Should().Be(3);
            loaded.RiskAmountPerTrade.Should().Be(2_500m);
            loaded.Watchlist.Should().BeEquivalentTo(new[] { "RELIANCE", "NIFTY" });
        }

        [Fact]
        public void Test_Save_DoesNotThrow_WhenDirectoryNotWritable()
        {
            // Point the service at a path whose parent directory does not exist
            // — File.WriteAllText will throw IOException, which must be swallowed.
            string badPath = Path.Combine(_dir, "nonexistent_subdir", "vcp_settings.json");
            var svc = new VCPSettingsService(NullLogger<VCPSettingsService>.Instance, badPath);

            var act = () => svc.Save(new VCPSettings
            {
                Watchlist = new List<string> { "NIFTY" },
            });

            act.Should().NotThrow("Save must never propagate exceptions to the caller");
        }
    }
}
