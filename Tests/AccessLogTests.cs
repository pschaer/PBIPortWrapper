using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PBIRelay.Models;
using PBIRelay.Services;
using Xunit;

namespace PBIRelay.Core.Tests
{
    public class AccessLogFormatTests
    {
        private static AccessLogEntry Entry() => new AccessLogEntry
        {
            Timestamp = new DateTime(2026, 7, 27, 14, 5, 9),
            Caller = "PASCAL",
            RemoteAddress = "10.9.20.21",
            Client = "MSOLAP 17.0 Client",
            Model = "Sample01",
            Verb = "Discover",
            Detail = "MDSCHEMA_CUBES",
            Outcome = "ok",
            DurationMs = 42
        };

        [Fact]
        public void A_line_carries_every_column_in_header_order()
        {
            Assert.Equal(
                "2026-07-27 14:05:09,PASCAL,10.9.20.21,MSOLAP 17.0 Client,Sample01,Discover,MDSCHEMA_CUBES,ok,42",
                AccessLogFormat.Line(Entry()));
        }

        [Fact]
        public void The_header_names_as_many_columns_as_a_line_has()
        {
            Assert.Equal(
                AccessLogFormat.Header.Split(',').Length,
                AccessLogFormat.Line(Entry()).Split(',').Length);
        }

        [Fact]
        public void A_field_containing_a_comma_is_quoted_so_later_columns_do_not_shift()
        {
            // User-agents arrive from the network and routinely contain commas. A log
            // that silently shifts every later column is worse than none, because it
            // still looks readable.
            var entry = Entry();
            entry.Client = "Tool/1.0 (Windows NT 10.0, x64)";

            string line = AccessLogFormat.Line(entry);

            Assert.Contains("\"Tool/1.0 (Windows NT 10.0, x64)\"", line);
            Assert.Equal(AccessLogFormat.Header.Split(',').Length, SplitCsv(line).Length);
        }

        [Fact]
        public void A_quote_is_doubled_the_way_csv_expects()
        {
            var entry = Entry();
            entry.Caller = "he said \"hi\"";

            Assert.Contains("\"he said \"\"hi\"\"\"", AccessLogFormat.Line(entry));
        }

        [Fact]
        public void A_newline_is_folded_so_one_request_stays_one_record()
        {
            var entry = Entry();
            entry.Client = "line one\r\nline two";

            string line = AccessLogFormat.Line(entry);

            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("\r", line);
        }

        [Fact]
        public void Empty_fields_do_not_collapse_columns()
        {
            var line = AccessLogFormat.Line(new AccessLogEntry { Timestamp = new DateTime(2026, 1, 1) });
            Assert.Equal(AccessLogFormat.Header.Split(',').Length, line.Split(',').Length);
        }

        /// <summary>Minimal CSV split that honours quoting, to prove the escaping works.</summary>
        private static string[] SplitCsv(string line)
        {
            var fields = new System.Collections.Generic.List<string>();
            bool quoted = false;
            var current = new System.Text.StringBuilder();
            foreach (char c in line)
            {
                if (c == '"') quoted = !quoted;
                else if (c == ',' && !quoted) { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }
    }

    public class AccessLogWriterTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "pbipw-accesslog-" + Guid.NewGuid().ToString("N"));

        private string Path_ => Path.Combine(_dir, "access.csv");

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }

        [Fact]
        public void The_first_write_creates_the_file_with_a_header()
        {
            new AccessLog(Path_).Write(new AccessLogEntry { Model = "Sales" });

            string[] lines = File.ReadAllLines(Path_);
            Assert.Equal(AccessLogFormat.Header, lines[0]);
            Assert.Contains("Sales", lines[1]);
        }

        [Fact]
        public void Later_writes_append_without_repeating_the_header()
        {
            var log = new AccessLog(Path_);
            log.Write(new AccessLogEntry { Model = "One" });
            log.Write(new AccessLogEntry { Model = "Two" });

            string[] lines = File.ReadAllLines(Path_);
            Assert.Equal(3, lines.Length);
            Assert.Single(lines.Where(l => l == AccessLogFormat.Header));
        }

        [Fact]
        public void It_rotates_once_it_gets_large_and_keeps_one_previous_file()
        {
            // Unbounded is not an option at ~50 lines per Excel session; more than one
            // generation would be archiving, which this is not.
            var log = new AccessLog(Path_, maxBytes: 200);
            for (int i = 0; i < 20; i++) log.Write(new AccessLogEntry { Model = "Model" + i });

            Assert.True(File.Exists(Path_));
            Assert.True(File.Exists(Path.Combine(_dir, "access.prev.csv")));
        }

        [Fact]
        public void A_log_that_cannot_be_written_says_so_once_not_once_per_request()
        {
            // The endpoint's job is to serve, not to keep a diary: a broken log must not
            // turn every request into another line about itself.
            int notices = 0;
            var log = new AccessLog(
                Path.Combine(_dir, "no:such|path", "access.csv"), onNotice: _ => notices++);

            log.Write(new AccessLogEntry());
            log.Write(new AccessLogEntry());
            log.Write(new AccessLogEntry());

            Assert.Equal(1, notices);
        }

        [Fact]
        public void Recording_resumes_by_itself_once_the_file_is_free_again()
        {
            // The usual reason a write fails is that someone opened the file in Excel,
            // which holds it until the window closes. An earlier version gave up for the
            // rest of the run, so looking at the log meant losing it.
            var notices = new System.Collections.Generic.List<string>();
            var log = new AccessLog(Path_, onNotice: notices.Add);

            log.Write(new AccessLogEntry { Model = "Before" });

            using (File.Open(Path_, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                log.Write(new AccessLogEntry { Model = "Locked out" });
                log.Write(new AccessLogEntry { Model = "Locked out" });
            }

            log.Write(new AccessLogEntry { Model = "After" });

            string written = File.ReadAllText(Path_);
            Assert.Contains("Before", written);
            Assert.Contains("After", written);          // recovered without a restart
            Assert.DoesNotContain("Locked out", written); // lost while held, as expected

            Assert.Equal(2, notices.Count);              // one outage, one recovery
            Assert.Contains("Excel", notices[0]);
            Assert.Contains("again", notices[1]);
        }
    }

    public class XmlaRequestSummaryTests
    {
        [Fact]
        public void A_discover_is_named_by_its_request_type()
        {
            var doc = XDocument.Parse(
                "<Envelope><Body><Discover><RequestType>MDSCHEMA_CUBES</RequestType></Discover></Body></Envelope>");
            Assert.Equal("MDSCHEMA_CUBES", XmlaRequestSummary.Describe(doc));
        }

        [Fact]
        public void An_execute_is_named_by_its_command()
        {
            var doc = XDocument.Parse(
                "<Envelope><Body><Execute><Command><Statement>EVALUATE 'Sales'</Statement></Command></Execute></Body></Envelope>");
            Assert.Equal("Statement", XmlaRequestSummary.Describe(doc));
        }

        [Fact]
        public void The_query_itself_never_reaches_the_summary()
        {
            // The access log has to stay safe to leave on, which means it must never
            // contain the data or the question asked of it.
            var doc = XDocument.Parse(
                "<Envelope><Body><Execute><Command><Statement>EVALUATE SECRETS</Statement></Command></Execute></Body></Envelope>");
            Assert.DoesNotContain("SECRET", XmlaRequestSummary.Describe(doc), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_malformed_envelope_is_still_a_request_that_happened()
        {
            Assert.Null(XmlaRequestSummary.ParseOrNull("<not xml"));
            Assert.Equal(string.Empty, XmlaRequestSummary.Describe(null));
        }

        [Theory]
        [InlineData("<soap:Envelope><soap:Body><soap:Fault /></soap:Body></soap:Envelope>", true)]
        [InlineData("<soap:Envelope><soap:Body><ExecuteResponse /></soap:Body></soap:Envelope>", false)]
        [InlineData(null, false)]
        public void A_fault_is_recognised_so_it_can_be_told_apart_from_success(string response, bool expected)
        {
            Assert.Equal(expected, XmlaRequestSummary.IsFault(response));
        }
    }

    public class AuthOutcomeVocabularyTests
    {
        [Fact]
        public void A_rejected_sign_in_is_an_access_event_and_is_recorded_in_full()
        {
            // Every failed attempt is kept here, individually, however many of them
            // there are. log.txt summarises repeats; this file does not, because it is
            // the record rather than the running commentary (#132).
            var rejected = new AccessLogEntry { Caller = "Pascal", Outcome = "unauthorized" };

            string line = AccessLogFormat.Line(rejected);
            Assert.Contains("unauthorized", line);
            Assert.Contains("Pascal", line);
        }
    }

    public class AccessLogConfigTests
    {
        [Fact]
        public void Access_logging_is_on_by_default()
        {
            // The point is to answer "who is using my models" without first turning a
            // debugging switch on and waiting for the question to happen again.
            Assert.True(new HttpBridgeConfig().AccessLog);
        }

        [Fact]
        public void Payload_logging_stays_off_by_default()
        {
            // The two are deliberately different: payloads contain query results.
            Assert.False(new HttpBridgeConfig().LogPayloads);
        }

        [Fact]
        public void A_config_written_before_access_logging_existed_loads_with_it_on()
        {
            var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<HttpBridgeConfig>(
                "{ \"Enabled\": true, \"Port\": 55555 }");

            Assert.True(restored.AccessLog);
        }
    }
}
