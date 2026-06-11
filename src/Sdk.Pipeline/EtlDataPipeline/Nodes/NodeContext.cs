using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

/// <summary>
/// Implementation of the node context
/// </summary>
[DebuggerDisplay("NodePath = {NodePath}")]
public class NodeContext : INodeContext
{
    private readonly IPipelineLogger _logger;
    private readonly INodeConfiguration? _configurationNode;

    /// <summary>
    /// Creates a new instance of <see cref="NodeContext"/>
    /// </summary>
    /// <param name="parent">Parent node context</param>
    /// <param name="nodeQualifiedName">The qualified name of the node</param>
    /// <param name="sequenceNumber">Sequence number of the node within a transformation list</param>
    /// <param name="serviceProvider">The scope service provider</param>
    /// <param name="pipelineLogger">The logger for the pipeline</param>
    /// <param name="nodeConfiguration">The node configuration</param>
    /// <param name="pipelineDebugger">Optional debugger for the pipeline</param>
    private NodeContext(INodeContext? parent, string? nodeQualifiedName, uint sequenceNumber,
        IServiceProvider serviceProvider,
        IPipelineLogger pipelineLogger, INodeConfiguration? nodeConfiguration,
        IPipelineDebugger? pipelineDebugger = null)
    {
        Parent = parent;
        ServiceProvider = serviceProvider;
        PipelineDebugger = pipelineDebugger;
        _logger = pipelineLogger;
        _configurationNode = nodeConfiguration;

        var name = "[" + sequenceNumber + "]";
        var id = name;
        if (!string.IsNullOrEmpty(nodeQualifiedName))
        {
            name = nodeQualifiedName;
            id = $"{sequenceNumber}:{nodeQualifiedName}";
        }

        NodePath = parent != null ? new NodePath(parent.NodePath + "/" + name) : new NodePath(name);
        NodeId = parent != null ? parent.NodeId + "/" + id : id;
        SequenceNumber = sequenceNumber;
        NodeStack = new Stack<NodePath>();

        NodeStack = new Stack<NodePath>(parent?.NodeStack.Reverse() ?? []);
        NodeStack.Push(NodePath);
    }

    /// <inheritdoc />
    public IServiceProvider ServiceProvider { get; }

    /// <inheritdoc />
    public IPipelineDebugger? PipelineDebugger { get; }

    /// <inheritdoc />
    public INodeContext? Parent { get; }

    /// <inheritdoc />
    public NodePath NodePath { get; }

    /// <inheritdoc />
    public NodePath NodeId { get; }

    /// <inheritdoc />
    public uint SequenceNumber { get; }

    /// <inheritdoc />
    public Stack<NodePath> NodeStack { get; }

    /// <inheritdoc />
    public void Debug(string message, params object[] args)
    {
        _logger.Debug(NodeId, NodePath, message, args);
    }

    /// <inheritdoc />
    public void Info(string message, params object[] args)
    {
        _logger.Info(NodeId, NodePath, message, args);
    }

    /// <inheritdoc />
    public void Warning(string message, params object[] args)
    {
        _logger.Warning(NodeId, NodePath, message, args);
    }

    /// <inheritdoc />
    public void Error(string message, params object[] args)
    {
        _logger.Error(NodeId, NodePath, message, args);
    }

    /// <inheritdoc />
    public void Error(Exception exception, string message, params object[] args)
    {
        _logger.Error(NodeId, NodePath, exception, message, args);
    }

    /// <inheritdoc />
    public void Unregister(IDataContext dataContext)
    {
        PipelineDebugger?.LogOutput(NodeId, NodePath, _configurationNode?.Description, SequenceNumber,
            ((IDebugSnapshotSource)dataContext).GetDebugSnapshot());
        _logger.Debug(NodeId, NodePath, "Node completed");
    }

    /// <inheritdoc />
    public T GetNodeConfiguration<T>() where T : INodeConfiguration
    {
        if (_configurationNode == null)
        {
            throw DataPipelineException.NoConfigurationNodeSet();
        }

        return (T)_configurationNode;
    }

    /// <inheritdoc />
    public INodeContext RegisterChildNode(uint sequenceNumber,
        INodeConfiguration nodeConfiguration, IDataContext dataContext)
    {
        var nodeContext = new NodeContext(this, null, sequenceNumber, ServiceProvider, _logger,
            nodeConfiguration,
            PipelineDebugger);
        PipelineDebugger?.LogInput(nodeContext.NodeId, nodeContext.NodePath, null, sequenceNumber,
            ((IDebugSnapshotSource)dataContext).GetDebugSnapshot());
        return nodeContext;
    }

    /// <inheritdoc />
    public INodeContext RegisterChildNode(string nodeQualifiedName, uint sequenceNumber,
        INodeConfiguration nodeConfiguration,
        IDataContext dataContext)
    {
        var nodeContext = new NodeContext(this, nodeQualifiedName, sequenceNumber, ServiceProvider, _logger,
            nodeConfiguration,
            PipelineDebugger);
        PipelineDebugger?.LogInput(nodeContext.NodeId, nodeContext.NodePath, nodeConfiguration.Description,
            sequenceNumber,
            ((IDebugSnapshotSource)dataContext).GetDebugSnapshot());
        return nodeContext;
    }

    /// <inheritdoc />
    public INodeContext RegisterChildNode(string qualifiedName,
        INodeConfiguration nodeConfiguration, IDataContext dataContext)
    {
        var nodeContext = new NodeContext(this, qualifiedName, 0, ServiceProvider, _logger,
            nodeConfiguration,
            PipelineDebugger);
        PipelineDebugger?.LogInput(nodeContext.NodeId, nodeContext.NodePath, nodeConfiguration.Description, 0,
            ((IDebugSnapshotSource)dataContext).GetDebugSnapshot());
        return nodeContext;
    }

    /// <summary>
    /// Creates a root node context
    /// </summary>
    /// <returns></returns>
    public static NodeContext CreateRootNodeContext(IServiceProvider serviceProvider, IPipelineLogger pipelineLogger,
        IDataContext dataContext,
        IPipelineDebugger? pipelineDebugger = null)
    {
        var nodeContext =
            CreateRootNodeContext(serviceProvider, pipelineLogger, "PipelineExecution", null, pipelineDebugger);
        pipelineDebugger?.LogInput(nodeContext.NodeId, nodeContext.NodePath, null, 0, ((IDebugSnapshotSource)dataContext).GetDebugSnapshot());
        return nodeContext;
    }

    /// <summary>
    /// Creates a root node context with the specified node qualified name
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="pipelineLogger"></param>
    /// <param name="nodeQualifiedName"></param>
    /// <param name="nodeConfiguration"></param>
    /// <param name="pipelineDebugger"></param>
    /// <returns></returns>
    public static NodeContext CreateRootNodeContext(IServiceProvider serviceProvider, IPipelineLogger pipelineLogger,
        string nodeQualifiedName,
        INodeConfiguration? nodeConfiguration, IPipelineDebugger? pipelineDebugger = null)
    {
        var nodeContext =
            new NodeContext(null, nodeQualifiedName, 0, serviceProvider, pipelineLogger, nodeConfiguration,
                pipelineDebugger);
        return nodeContext;
    }
}
