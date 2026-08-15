using System;
using System.Collections.Generic;
using System.IO;
using PBIRelay.Models;
using PBIRelay.Services;
using Xunit;

namespace PBIRelay.Core.Tests
{
    /// <summary>
    /// Covers the endpoint's runtime lifetime (#125): settings change while the app
    /// runs, and the listener follows — but only when a setting it actually uses moved.
    /// </summary>
    public sealed class XmlaEndpointCoordinatorTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ConfigService _config;
        private readonly FakeEndpoint _endpoint = new FakeEndpoint();

        public XmlaEndpointCoordinatorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PBIRelayTests", Guid.NewGuid().ToString("N"));
            _config = new ConfigService(new ConfigurationManager(_tempDir));
            _config.Load();
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        /// <summary>A listener that records what it was asked to do, and can refuse to bind.</summary>
        private sealed class FakeEndpoint : IXmlaEndpoint
        {
            public int StartCount;
            public int StopCount;
            public readonly List<int> StartedPorts = new();
            public Exception FailWith;

            public bool IsRunning { get; private set; }
            public string BoundPrefix { get; private set; }

            public void Start(HttpBridgeConfig config)
            {
                StartCount++;
                StartedPorts.Add(config.Port);
                if (FailWith != null) throw FailWith;

                IsRunning = true;

                // The scheme it actually bound, which is what the coordinator reports
                // while it runs - a listener serving http must never be described as
                // https just because the configuration asks for it.
                BoundPrefix = $"{(config.UseHttps ? "https" : "http")}://+:{config.Port}/";
            }

            public void Stop()
            {
                StopCount++;
                IsRunning = false;
                BoundPrefix = null;
            }
        }

        /// <summary>Records what was logged and at which level, without touching disk.</summary>
        private sealed class FakeLogger : ILogger, ILogLevelSwitch
        {
            public LogLevel MinimumLevel { get; set; } = LogLevel.Info;
            public readonly List<(LogLevel Level, string Message)> Entries = new();

            public void Log(LogLevel level, string category, string message, Exception exception = null)
            {
                if (level < MinimumLevel) return;
                Entries.Add((level, message));
            }

            public string GetLogFilePath() => string.Empty;
        }

        private XmlaEndpointCoordinator Coordinator() =>
            new XmlaEndpointCoordinator(_endpoint, _config);

        // --- Diagnostic verbosity ----------------------------------------------------

        [Fact]
        public void LogPayloads_RaisesAndLowersVerbosity_WithoutRestartingTheListener()
        {
            // Routing detail is logged at Debug so ~50 lines per Excel session stay out
            // of the dashboard. LogPayloads is the existing "tell me everything" switch,
            // so it is what makes them visible - one diagnostic control, not two.
            var logger = new FakeLogger();
            _config.SetEndpointEnabled(true);
            using var coordinator = new XmlaEndpointCoordinator(_endpoint, _config, logger);
            coordinator.ApplyConfiguration();

            Assert.Equal(LogLevel.Info, logger.MinimumLevel);

            _config.Current.HttpBridge.LogPayloads = true;
            _config.Save();
            Assert.Equal(LogLevel.Debug, logger.MinimumLevel);

            _config.Current.HttpBridge.LogPayloads = false;
            _config.Save();
            Assert.Equal(LogLevel.Info, logger.MinimumLevel);

            // LogPayloads is not a listener setting: toggling it must not disconnect
            // anyone, which is the whole reason it is checked before the change test.
            Assert.Equal(1, _endpoint.StartCount);
            Assert.Equal(0, _endpoint.StopCount);
        }

        // --- Encryption settings (#132) ----------------------------------------------

        [Fact]
        public void TurningHttpsOn_RebindsTheListener()
        {
            // Without this the listener keeps serving plain HTTP while every published
            // URL switches to https:// - handing out addresses that cannot connect,
            // each one looking perfectly correct.
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();
            Assert.False(coordinator.Status.Https);

            _config.Current.HttpBridge.UseHttps = true;
            _config.Save();

            Assert.Equal(2, _endpoint.StartCount);
            Assert.True(coordinator.Status.Https);
        }

        [Fact]
        public void ChangingTheCertificate_RebindsTheListener()
        {
            // Pointing at a different certificate is not something the running listener
            // picks up: the per-connection reload re-reads the SAME source.
            _config.Current.HttpBridge.UseHttps = true;
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            _config.Current.HttpBridge.CertificatePath = @"C:\certs\fullchain.pem";
            _config.Current.HttpBridge.CertificateKeyPath = @"C:\certs\privkey.pem";
            _config.Save();

            Assert.Equal(2, _endpoint.StartCount);
        }

        [Fact]
        public void ARenewalDoesNotRebindTheListener()
        {
            // The paths do not change when a certificate renews - the files behind them
            // do, and the endpoint re-reads those per connection. Restarting would drop
            // live clients every sixty days for no reason.
            _config.Current.HttpBridge.UseHttps = true;
            _config.Current.HttpBridge.CertificatePath = @"C:\certs\fullchain.pem";
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            _config.Save();   // some unrelated change

            Assert.Equal(1, _endpoint.StartCount);
            Assert.Equal(0, _endpoint.StopCount);
        }

        [Fact]
        public void AListenerServingHttp_IsNotReportedAsEncrypted()
        {
            // The status says what is being SERVED, not what was asked for. Anything
            // else lets a failed upgrade publish https:// URLs.
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            Assert.True(coordinator.Status.Running);
            Assert.False(coordinator.Status.Https);
            Assert.DoesNotContain("HTTPS", coordinator.Status.Summary);
        }

        // --- Starting and stopping -------------------------------------------------

        [Fact]
        public void Disabled_DoesNotStartTheListener()
        {
            using var coordinator = Coordinator();

            coordinator.ApplyConfiguration();

            Assert.Equal(0, _endpoint.StartCount);
            Assert.False(coordinator.Status.Running);
            Assert.Equal("Off", coordinator.Status.Summary);
        }

        [Fact]
        public void EnablingAtRuntime_StartsTheListenerWithoutARestart()
        {
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            _config.SetEndpointEnabled(true);   // raises ConfigurationChanged

            Assert.Equal(1, _endpoint.StartCount);
            Assert.True(coordinator.Status.Running);
            Assert.True(coordinator.Status.Enabled);
        }

        [Fact]
        public void DisablingAtRuntime_StopsTheListener()
        {
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            _config.SetEndpointEnabled(false);

            Assert.Equal(1, _endpoint.StopCount);
            Assert.False(_endpoint.IsRunning);
            Assert.Equal("Off", coordinator.Status.Summary);
        }

        [Fact]
        public void ChangingThePort_RebindsOnTheNewPort()
        {
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            _config.SetEndpointPort(60123);

            Assert.Equal(new[] { 55555, 60123 }, _endpoint.StartedPorts);
            Assert.Equal(1, _endpoint.StopCount);
            Assert.Equal(60123, coordinator.Status.Port);
        }

        [Fact]
        public void ChangingTheAuthMode_Rebinds()
        {
            // The scheme is fixed on the listener when it binds, so it cannot change
            // underneath a running one.
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            _config.SetEndpointAuthMode(BridgeAuthMode.Anonymous);

            Assert.Equal(2, _endpoint.StartCount);
            Assert.Equal(BridgeAuthMode.Anonymous, coordinator.Status.AuthMode);
        }

        // --- What must NOT restart it ----------------------------------------------

        [Fact]
        public void AnUnrelatedConfigurationChange_LeavesTheListenerAlone()
        {
            // ConfigurationChanged fires for every serve, rule edit and policy change.
            // Bouncing the endpoint on those would drop live client connections for
            // nothing — and serving is exactly when clients are connected.
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            _config.SetOnDetection("Sample01", OnDetectionPolicy.ServeImmediately);
            _config.SetStableAlias("Sample01", "Sales");
            _config.SetMinimizeToTray(true);

            Assert.Equal(1, _endpoint.StartCount);
            Assert.Equal(0, _endpoint.StopCount);
            Assert.True(_endpoint.IsRunning);
        }

        [Fact]
        public void ChangingTheHostname_DoesNotInterruptTheListener()
        {
            // Hostname only shapes the URLs shown to users. Restarting for it would
            // disconnect clients over a display setting.
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            _config.SetEndpointHostname("nas.local");

            Assert.Equal(1, _endpoint.StartCount);
            Assert.Equal(0, _endpoint.StopCount);
        }

        [Fact]
        public void ReapplyingUnchangedSettings_IsANoOp()
        {
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            coordinator.ApplyConfiguration();
            coordinator.ApplyConfiguration();

            Assert.Equal(1, _endpoint.StartCount);
        }

        // --- Failure ----------------------------------------------------------------

        [Fact]
        public void ABindFailure_BecomesStatusRatherThanAnException()
        {
            _config.SetEndpointEnabled(true);
            _endpoint.FailWith = new InvalidOperationException("port already in use");
            using var coordinator = Coordinator();

            coordinator.ApplyConfiguration();

            Assert.True(coordinator.Status.Enabled);
            Assert.False(coordinator.Status.Running);
            Assert.Contains("port already in use", coordinator.Status.Summary);
        }

        [Fact]
        public void AfterAFailure_UnrelatedChangesDoNotRetry_ButRestartDoes()
        {
            // Retrying a failed bind on every serve would spam the log and the user.
            // Restart() is the deliberate way back.
            _config.SetEndpointEnabled(true);
            _endpoint.FailWith = new InvalidOperationException("port already in use");
            using var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            _config.SetOnDetection("Sample01", OnDetectionPolicy.ServeImmediately);
            Assert.Equal(1, _endpoint.StartCount);

            _endpoint.FailWith = null;
            coordinator.Restart();

            Assert.Equal(2, _endpoint.StartCount);
            Assert.True(coordinator.Status.Running);
        }

        // --- Status -----------------------------------------------------------------

        [Fact]
        public void StatusChanged_FiresWithEachAppliedState()
        {
            using var coordinator = Coordinator();
            var seen = new List<string>();
            coordinator.StatusChanged += (_, status) => seen.Add(status.Summary);

            coordinator.ApplyConfiguration();      // off
            _config.SetEndpointEnabled(true);      // running

            Assert.Equal(
                new[] { "Off", $"Running on port 55555 ({BridgeAuthModeLabel.For(BridgeAuthMode.Basic)})" },
                seen);
        }

        [Fact]
        public void ARunningEndpointIsReachable_BecauseThereIsNoFallbackLeft()
        {
            // The old failure was silent: without a URL ACL the endpoint ran but only
            // this machine could reach it, and nothing in Excel said why. Kestrel binds
            // every address without a reservation, so running now means reachable and
            // the status line has no restricted case left to name (#132).
            _config.SetEndpointEnabled(true);
            using var coordinator = Coordinator();

            coordinator.ApplyConfiguration();

            Assert.True(coordinator.Status.Running);
            Assert.True(coordinator.Status.IsLanReachable);
            Assert.DoesNotContain("this machine only", coordinator.Status.Summary);
        }

        [Fact]
        public void AnonymousIsFlaggedAsUnauthenticated()
        {
            _config.SetEndpointEnabled(true);
            _config.SetEndpointAuthMode(BridgeAuthMode.Anonymous);
            using var coordinator = Coordinator();

            coordinator.ApplyConfiguration();

            Assert.True(coordinator.Status.IsUnauthenticated);
        }

        [Fact]
        public void Dispose_StopsTheListenerAndStopsFollowingConfiguration()
        {
            _config.SetEndpointEnabled(true);
            var coordinator = Coordinator();
            coordinator.ApplyConfiguration();

            coordinator.Dispose();
            Assert.Equal(1, _endpoint.StopCount);

            _config.SetEndpointPort(60123);
            Assert.Equal(1, _endpoint.StartCount);   // no resurrection after disposal
        }

        // --- Settings persistence ----------------------------------------------------

        [Fact]
        public void EndpointSettings_PersistAcrossAReload()
        {
            _config.SetEndpointEnabled(true);
            _config.SetEndpointAuthMode(BridgeAuthMode.Anonymous);
            _config.SetEndpointHostname("  nas.local  ");
            Assert.True(_config.SetEndpointPort(60123).IsValid);

            var reloaded = new ConfigService(new ConfigurationManager(_tempDir));
            reloaded.Load();

            Assert.True(reloaded.Current.HttpBridge.Enabled);
            Assert.Equal(60123, reloaded.Current.HttpBridge.Port);
            Assert.Equal(BridgeAuthMode.Anonymous, reloaded.Current.HttpBridge.AuthMode);
            Assert.Equal("nas.local", reloaded.Current.HttpBridge.Hostname);   // trimmed
        }

        [Theory]
        [InlineData(0)]
        [InlineData(80)]
        [InlineData(1023)]
        [InlineData(65536)]
        public void APortOutsideTheUnprivilegedRange_IsRejectedRatherThanSaved(int port)
        {
            var (isValid, message) = _config.SetEndpointPort(port);

            Assert.False(isValid);
            Assert.NotEmpty(message);
            Assert.Equal(55555, _config.Current.HttpBridge.Port);   // unchanged
        }
    }
}
