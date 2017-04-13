using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using Newtonsoft.Json;

namespace EventLogStream
{
    public class EventLogStream : IDisposable
    {
        //
        // Summary:
        //     EventLog name.
        public string LogName { get; }
        //
        // Summary:
        //     Current EventLog.
        private EventLog log;
        //
        // Summary:
        //     Lock object for reading events from the EventLogEntryCollection.
        private Object logLock = new Object();
        //
        // Summary:
        //     Stream all events from the log, including past events.
        private bool streamAll = true;
        //
        // Summary:
        //     TextWriter for EventLogEntries.
        private TextWriter stdout;
        //
        // Summary:
        //     TextWriter for any errors encountered.
        private TextWriter stderr;
        //
        // Summary:
        //     EventLogStream run state constants.
        private const Int32 stateStopped = 0;
        private const Int32 stateRunning = 1;
        private const Int32 stateDisposed = 2;
        //
        // Summary:
        //     EventLogStream run state, used to prevent multiple calls to Start().
        private Int32 state = stateStopped;

        //
        // Summary:
        //     Marshals EventLogEntry e into UTF-8 encoded JSON.
        //
        // Returns:
        //     The UTF-8 encoded JSON representaion of EventLogEntry e.
        private string ToJson(EventLogEntry e)
        {

            using (Utf8StringWriter sw = new Utf8StringWriter())
            {
                using (JsonTextWriter w = new JsonTextWriter(sw))
                {
                    w.WriteStartObject();

                    // Event log name
                    w.WritePropertyName("LogName");
                    w.WriteValue(LogName);

                    // EventLogEntry properties

                    // Looking up the category is expensive, skip it
                    // if the CategoryNumber is 0.
                    w.WritePropertyName("Category");
                    if (e.CategoryNumber == 0)
                    {
                        w.WriteValue("(0)");
                    }
                    else
                    {
                        w.WriteValue(e.Category);
                    }
                    w.WritePropertyName("CategoryNumber");
                    w.WriteValue(e.CategoryNumber);

                    if (e.Data != null && e.Data.Length != 0)
                    {
                        w.WritePropertyName("Data");
                        w.WriteValue(e.Data);
                    }

                    w.WritePropertyName("EntryType");
                    w.WriteValue(e.EntryType.ToString());

                    // EventID is deprecated, but include it anyway.
#pragma warning disable CS0618 // Type or member is obsolete
                    w.WritePropertyName("EventID");
                    w.WriteValue(e.EventID);
#pragma warning restore CS0618 // Type or member is obsolete

                    w.WritePropertyName("Index");
                    w.WriteValue(e.Index);
                    w.WritePropertyName("InstanceId");
                    w.WriteValue(e.InstanceId);
                    w.WritePropertyName("MachineName");
                    w.WriteValue(e.MachineName);
                    w.WritePropertyName("Message");
                    w.WriteValue(e.Message);
                    w.WritePropertyName("Source");
                    w.WriteValue(e.Source);
                    w.WritePropertyName("TimeGenerated");
                    w.WriteValue(e.TimeGenerated);
                    w.WritePropertyName("TimeWritten");
                    w.WriteValue(e.TimeWritten);

                    if (e.UserName != null && e.UserName.Length != 0)
                    {
                        w.WritePropertyName("UserName");
                        w.WriteValue(e.UserName);
                    }
                    w.WriteEndObject();

                    return sw.ToString();
                }
            }
        }

        private void StreamEvents()
        {
            try
            {
                lock (logLock)
                {
                    foreach (EventLogEntry entry in log.Entries)
                    {
                        stdout.WriteLine(ToJson(entry));
                    }
                }
            }
            catch (Exception e)
            {
                stderr.WriteLine("Error (StreamEvents): " + e.Message);
            }
        }

        private void Callback(Object o, EntryWrittenEventArgs a)
        {
            // The EventLog.EntryWritten is only triggered if the last write
            // event occured at least six seconds previously.  So we don't
            // actually use the Entry, it only serves as a signal that writes 
            // occured, but we should dispose of it regardless.
            using (a.Entry)
            {
                try
                {
                    StreamEvents();
                }
                catch (Exception e)
                {
                    stderr.WriteLine("Error (Callback): " + e.Message);
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && state != stateDisposed && log != null)
            {
                log.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            // This class has no finalizer, just following the pattern...
            GC.SuppressFinalize(this);
        }

        public void Start()
        {
            // Allow only one call to start.
            if (Interlocked.CompareExchange(ref state, stateRunning, stateStopped) == stateStopped)
            {
                if (streamAll)
                {
                    StreamEvents();
                }
                // Register the event handler.
                log.EntryWritten += new EntryWrittenEventHandler(Callback);
                log.EnableRaisingEvents = true;
            }
            else
            {
                throw new Exception("EventLogStream: multiple calls to Start().");
            }
        }

        public EventLogStream(string LogName, bool StreamAllEvents = true)
        {

            this.LogName = LogName;
            log = new EventLog(LogName);

            // This will trigger an exception if the log does not exist.
            // Otherwise we don't get an exception until Start(), which
            // occurs in a Task.
            int count = log.Entries.Count;

            streamAll = StreamAllEvents;

            // TODO (CEV): Make streams configurable.
            stdout = Console.Out;
            stderr = Console.Error;
        }
    }
}
