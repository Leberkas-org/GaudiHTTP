using Akka.Streams;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Client;

namespace TurboHTTP.Tests.Streams;

public sealed class ConnectionShapeSpec
{
    [Fact(Timeout = 5000)]
    public void ClientConnectionShape_should_initialize_with_correct_ports()
    {
        var inServer = new Inlet<ITransportInbound>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<ITransportOutbound>("OutNetwork");

        var shape = new ClientConnectionShape(inServer, outResponse, inApp, outNetwork);

        Assert.Equal(inServer, shape.InNetwork);
        Assert.Equal(outResponse, shape.OutResponse);
        Assert.Equal(inApp, shape.InRequest);
        Assert.Equal(outNetwork, shape.OutNetwork);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionShape_should_report_correct_inlets()
    {
        var inServer = new Inlet<ITransportInbound>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<ITransportOutbound>("OutNetwork");

        var shape = new ClientConnectionShape(inServer, outResponse, inApp, outNetwork);

        var inlets = shape.Inlets;
        Assert.Equal(2, inlets.Length);
        Assert.Contains(inServer, inlets);
        Assert.Contains(inApp, inlets);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionShape_should_report_correct_outlets()
    {
        var inServer = new Inlet<ITransportInbound>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<ITransportOutbound>("OutNetwork");

        var shape = new ClientConnectionShape(inServer, outResponse, inApp, outNetwork);

        var outlets = shape.Outlets;
        Assert.Equal(2, outlets.Length);
        Assert.Contains(outResponse, outlets);
        Assert.Contains(outNetwork, outlets);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionShape_should_create_deep_copy()
    {
        var inServer = new Inlet<ITransportInbound>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<ITransportOutbound>("OutNetwork");

        var shape = new ClientConnectionShape(inServer, outResponse, inApp, outNetwork);
        var copy = shape.DeepCopy();

        Assert.IsType<ClientConnectionShape>(copy);
        var copiedShape = (ClientConnectionShape)copy;

        Assert.NotSame(shape.InNetwork, copiedShape.InNetwork);
        Assert.NotSame(shape.OutResponse, copiedShape.OutResponse);
        Assert.NotSame(shape.InRequest, copiedShape.InRequest);
        Assert.NotSame(shape.OutNetwork, copiedShape.OutNetwork);

        // Port names should be preserved
        Assert.Equal(shape.InNetwork.Name, copiedShape.InNetwork.Name);
        Assert.Equal(shape.OutResponse.Name, copiedShape.OutResponse.Name);
        Assert.Equal(shape.InRequest.Name, copiedShape.InRequest.Name);
        Assert.Equal(shape.OutNetwork.Name, copiedShape.OutNetwork.Name);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionShape_should_copy_from_ports()
    {
        var inServer = new Inlet<ITransportInbound>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<ITransportOutbound>("OutNetwork");

        var shape = new ClientConnectionShape(inServer, outResponse, inApp, outNetwork);

        var newInlets = new[] { inServer.CarbonCopy(), inApp.CarbonCopy() };
        var newOutlets = new[] { outResponse.CarbonCopy(), outNetwork.CarbonCopy() };

        var copiedShape = shape.CopyFromPorts([.. newInlets], [.. newOutlets]);

        Assert.IsType<ClientConnectionShape>(copiedShape);
        var result = (ClientConnectionShape)copiedShape;

        Assert.Equal(2, result.Inlets.Length);
        Assert.Equal(2, result.Outlets.Length);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionShape_should_maintain_port_order_in_inlets()
    {
        var inServer = new Inlet<ITransportInbound>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<ITransportOutbound>("OutNetwork");

        var shape = new ClientConnectionShape(inServer, outResponse, inApp, outNetwork);

        // Order should be InServer first, then InApp
        Assert.Equal(inServer, shape.Inlets[0]);
        Assert.Equal(inApp, shape.Inlets[1]);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionShape_should_maintain_port_order_in_outlets()
    {
        var inServer = new Inlet<ITransportInbound>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<ITransportOutbound>("OutNetwork");

        var shape = new ClientConnectionShape(inServer, outResponse, inApp, outNetwork);

        // Order should be OutResponse first, then OutNetwork
        Assert.Equal(outResponse, shape.Outlets[0]);
        Assert.Equal(outNetwork, shape.Outlets[1]);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionShape_should_implement_shape_interface()
    {
        var inServer = new Inlet<ITransportInbound>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<ITransportOutbound>("OutNetwork");

        var shape = new ClientConnectionShape(inServer, outResponse, inApp, outNetwork);

        Assert.IsAssignableFrom<Shape>(shape);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionShape_deep_copy_should_create_independent_instances()
    {
        var inServer = new Inlet<ITransportInbound>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<ITransportOutbound>("OutNetwork");

        var shape1 = new ClientConnectionShape(inServer, outResponse, inApp, outNetwork);
        var shape2 = shape1.DeepCopy();
        var shape3 = shape1.DeepCopy();

        var copied2 = (ClientConnectionShape)shape2;
        var copied3 = (ClientConnectionShape)shape3;

        // Different copies should have different port instances
        Assert.NotSame(copied2.InNetwork, copied3.InNetwork);
        Assert.NotSame(copied2.OutResponse, copied3.OutResponse);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionShape_copy_from_ports_should_preserve_port_types()
    {
        var inServer = new Inlet<ITransportInbound>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<ITransportOutbound>("OutNetwork");

        var shape = new ClientConnectionShape(inServer, outResponse, inApp, outNetwork);

        var newInlets = new[] { inServer.CarbonCopy(), inApp.CarbonCopy() };
        var newOutlets = new[] { outResponse.CarbonCopy(), outNetwork.CarbonCopy() };

        var copiedShape = shape.CopyFromPorts([.. newInlets], [.. newOutlets]);
        var result = (ClientConnectionShape)copiedShape;

        Assert.IsType<Inlet<ITransportInbound>>(result.InNetwork);
        Assert.IsType<Outlet<HttpResponseMessage>>(result.OutResponse);
        Assert.IsType<Inlet<HttpRequestMessage>>(result.InRequest);
        Assert.IsType<Outlet<ITransportOutbound>>(result.OutNetwork);
    }
}