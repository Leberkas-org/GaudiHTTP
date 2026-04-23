using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Servus.Akka.Diagnostics;

namespace TurboHTTP.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class ServusMetricsSpec : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentBag<MetricMeasurement<long>> _longMeasurements = [];
    private readonly ConcurrentBag<MetricMeasurement<double>> _doubleMeasurements = [];

    public ServusMetricsSpec()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ServusMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) =>
                _longMeasurements.Add(new MetricMeasurement<long>(instrument.Name, measurement, tags)));

        _listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, _) =>
                _doubleMeasurements.Add(new MetricMeasurement<double>(instrument.Name, measurement, tags)));

        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Meter_should_have_correct_name()
    {
        Assert.Equal("Servus.Akka", ServusMetrics.Meter.Name);
        Assert.Equal("Servus.Akka", ServusMetrics.MeterName);
    }

    [Fact(Timeout = 5000)]
    public void OpenConnections_should_increment_active()
    {
        ClearMeasurements();

        ServusMetrics.OpenConnections.Add(1,
            new KeyValuePair<string, object?>("http.connection.state", "active"),
            new KeyValuePair<string, object?>("server.address", "pool.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.open_connections"));
        Assert.Equal(1, m.Value);
        Assert.Equal("active", GetTag(m.Tags, "http.connection.state"));
    }

    [Fact(Timeout = 5000)]
    public void OpenConnections_should_decrement_active()
    {
        ClearMeasurements();

        ServusMetrics.OpenConnections.Add(1,
            new KeyValuePair<string, object?>("http.connection.state", "active"),
            new KeyValuePair<string, object?>("server.address", "pool.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));
        ServusMetrics.OpenConnections.Add(-1,
            new KeyValuePair<string, object?>("http.connection.state", "active"),
            new KeyValuePair<string, object?>("server.address", "pool.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.open_connections");
        Assert.Equal(2, measurements.Count);
        Assert.Contains(measurements, m => m.Value == 1);
        Assert.Contains(measurements, m => m.Value == -1);
    }

    [Fact(Timeout = 5000)]
    public void OpenConnections_should_distinguish_active_and_idle()
    {
        ClearMeasurements();

        ServusMetrics.OpenConnections.Add(1,
            new KeyValuePair<string, object?>("http.connection.state", "active"));
        ServusMetrics.OpenConnections.Add(1,
            new KeyValuePair<string, object?>("http.connection.state", "idle"));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.open_connections");
        Assert.Equal(2, measurements.Count);
        Assert.Contains(measurements, m => GetTag(m.Tags, "http.connection.state")?.ToString() == "active");
        Assert.Contains(measurements, m => GetTag(m.Tags, "http.connection.state")?.ToString() == "idle");
    }

    [Fact(Timeout = 5000)]
    public void ConnectionDuration_should_record()
    {
        ClearMeasurements();

        ServusMetrics.ConnectionDuration.Record(30.5,
            new KeyValuePair<string, object?>("server.address", "conn.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("http.client.connection.duration"));
        Assert.Equal(30.5, m.Value);
        Assert.Equal("conn.example.com", GetTag(m.Tags, "server.address"));
    }

    [Fact(Timeout = 5000)]
    public void RequestTimeInQueue_should_record()
    {
        ClearMeasurements();

        ServusMetrics.RequestTimeInQueue.Record(0.050,
            new KeyValuePair<string, object?>("server.address", "example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("http.client.request.time_in_queue"));
        Assert.Equal(0.050, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void DnsLookupDuration_should_record()
    {
        ClearMeasurements();

        ServusMetrics.DnsLookupDuration.Record(0.015,
            new KeyValuePair<string, object?>("dns.question.name", "example.com"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("dns.lookup.duration"));
        Assert.Equal(0.015, m.Value);
        Assert.Equal("example.com", GetTag(m.Tags, "dns.question.name"));
    }

    [Fact(Timeout = 5000)]
    public void Instruments_should_have_correct_units()
    {
        Assert.Equal("{connection}", ServusMetrics.OpenConnections.Unit);
        Assert.Equal("s", ServusMetrics.ConnectionDuration.Unit);
        Assert.Equal("s", ServusMetrics.RequestTimeInQueue.Unit);
        Assert.Equal("s", ServusMetrics.DnsLookupDuration.Unit);
    }

    [Fact(Timeout = 5000)]
    public void Instruments_should_have_descriptions()
    {
        Assert.False(string.IsNullOrEmpty(ServusMetrics.OpenConnections.Description));
        Assert.False(string.IsNullOrEmpty(ServusMetrics.ConnectionDuration.Description));
        Assert.False(string.IsNullOrEmpty(ServusMetrics.RequestTimeInQueue.Description));
        Assert.False(string.IsNullOrEmpty(ServusMetrics.DnsLookupDuration.Description));
    }

    private void ClearMeasurements()
    {
        _longMeasurements.Clear();
        _doubleMeasurements.Clear();
    }

    private List<MetricMeasurement<long>> GetLongMeasurements(string name) =>
        _longMeasurements.Where(m => m.InstrumentName == name).ToList();

    private List<MetricMeasurement<double>> GetDoubleMeasurements(string name) =>
        _doubleMeasurements.Where(m => m.InstrumentName == name).ToList();

    private static object? GetTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
            {
                return tag.Value;
            }
        }

        return null;
    }

    private readonly record struct MetricMeasurement<T> where T : struct
    {
        public string InstrumentName { get; }
        public T Value { get; }
        public KeyValuePair<string, object?>[] Tags { get; }

        public MetricMeasurement(string instrumentName, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            InstrumentName = instrumentName;
            Value = value;
            Tags = tags.ToArray();
        }
    }
}
