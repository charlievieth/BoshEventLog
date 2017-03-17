using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json;


namespace EventLogStream
{

    class Program
    {
        //
        // Summary:
        //     Shutdown event used for sleeping the main thread.
        static ManualResetEvent shutdownEvent = new ManualResetEvent(false);
        //
        // Summary:
        //     Lock object for EventStreams.
        static Object eventLock = new Object();
        //
        // Summary:
        //     List of event log names to stream/read.
        static List<string> LogNames = new List<string>();
        //
        // Summary:
        //     Dictionary of EventLogStreams being streamed/read.
        static Dictionary<string, EventLogStream> EventStreams =
            new Dictionary<string, EventLogStream>();
        //
        // Summary:
        //     Stream all events from the log, including past events.
        static bool streamAll = false;
        //
        // Summary:
        //     Exit after reading the existing logs, do not wait for 
        //     and stream new events.
        static bool exitAfterReading = false;
        //
        // Summary:
        //     Valid command line arguments.
        static string[] argumentNames = new string[] { "all", "exit", "help" };
        //
        // Summary:
        //     Command usage message.
        const string usageMsg = @"
Usage EventLog [OPTIONS] LOG_NAMES

  Stream event logs LOG_NAMES formatted as UTF-8 JSON to stdout.

Example:
  EventLog -all ""System"" ""Application""

Options:
  --all   Include all past events
  --exit  Print contents of each event log then exit (implies --all)
  --help  Display this message and exit
";

        //
        // Summary:
        //     Prints usage message and exits with code.
        static void Usage(int code)
        {
            Console.Error.WriteLine(usageMsg);
            System.Environment.Exit(code);
        }
        //
        // Summary:
        //     Cleans command line argument s.
        //
        // Returns:
        //     Argument s converted to lowercase and stripped of 
        //     any '--', '-' or '/' prefix.
        static string ArgumentName(string s)
        {
            if (s.Length > 0)
            {
                if (s.StartsWith("--"))
                {
                    return s.Substring(2).ToLower();
                }
                if (s[0] == '-' || s[0] == '/')
                {
                    return s.Substring(1).ToLower();
                }
            }
            return "";
        }
        //
        // Summary:
        //     Determines if command line argument s is a 'help' argument.
        //
        // Returns:
        //     If command line argument s is a 'help' argument.
        static bool IsHelpArgument(string s)
        {
            return ArgumentName(s) == "help";
        }
        //
        // Summary:
        //     Determines if command line argument s is valid.
        //
        // Returns:
        //     If command line argument s is valid.
        static bool ValidArgument(string s)
        {
            return argumentNames.Contains(ArgumentName(s));
        }
        //
        // Summary:
        //     Parses command line arguments args.  If an invalid or 'help' flag
        //     is specified Usage() is be called and the program will exit.
        static void ParseArgs(string[] args)
        {
            bool optionsComplete = false;
            foreach (string arg in args.Select(s => s.Trim()))
            {
                if (arg == "")
                {
                    Console.Error.WriteLine("Ignoring empty argument");
                    continue;
                }
                if (optionsComplete && arg.StartsWith("-"))
                {
                    // Allow "help" to be located anywhere.
                    if (IsHelpArgument(arg))
                    {
                        Usage(0);
                    }
                    if (ValidArgument(arg))
                    {
                        Console.Error.WriteLine("Invalid location for argument: " + arg);
                    }
                    else
                    {
                        Console.Error.WriteLine("Invalid argument: " + arg);
                    }
                    Usage(1);
                }
                // Like getopt, but without the terror of char arrays!!!
                switch (arg.ToLower())
                {
                    case "/help":
                    case "-help":
                    case "--help":
                        Usage(0);
                        break;
                    case "/all":
                    case "-all":
                    case "--all":
                        streamAll = true;
                        break;
                    case "/exit":
                    case "-exit":
                    case "--exit":
                        streamAll = true;
                        exitAfterReading = true;
                        break;
                    default:
                        if (arg.StartsWith("-") || arg.StartsWith("/"))
                        {
                            Console.Error.WriteLine("Invalid argument: " + arg);
                            Usage(1);
                        }
                        if (LogNames.Exists(s => s.ToLower() == arg.ToLower()))
                        {
                            Console.Error.WriteLine("Ignoring duplicate Event Log: " + arg);
                        }
                        else
                        {
                            LogNames.Add(arg);
                        }
                        optionsComplete = true;
                        break;
                }
            }
            if (LogNames.Count == 0)
            {
                Console.Error.WriteLine("No Event Logs specified.");
                Usage(1);
            }
        }

        static void DisposeEventStreams(bool signalShutdownEvent)
        {
            // Lock in case this was triggered by the Exit handler
            lock (eventLock)
            {
                foreach (EventLogStream log in EventStreams.Values)
                {
                    try
                    {
                        log.Dispose();
                    }
                    catch (Exception e)
                    {
                        // We are exiting anyway no need to re-throw.
                        Console.Error.WriteLine("Error disposing of EventLogStream ({0}): {1}",
                            log.LogName, e.Message);
                    }
                }
                EventStreams.Clear();
                if (signalShutdownEvent)
                {
                    shutdownEvent.Set();
                }
            }
        }

        protected static void ExitHandler(object sender, ConsoleCancelEventArgs args)
        {
            Console.Error.WriteLine("Recieved interupt ({0}): cleaning up now", args.SpecialKey);
            DisposeEventStreams(true);
            Console.Error.WriteLine("Cleanup complete - exiting now");
        }

        static void Main(string[] args)
        {
            // Immediately set output encoding
            Console.OutputEncoding = Encoding.UTF8;
            Console.CancelKeyPress += new ConsoleCancelEventHandler(ExitHandler);

            // Parse command line arguments
            ParseArgs(args);

            List<Task> tasks = new List<Task>();
            try
            {
                foreach (string name in LogNames)
                {
                    Console.WriteLine(name);
                    EventLogStream log = new EventLogStream(name, streamAll);
                    EventStreams.Add(name, log);
                    tasks.Add(Task.Factory.StartNew(() => { log.Start(); }));
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error initiating Event Logs: " + e.Message);
                System.Environment.Exit(2);
            }

            try
            {
                Task.WaitAll(tasks.ToArray());
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error running EventLogStream tasks: " + e.Message);
                System.Environment.Exit(2);
            }

            if (exitAfterReading)
            {
                DisposeEventStreams(false);
                return;
            }

            // Wait for shutdown event, in any...
            shutdownEvent.WaitOne();
        }
    }
}
