using System.Xml.Linq;
using PBIRelay.Services;
using Xunit;

namespace PBIRelay.Core.Tests
{
    public class XmlaCommandClassifierTests
    {
        private static XDocument Execute(string commandBody) => XDocument.Parse(
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
            "<Execute xmlns=\"urn:schemas-microsoft-com:xml-analysis\">" +
            $"<Command>{commandBody}</Command>" +
            "<Properties><PropertyList><Catalog>Sales</Catalog></PropertyList></Properties>" +
            "</Execute></soap:Body></soap:Envelope>");

        [Theory]
        [InlineData("EVALUATE 'Sales'")]
        [InlineData("DEFINE MEASURE 'Sales'[X] = 1 EVALUATE 'Sales'")]
        [InlineData("SELECT {[Measures].[Amount]} ON 0 FROM [Model]")]
        [InlineData("WITH MEMBER [Measures].[X] AS 1 SELECT {[Measures].[X]} ON 0 FROM [Model]")]
        [InlineData("DRILLTHROUGH SELECT FROM [Model]")]
        public void Queries_are_reads(string statement)
        {
            Assert.False(XmlaCommandClassifier.Mutates(Execute($"<Statement>{statement}</Statement>"), out _));
        }

        [Fact]
        public void Session_scoped_mdx_is_a_read_so_excel_keeps_working()
        {
            // Calculated members die with the session and never reach the model, so a
            // read-only model must still accept them or Excel PivotTables break.
            var doc = Execute("<Statement>CREATE SESSION CUBE [X] FROM [Model]</Statement>");
            Assert.False(XmlaCommandClassifier.Mutates(doc, out _));
        }

        [Fact]
        public void A_discover_nested_in_a_batch_is_a_read()
        {
            // Exactly the shape Tabular Editor reads model state with. A Discover
            // arriving as the body's own verb never reaches the classifier, so refusing
            // a nested one contradicted the surrounding code too.
            var doc = Execute("<Batch><Discover><RequestType>DISCOVER_CSDL_METADATA</RequestType></Discover></Batch>");
            Assert.False(XmlaCommandClassifier.Mutates(doc, out _));
        }

        [Fact]
        public void A_bare_discover_command_is_a_read()
        {
            var doc = Execute("<Discover><RequestType>DBSCHEMA_CATALOGS</RequestType></Discover>");
            Assert.False(XmlaCommandClassifier.Mutates(doc, out _));
        }

        [Fact]
        public void A_batch_mixing_a_discover_with_a_write_is_still_refused()
        {
            var doc = Execute(
                "<Batch><Discover><RequestType>DBSCHEMA_CATALOGS</RequestType></Discover>" +
                "<Alter><Object /></Alter></Batch>");
            Assert.True(XmlaCommandClassifier.Mutates(doc, out string what));
            Assert.Equal("Batch > Alter", what);
        }

        [Theory]
        [InlineData("BeginTransaction")]
        [InlineData("CommitTransaction")]
        [InlineData("RollbackTransaction")]
        public void Transaction_control_cannot_itself_change_anything(string command)
        {
            Assert.False(XmlaCommandClassifier.Mutates(Execute($"<{command} />"), out _));
        }

        [Fact]
        public void A_transaction_does_not_smuggle_a_write_past_the_gate()
        {
            // The reason transaction control is safe to allow: what it wraps is still
            // judged on its own, so there is never anything committed that was refused.
            var doc = Execute("<BeginTransaction /><Alter><Object /></Alter><CommitTransaction />");
            Assert.True(XmlaCommandClassifier.Mutates(doc, out string what));
            Assert.Equal("Alter", what);
        }

        [Fact]
        public void Cancel_is_a_read()
        {
            Assert.False(XmlaCommandClassifier.Mutates(Execute("<Cancel><ConnectionID>7</ConnectionID></Cancel>"), out _));
        }

        [Theory]
        [InlineData("Alter")]
        [InlineData("Create")]
        [InlineData("Delete")]
        [InlineData("Drop")]
        [InlineData("Process")]
        [InlineData("Backup")]
        [InlineData("Restore")]
        [InlineData("Attach")]
        [InlineData("Detach")]
        [InlineData("Synchronize")]
        [InlineData("MergePartitions")]
        [InlineData("Insert")]
        [InlineData("Update")]
        public void Asl_commands_mutate(string command)
        {
            Assert.True(XmlaCommandClassifier.Mutates(Execute($"<{command}><Object /></{command}>"), out string what));
            Assert.Equal(command, what);
        }

        // --- Containers are judged by their contents, not by being containers ------
        //
        // Refusing every Batch shipped as a regression in #129: Tabular Editor reads a
        // model's state through one, so a read-only model could not be opened at all.

        [Fact]
        public void A_batch_of_reads_is_a_read()
        {
            var doc = Execute("<Batch><Statement>EVALUATE 'Sales'</Statement></Batch>");
            Assert.False(XmlaCommandClassifier.Mutates(doc, out _));
        }

        [Fact]
        public void A_batch_containing_a_write_is_refused_and_names_what_inside_it()
        {
            var doc = Execute("<Batch><Alter><Object /></Alter></Batch>");
            Assert.True(XmlaCommandClassifier.Mutates(doc, out string what));
            Assert.Equal("Batch > Alter", what);
        }

        [Fact]
        public void A_write_nested_two_containers_deep_is_still_found()
        {
            var doc = Execute("<Batch><Parallel><Process><Object /></Process></Parallel></Batch>");
            Assert.True(XmlaCommandClassifier.Mutates(doc, out string what));
            Assert.Equal("Batch > Parallel > Process", what);
        }

        [Fact]
        public void A_batch_is_refused_if_any_of_its_commands_writes()
        {
            var doc = Execute(
                "<Batch><Statement>EVALUATE 'Sales'</Statement><Delete><Object /></Delete></Batch>");
            Assert.True(XmlaCommandClassifier.Mutates(doc, out string what));
            Assert.Equal("Batch > Delete", what);
        }

        [Fact]
        public void Tmsl_hidden_inside_a_batch_is_still_a_write()
        {
            var doc = Execute("<Batch><Statement>{ \"refresh\": {} }</Statement></Batch>");
            Assert.True(XmlaCommandClassifier.Mutates(doc, out string what));
            Assert.Equal("Batch > Statement carrying TMSL", what);
        }

        [Fact]
        public void An_empty_batch_does_nothing_so_there_is_nothing_to_refuse()
        {
            Assert.False(XmlaCommandClassifier.Mutates(Execute("<Batch />"), out _));
        }

        [Fact]
        public void A_command_nobody_has_heard_of_mutates_because_the_list_allows_not_denies()
        {
            // The point of an allow list: a verb added to XMLA after this was written
            // must not slip through a gate whose whole job is to stop it.
            Assert.True(XmlaCommandClassifier.Mutates(Execute("<Rematerialize><Object /></Rematerialize>"), out _));
        }

        [Theory]
        [InlineData("{ \"createOrReplace\": { \"object\": {} } }")]
        [InlineData("{ \"refresh\": { \"type\": \"full\" } }")]
        [InlineData("  \n\t{ \"delete\": { \"object\": {} } }")]
        [InlineData("[ { \"refresh\": {} } ]")]
        public void Tmsl_in_a_statement_mutates(string tmsl)
        {
            // This is how Tabular Editor writes. A gate that waved Statement through
            // would allow exactly the writes it promised to stop.
            Assert.True(XmlaCommandClassifier.Mutates(Execute($"<Statement>{tmsl}</Statement>"), out string what));
            Assert.Equal("Statement carrying TMSL", what);
        }

        [Fact]
        public void Statement_carrying_tmsl_after_a_byte_order_mark_still_mutates()
        {
            var doc = Execute("<Statement>﻿{ \"refresh\": {} }</Statement>");
            Assert.True(XmlaCommandClassifier.Mutates(doc, out _));
        }

        [Fact]
        public void Every_command_is_checked_not_only_the_first()
        {
            var doc = Execute("<Statement>EVALUATE 'Sales'</Statement><Alter><Object /></Alter>");
            Assert.True(XmlaCommandClassifier.Mutates(doc, out string what));
            Assert.Equal("Alter", what);
        }

        [Fact]
        public void An_execute_with_no_command_cannot_be_shown_to_be_a_read()
        {
            var doc = XDocument.Parse(
                "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
                "<Execute xmlns=\"urn:schemas-microsoft-com:xml-analysis\" /></soap:Body></soap:Envelope>");
            Assert.True(XmlaCommandClassifier.Mutates(doc, out _));
        }

        [Fact]
        public void An_empty_command_cannot_be_shown_to_be_a_read()
        {
            Assert.True(XmlaCommandClassifier.Mutates(Execute(""), out _));
        }
    }
}
