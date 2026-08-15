using System;
using System.IO;
using System.Text;
using PBIRelay.Models;

namespace PBIRelay.Services
{
    public interface IAccessLog
    {
        void Write(AccessLogEntry entry);
        string FilePath { get; }
    }

    /// <summary>
    /// Appends one CSV line per request to <c>access.csv</c>, next to <c>log.txt</c> (#128).
    ///
    /// A separate file rather than lines in log.txt: Excel sends around fifty requests
    /// per session, and log.txt is mirrored into the dashboard, so per-request entries
    /// there would bury everything else — which is exactly why the existing per-request
    /// line sits at Debug and only appears when payload logging is on. An operational
    /// log has to be readable without being turned on first.
    /// </summary>
    public class AccessLog : IAccessLog
    {
        private const long DefaultMaxBytes = 5 * 1024 * 1024;

        private readonly object _lock = new object();
        private readonly long _maxBytes;
        private readonly Action<string> _onNotice;
        private bool _reportedFailure;

        public string FilePath { get; }

        public AccessLog(string filePath, long maxBytes = DefaultMaxBytes, Action<string> onNotice = null)
        {
            FilePath = filePath;
            _maxBytes = maxBytes;
            _onNotice = onNotice;
        }

        public void Write(AccessLogEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(FilePath)) return;

            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    RotateIfLarge();

                    bool needsHeader = !File.Exists(FilePath) || new FileInfo(FilePath).Length == 0;
                    using (var writer = new StreamWriter(FilePath, append: true, encoding: new UTF8Encoding(true)))
                    {
                        if (needsHeader) writer.WriteLine(AccessLogFormat.Header);
                        writer.WriteLine(AccessLogFormat.Line(entry));
                    }

                    if (_reportedFailure)
                    {
                        _reportedFailure = false;
                        _onNotice?.Invoke("Access log is being written again.");
                    }
                }
                catch (Exception ex)
                {
                    // Every write is retried, because the usual reason one fails is
                    // that someone opened the file in Excel — which holds it for as
                    // long as the window is open and then lets go. Giving up for the
                    // rest of the run would turn looking at the log into losing it,
                    // which is what an earlier version of this did.
                    //
                    // Only the MESSAGE is rate-limited: one line per outage, not one
                    // per request, since the endpoint's job is to serve rather than to
                    // narrate its own diary.
                    if (!_reportedFailure)
                    {
                        _reportedFailure = true;
                        _onNotice?.Invoke(
                            $"Access log cannot be written and requests are not being recorded " +
                            $"({ex.Message}). If it is open in Excel, close it — recording resumes by itself.");
                    }
                }
            }
        }

        /// <summary>
        /// Keeps one previous file. Unbounded is not an option at fifty lines per Excel
        /// session, and more than one generation is archiving, which this is not.
        /// </summary>
        private void RotateIfLarge()
        {
            if (!File.Exists(FilePath)) return;
            if (new FileInfo(FilePath).Length < _maxBytes) return;

            string previous = Path.ChangeExtension(FilePath, ".prev.csv");
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(FilePath, previous);
        }
    }
}
