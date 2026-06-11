using LiteDB;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;

internal interface IDisposableChunkedDataBuffer<T> : IChunkedDataBuffer<T>, IDisposable;

/// <summary>
///     This represents the buffer for one chunk
/// </summary>
internal interface IChunkedDataBuffer<T>
{
    ChunkedDataBufferState State { get; }
    int AddDataPoint(DataPoint<T> dataPoint);
    int AddDataPoints(List<DataPoint<T>> dataPoints);
    IEnumerable<DataPoint<T>> GetDataPoints();
}

/// <summary>
///     The buffer for one chunk (one lite database file)
/// </summary>
internal class ChunkedDataBuffer<T> : IDisposableChunkedDataBuffer<T>
{
    private readonly ILiteCollection<DataPoint<T>> _data;
    private readonly LiteDatabase _database;
    private readonly ILogger<ChunkedDataBuffer<T>> _logger;
    private readonly Action _onDataReceivedCallback;
    private bool _isDisposed;

    public ChunkedDataBuffer(ILogger<ChunkedDataBuffer<T>> logger, ChunkMetadata metadata,
        ILiteDBFactory dbFactory, Action onDataReceivedCallback)
    {
        _logger = logger;
        Metadata = metadata;
        _onDataReceivedCallback = onDataReceivedCallback;
        _database = dbFactory.Create(metadata.FileName);
        _data = _database.GetCollection<DataPoint<T>>(Constants.DataCollectionName);
    }

    public ChunkMetadata Metadata { get; }

    public ChunkedDataBufferState State => Metadata.State;


    /// <summary>
    ///     Adds a new data point to the buffer;
    ///     Returns the number of inserted data points (Id)
    /// </summary>
    /// <param name="dataPoint"></param>
    /// <returns></returns>
    public int AddDataPoint(DataPoint<T> dataPoint)
    {
        EnsureState(ChunkedDataBufferState.Open);

        dataPoint.BufferedAt = DateTimeOffset.UtcNow;


        var operationResult = _database.WithTransaction(_logger, () => _data.Insert(dataPoint));

        if (!operationResult.Success)
        {
            _logger.LogError("Error inserting data point");
            return 0;
        }

        Metadata.DataCount++;

        // Persist metadata synchronously: a fire-and-forget Task.Run here races with
        // TryCloseCurrentChunk's own metadata write on the shared metadata database. A
        // stale background update can land after the close and revert the chunk's
        // persisted State from Closed back to Open, so GetClosedChunks() never sees the
        // chunk and its buffered data is silently dropped on retrieval. Running the
        // callback inline orders every insert's metadata write before the close.
        _onDataReceivedCallback();

        return Metadata.DataCount;
    }

    public int AddDataPoints(List<DataPoint<T>> dataPoints)
    {
        EnsureState(ChunkedDataBufferState.Open);

        var now = DateTimeOffset.UtcNow;

        dataPoints.ForEach(x => x.BufferedAt = now);

        var operationResult = _database.WithTransaction(_logger, () => _data.InsertBulk(dataPoints));


        if (!operationResult.Success)
        {
            _logger.LogError("Error inserting data points");
            return 0;
        }

        Metadata.DataCount += operationResult.Result;

        // Synchronous on purpose — see AddDataPoint: a fire-and-forget metadata write
        // races with TryCloseCurrentChunk and can revert the chunk's persisted State.
        _onDataReceivedCallback();

        return Metadata.DataCount;
    }

    public IEnumerable<DataPoint<T>> GetDataPoints()
    {
        EnsureState(ChunkedDataBufferState.Closed);

        return _data.Find(Query.All("Id"));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _database.Dispose();
        _isDisposed = true;
    }


    public void Open()
    {
        if (_isDisposed)
        {
            throw EdgeDataBufferException.Disposed();
        }

        Metadata.State = ChunkedDataBufferState.Open;
    }

    public void Close()
    {
        if (_isDisposed)
        {
            throw EdgeDataBufferException.Disposed();
        }

        Metadata.State = ChunkedDataBufferState.Closed;
    }

    private void EnsureState(ChunkedDataBufferState requiredState)
    {
        if (Metadata.State != requiredState)
        {
            throw EdgeDataBufferException.ChunkedBufferHasInvalidStateForOperation(Metadata.State, requiredState);
        }

        if (_isDisposed)
        {
            throw EdgeDataBufferException.Disposed();
        }
    }
}