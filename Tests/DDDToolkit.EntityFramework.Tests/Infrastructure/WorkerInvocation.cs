using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Context.Features;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// An Azure Functions invocation as the isolated worker hands it to middleware, without the host: the
/// trigger's binding and the trigger data the host sends, which is where an HTTP request's headers are.
/// </summary>
public sealed class WorkerInvocation : FunctionContext
{
    private readonly FakeDefinition _definition;
    private readonly FakeBindingContext _bindings;

    private WorkerInvocation(string trigger, IReadOnlyDictionary<string, object?> data)
    {
        _definition = new FakeDefinition(trigger);
        _bindings = new FakeBindingContext(data);
    }

    /// <summary>An HTTP-triggered invocation whose request carries <paramref name="authorization"/>, or no such header.</summary>
    public static WorkerInvocation Http(string? authorization)
    {
        var headers = new Dictionary<string, string> { ["Host"] = "localhost", ["Accept"] = "application/json" };
        if (authorization is not null)
        {
            headers["Authorization"] = authorization;
        }

        return new("httpTrigger", new Dictionary<string, object?> { ["Headers"] = JsonSerializer.Serialize(headers), ["Query"] = "{}" });
    }

    /// <summary>A queue-triggered invocation: no request, no headers.</summary>
    public static WorkerInvocation Queue() => new("queueTrigger", new Dictionary<string, object?> { ["DequeueCount"] = "1" });

    public override string InvocationId { get; } = Guid.NewGuid().ToString();

    public override string FunctionId => _definition.Id;

    public override TraceContext TraceContext => throw new NotSupportedException();

    public override BindingContext BindingContext => _bindings;

    public override RetryContext RetryContext => throw new NotSupportedException();

    public override IServiceProvider InstanceServices { get; set; } = null!;

    public override FunctionDefinition FunctionDefinition => _definition;

    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();

    public override IInvocationFeatures Features => throw new NotSupportedException();

    private sealed class FakeBindingContext(IReadOnlyDictionary<string, object?> data) : BindingContext
    {
        public override IReadOnlyDictionary<string, object?> BindingData { get; } = data;
    }

    private sealed class FakeDefinition(string trigger) : FunctionDefinition
    {
        public override ImmutableArray<FunctionParameter> Parameters => [];

        public override string PathToAssembly => typeof(FakeDefinition).Assembly.Location;

        public override string EntryPoint => "Tests.Function.Run";

        public override string Id { get; } = Guid.NewGuid().ToString();

        public override string Name => "Function";

        public override IImmutableDictionary<string, BindingMetadata> InputBindings { get; } =
            ImmutableDictionary<string, BindingMetadata>.Empty.Add("trigger", new FakeBinding("trigger", trigger));

        public override IImmutableDictionary<string, BindingMetadata> OutputBindings => ImmutableDictionary<string, BindingMetadata>.Empty;
    }

    private sealed class FakeBinding(string name, string type) : BindingMetadata
    {
        public override string Name => name;

        public override string Type => type;

        public override BindingDirection Direction => BindingDirection.In;
    }
}
