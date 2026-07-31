using System.Text;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Execution;

namespace Sdk.Common.Tests.EtlDataPipeline;

public class PipelineScratchSpaceTests
{
    [Fact]
    public async Task CreateWriteRead_RoundTripsBytesAndLength()
    {
        await using var space = new PipelineScratchSpace();
        var payload = Encoding.UTF8.GetBytes("hello scratch space");

        var token = space.CreateFile("pdf");
        Assert.True(space.Exists(token));

        var ct = TestContext.Current.CancellationToken;
        await using (var write = space.OpenWrite(token))
        {
            await write.WriteAsync(payload, ct);
        }

        Assert.Equal(payload.Length, space.GetLength(token));

        await using var read = space.OpenRead(token);
        using var ms = new MemoryStream();
        await read.CopyToAsync(ms, ct);
        Assert.Equal(payload, ms.ToArray());
    }

    [Fact]
    public async Task DisposeAsync_DeletesTheFiles()
    {
        var space = new PipelineScratchSpace();
        var token = space.CreateFile();
        Assert.True(space.Exists(token));

        await space.DisposeAsync();

        // The per-execution directory is gone, so the file no longer exists.
        Assert.False(space.Exists(token));
    }

    [Fact]
    public void OpenRead_UnknownToken_Throws()
    {
        using var _ = new AsyncDisposeGuard(out var space);
        Assert.Throws<InvalidOperationException>(() => space.OpenRead("not-a-real-token"));
    }

    [Fact]
    public async Task ScratchFileRef_RoundTripsThroughDataContext()
    {
        using var dataContext = new DataContextImpl();
        ScratchFileRef.Write(dataContext, "$.artifact", "tok123", 42, "handover.zip", "application/zip");

        Assert.True(ScratchFileRef.TryRead(dataContext, "$.artifact", out var reference));
        Assert.Equal("tok123", reference.Token);
        Assert.Equal(42, reference.Length);
        Assert.Equal("handover.zip", reference.FileName);
        Assert.Equal("application/zip", reference.ContentType);
    }

    [Fact]
    public void ScratchFileRef_TreatsBase64StringAsInline()
    {
        using var dataContext = new DataContextImpl();
        dataContext.Set("$.artifact", "SGVsbG8gd29ybGQ="); // a plain base64 string, not a scratch ref

        Assert.False(ScratchFileRef.TryRead(dataContext, "$.artifact", out _));
    }

    [Fact]
    public void ScratchFileRef_MissingPath_ReturnsFalse()
    {
        using var dataContext = new DataContextImpl();
        Assert.False(ScratchFileRef.TryRead(dataContext, "$.nothing", out _));
    }

    // Small helper so the synchronous test can hold an IAsyncDisposable without an async body.
    private sealed class AsyncDisposeGuard : IDisposable
    {
        private readonly PipelineScratchSpace _space;

        public AsyncDisposeGuard(out PipelineScratchSpace space)
        {
            _space = space = new PipelineScratchSpace();
        }

        public void Dispose()
        {
            _space.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
