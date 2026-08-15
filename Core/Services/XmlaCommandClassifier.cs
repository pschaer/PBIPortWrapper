using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PBIRelay.Services
{
    /// <summary>
    /// Decides whether an XMLA <c>Execute</c> only reads, so that a model marked
    /// read-only can refuse the rest (#129).
    ///
    /// This reads the envelope and never rewrites it, which keeps the relay out of
    /// translator territory: the request either goes to the engine exactly as the
    /// client wrote it, or it does not go at all.
    ///
    /// The command list is an ALLOW list. A deny list of the mutating verbs fails open
    /// — anything Microsoft adds later, or anything simply not thought of, would pass
    /// a gate whose entire purpose is to stop it. An allow list fails closed, and the
    /// set of commands a reader needs is small and stable.
    /// </summary>
    public static class XmlaCommandClassifier
    {
        /// <summary>
        /// The commands a read-only client needs.
        ///
        /// <c>Statement</c> carries queries — and, in Tabular, TMSL scripts too, which is
        /// why its content is inspected below. <c>Cancel</c> aborts a running request and
        /// changes no model state.
        ///
        /// <c>Discover</c> is the XMLA read verb: it returns metadata and schema rowsets
        /// and has no form that writes. A Discover arriving as the body's own verb never
        /// reaches this class at all — the read-only check only runs for Execute — so
        /// refusing one nested inside an Execute contradicted the surrounding code as
        /// well as being wrong. Tabular Editor reads a model's state through exactly
        /// that shape, a Discover inside a Batch.
        /// </summary>
        private static readonly HashSet<string> ReadCommands =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Statement", "Cancel", "Discover",
                // Transaction control cannot itself change a model, and everything it
                // could wrap is judged on its own — a refused Alter stays refused whether
                // or not a transaction is open, so there is never anything to commit.
                // Added without having seen a client send one, deliberately: allowing a
                // command that provably cannot mutate can only ever prevent a false
                // refusal, never cause one. This list stays a closed set of commands
                // shown to be reads, not a growing list of things that broke.
                "BeginTransaction", "CommitTransaction", "RollbackTransaction"
            };

        /// <summary>
        /// Commands that only hold other commands. They carry no meaning of their own,
        /// so they are read exactly when everything inside them is.
        ///
        /// Judging a container whole was wrong, and shipped as a regression: Tabular
        /// Editor reads a model's state through a <c>Batch</c>, so refusing every Batch
        /// stopped it opening a read-only model at all. "Deciding what is inside it is
        /// the same problem one level down" was the reasoning — and being the same
        /// problem is precisely why recursing answers it.
        /// </summary>
        private static readonly HashSet<string> Containers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Batch", "Sequence", "Parallel" };

        /// <summary>
        /// True when the envelope would change something, with <paramref name="what"/>
        /// naming it for the fault the client sees.
        /// </summary>
        public static bool Mutates(XDocument executeEnvelope, out string what)
        {
            what = null;

            XElement command = executeEnvelope?.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("Command", StringComparison.OrdinalIgnoreCase));

            // A well-formed Execute always carries a Command. Something claiming to be
            // one without it cannot be shown to be a read, so it is not treated as one.
            if (command == null)
            {
                what = "an Execute with no command";
                return true;
            }

            List<XElement> verbs = command.Elements().ToList();
            if (verbs.Count == 0)
            {
                what = "an empty command";
                return true;
            }

            foreach (XElement verb in verbs)
            {
                if (MutatingCommand(verb, null, out what)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether one command mutates, naming it by the path taken to reach it —
        /// <c>Batch &gt; Parallel &gt; Process</c> rather than bare <c>Process</c>. The
        /// path costs nothing and says exactly which part of a nested command was
        /// refused, in the fault the client shows and in the log.
        /// </summary>
        private static bool MutatingCommand(XElement verb, string prefix, out string what)
        {
            what = null;

            string name = verb.Name.LocalName;
            string path = prefix == null ? name : prefix + " > " + name;

            if (Containers.Contains(name))
            {
                foreach (XElement child in verb.Elements())
                {
                    if (MutatingCommand(child, path, out what)) return true;
                }

                // An empty container does nothing, so there is nothing to refuse.
                return false;
            }

            if (!ReadCommands.Contains(name))
            {
                what = path;
                return true;
            }

            if (name.Equals("Statement", StringComparison.OrdinalIgnoreCase) && IsTmsl(verb.Value))
            {
                // The one that matters in practice: Tabular Editor WRITES through a
                // Statement carrying a TMSL script, so a gate that waved Statement
                // through would allow exactly the writes it promised to stop.
                what = path + " carrying TMSL";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a Statement's body is a TMSL script rather than a query. TMSL is
        /// JSON and every command in it writes (createOrReplace, delete, refresh,
        /// alter, backup, restore, sequence …), so the shape alone is enough — no need
        /// to parse it, and no need to keep up with its verbs.
        ///
        /// DAX and MDX are left alone. DAX has no write syntax at all, and MDX's only
        /// one is cell writeback, which needs a writeback-enabled partition that a
        /// Power BI Desktop model does not have. Session-scoped MDX (CREATE SESSION
        /// CUBE, CREATE MEMBER) stays allowed deliberately: it is how Excel builds
        /// calculated members, it dies with the session, and it never reaches the
        /// model.
        /// </summary>
        private static bool IsTmsl(string statement)
        {
            if (string.IsNullOrWhiteSpace(statement)) return false;

            char first = statement.TrimStart('﻿', ' ', '\t', '\r', '\n')
                                  .FirstOrDefault();
            return first == '{' || first == '[';
        }
    }
}
