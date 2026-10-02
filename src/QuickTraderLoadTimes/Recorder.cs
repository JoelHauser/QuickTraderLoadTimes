using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EFT.Trading;
using EFT.UI;

namespace QuickTraderLoadTimes
{
    /// <summary>
    /// Owns the trader open being measured. Probes report into it; the plugin's Update drives it.
    /// Async continuations can finish off the main thread, so every write goes through Gate.
    /// </summary>
    internal static class Recorder
    {
        private static readonly object Gate = new object();

        private const int BaselineFrames = 180;
        private static readonly float[] Ring = new float[BaselineFrames];
        private static int _ringCount;
        private static int _ringPos;

        private static TraderSession _current;
        private static int _openCounter;
        private static readonly Dictionary<string, int> OpensPerTrader = new Dictionary<string, int>();

        private static string _csvPath;
        private static string _launchId;

        public static bool Active
        {
            get
            {
                lock (Gate) return _current != null;
            }
        }

        public static void Init(string pluginDir, string launchId)
        {
            _launchId = launchId;
            _csvPath = Path.Combine(pluginDir, "measurements.csv");
        }

        public static void Begin(TraderDealScreen screen, Trader trader)
        {
            QuickTraderLoadTimesPlugin.LogEnvironmentOnce();
            lock (Gate)
            {
                if (_current != null) End("next trader opened");

                Series baseline = new Series();
                for (int i = 0; i < _ringCount; i++) baseline.Add(Ring[i]);

                string id = trader?.Id ?? "?";
                OpensPerTrader.TryGetValue(id, out int n);
                OpensPerTrader[id] = ++n;
                _current = new TraderSession(screen, trader, ++_openCounter, n,
                    baseline.Percentile(50), baseline.Percentile(95));
            }
        }

        public static void Tick(float frameMs)
        {
            lock (Gate)
            {
                if (_current == null)
                {
                    // Only frames outside a trader open feed the baseline.
                    Ring[_ringPos] = frameMs;
                    _ringPos = (_ringPos + 1) % BaselineFrames;
                    _ringCount = Math.Min(_ringCount + 1, BaselineFrames);
                    return;
                }
                if (_current.Tick(frameMs, QuickTraderLoadTimesPlugin.MaxSessionSeconds.Value, QuickTraderLoadTimesPlugin.SettleSeconds.Value))
                {
                    End(null);
                }
            }
        }

        /// <summary>Runs <paramref name="add"/> against the open session, if there is one.</summary>
        public static void With(Action<TraderSession> add)
        {
            lock (Gate)
            {
                if (_current != null) add(_current);
            }
        }

        /// <summary>
        /// Times an async call from its start to the moment its Task completes, and credits the
        /// result to the session that was open when the call started (dropped if that one has
        /// since ended, so a slow call cannot leak into the next trader's numbers).
        /// </summary>
        public static void TrackTask(Task task, long start, Action<TraderSession, double> add)
        {
            if (task == null) return;
            TraderSession owner;
            lock (Gate) owner = _current;
            if (owner == null) return;

            void Done()
            {
                double ms = TraderSession.Elapsed(start, Stopwatch.GetTimestamp());
                lock (Gate)
                {
                    if (ReferenceEquals(owner, _current)) add(owner, ms);
                }
            }

            if (task.IsCompleted) Done();
            else task.ContinueWith(_ => Done(), TaskContinuationOptions.ExecuteSynchronously);
        }

        public static void EndIfOpen(string reason)
        {
            lock (Gate)
            {
                if (_current != null) End(reason);
            }
        }

        // Caller holds Gate.
        private static void End(string reason)
        {
            TraderSession s = _current;
            _current = null;
            if (reason != null) s.EndReason = reason;
            s.Finish();

            string label = QuickTraderLoadTimesPlugin.RunLabel.Value;
            bool cold = QuickTraderLoadTimesPlugin.ColdCacheActive;
            QuickTraderLoadTimesPlugin.Log.LogInfo("\n" + s.Report(label, _launchId, cold));

            try
            {
                List<KeyValuePair<string, string>> fields = s.CsvFields(label, _launchId, cold);
                string header = Csv.Row(fields.Select(f => f.Key));
                string row = Csv.Row(fields.Select(f => f.Value));
                WriteCsv(header, row);
            }
            catch (Exception e)
            {
                QuickTraderLoadTimesPlugin.Log.LogError("could not write " + _csvPath + ": " + e.Message);
            }
        }

        /// <summary>
        /// Appends a row. A file whose header differs (an older build's columns) is renamed aside
        /// rather than mixed, so every file holds rows that line up.
        /// </summary>
        private static void WriteCsv(string header, string row)
        {
            if (File.Exists(_csvPath))
            {
                string first;
                using (StreamReader r = new StreamReader(_csvPath)) first = r.ReadLine();
                if (first != header)
                {
                    string aside = Path.Combine(Path.GetDirectoryName(_csvPath),
                        "measurements-old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                    File.Move(_csvPath, aside);
                    QuickTraderLoadTimesPlugin.Log.LogInfo("columns changed; previous measurements moved to " + aside);
                }
            }
            if (!File.Exists(_csvPath)) File.WriteAllText(_csvPath, header + Environment.NewLine);
            File.AppendAllText(_csvPath, row + Environment.NewLine);
        }
    }
}
