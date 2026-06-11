using LiteDB;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;

/// <summary>
///     Creates or deletes a LiteDatabase
/// </summary>
internal interface ILiteDBFactory
{
    LiteDatabase Create(string fileName);
    void Delete(string fileName);
}

/// <summary>
///     This implementation creates a LiteDatabase file on disk
/// </summary>
internal class LiteDbFileFactory : ILiteDBFactory
{
    public LiteDatabase Create(string fileName)
    {
        var folderPath = Path.GetDirectoryName(fileName);
        if (folderPath == null)
        {
            throw EdgeDataBufferException.CantOpenDatabaseFile(fileName);
        }

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        return new LiteDatabase("FileName=" + fileName + ";Mode=Exclusive");
    }

    public void Delete(string fileName)
    {
        try
        {
            File.Delete(fileName);
        }
        catch (Exception)
        {
            throw EdgeDataBufferException.CantDeleteFile(fileName);
        }
    }
}

/// <summary>
///     Used for testing purposes. Creates a LiteDatabase in memory
/// </summary>
internal class LiteDbInMemoryFactory : ILiteDBFactory
{
    public LiteDatabase Create(string fileName)
    {
        return new LiteDatabase("Filename=:memory:");
    }

    public void Delete(string fileName)
    {
        //nothing to do
    }
}