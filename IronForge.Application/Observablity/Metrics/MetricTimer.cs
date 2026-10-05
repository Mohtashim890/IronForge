using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IronForge.Application.Observablity.Metrics
{
    public sealed class MetricTimer : IDisposable
    {
        private readonly Histogram<double> _histogram;
        private readonly KeyValuePair<string, object?>[] _tags;
        private readonly Stopwatch _stopwatch;

        private bool _disposed;

        private MetricTimer(
            Histogram<double> histogram,
            KeyValuePair<string, object?>[] tags)
        {
            _histogram = histogram;
            _tags = tags;
            _stopwatch = Stopwatch.StartNew();
        }

        public static MetricTimer Start(
            Histogram<double> histogram,
            params KeyValuePair<string, object?>[] tags)
        {
            return new MetricTimer(
                histogram,
                tags);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _stopwatch.Stop();

            _histogram.Record(
                _stopwatch.Elapsed.TotalMilliseconds,
                _tags);
        }
    }
}
