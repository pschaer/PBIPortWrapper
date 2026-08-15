using System;
using System.IO;
using PBIRelay.Models;
using PBIRelay.Services;
using Xunit;

namespace PBIRelay.Core.Tests
{
    /// <summary>
    /// Covers ConfigService.SetOnDetection: persisting a model's OnDetection policy
    /// while keeping the legacy AutoConnect flag consistent, so the forward and serve
    /// paths never fight over a port. (The serve-on-detection decision itself now
    /// lives in <see cref="ServeLifecycleMachine"/>; see ServeLifecycleMachineTests.)
    /// </summary>
    public sealed class AutoServeTests : IDisposable
    {
        private readonly string _tempDir;

        public AutoServeTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "pbipw-autoserve-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Theory]
        [InlineData(OnDetectionPolicy.ServeImmediately, false)]
        [InlineData(OnDetectionPolicy.ServeAfterGrace, false)]
        [InlineData(OnDetectionPolicy.DoNothing, false)]
        public void SetOnDetection_keeps_AutoConnect_consistent(OnDetectionPolicy policy, bool expectedAutoConnect)
        {
            var svc = new ConfigService(new ConfigurationManager(_tempDir));
            svc.Load();

            svc.SetOnDetection("Sales", policy);

            var reloaded = new ConfigService(new ConfigurationManager(_tempDir));
            reloaded.Load();
            var rule = Assert.Single(reloaded.Current.Models);
            Assert.Equal(policy, rule.OnDetection);
            Assert.Equal(expectedAutoConnect, rule.AutoConnect);
        }

        [Fact]
        public void SetOnDetection_ignores_Untitled()
        {
            var svc = new ConfigService(new ConfigurationManager(_tempDir));
            svc.Load();

            svc.SetOnDetection("Untitled", OnDetectionPolicy.ServeImmediately);

            Assert.Empty(svc.Current.Models);
        }
    }
}
