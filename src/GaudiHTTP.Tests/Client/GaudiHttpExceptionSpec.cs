using GaudiHTTP.Internal;

namespace GaudiHTTP.Tests.Client;

internal sealed class TestGaudiHttpException : GaudiHttpException
{
    public TestGaudiHttpException(string message) : base(message)
    {
    }

    public TestGaudiHttpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class TestGaudiProtocolException : GaudiProtocolException
{
    public TestGaudiProtocolException(string message) : base(message)
    {
    }

    public TestGaudiProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class TestGaudiTransportException(string message) : GaudiTransportException(message);

public sealed class GaudiHttpExceptionSpec
{
    [Fact(Timeout = 5000)]
    public void GaudiHttpException_WithMessage_CreatesException()
    {
        var exception = new TestGaudiHttpException("test message");

        Assert.NotNull(exception);
        Assert.Equal("test message", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpException_WithMessageAndInner_CreatesException()
    {
        var innerException = new InvalidOperationException("inner");
        var exception = new TestGaudiHttpException("test message", innerException);

        Assert.NotNull(exception);
        Assert.Equal("test message", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpException_IsException()
    {
        var exception = new TestGaudiHttpException("test");

        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact(Timeout = 5000)]
    public void GaudiProtocolException_WithMessage_CreatesException()
    {
        var exception = new TestGaudiProtocolException("protocol error");

        Assert.NotNull(exception);
        Assert.Equal("protocol error", exception.Message);
    }

    [Fact(Timeout = 5000)]
    public void GaudiProtocolException_WithMessageAndInner_CreatesException()
    {
        var innerException = new InvalidDataException("malformed");
        var exception = new TestGaudiProtocolException("protocol error", innerException);

        Assert.NotNull(exception);
        Assert.Equal("protocol error", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact(Timeout = 5000)]
    public void GaudiProtocolException_IsGaudiHttpException()
    {
        var exception = new TestGaudiProtocolException("test");

        Assert.IsAssignableFrom<GaudiHttpException>(exception);
    }

    [Fact(Timeout = 5000)]
    public void GaudiTransportException_WithMessage_CreatesException()
    {
        var exception = new TestGaudiTransportException("connection failed");

        Assert.NotNull(exception);
        Assert.Equal("connection failed", exception.Message);
    }

    [Fact(Timeout = 5000)]
    public void GaudiTransportException_Message_IsPreserved()
    {
        var exception = new TestGaudiTransportException("connection failed");

        Assert.NotNull(exception);
        Assert.Equal("connection failed", exception.Message);
    }

    [Fact(Timeout = 5000)]
    public void GaudiTransportException_IsGaudiHttpException()
    {
        var exception = new TestGaudiTransportException("test");

        Assert.IsAssignableFrom<GaudiHttpException>(exception);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpException_CanBeCaughtAsException()
    {
        Exception? caughtException;

        try
        {
            throw new TestGaudiHttpException("test");
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        Assert.NotNull(caughtException);
        Assert.IsType<TestGaudiHttpException>(caughtException);
    }

    [Fact(Timeout = 5000)]
    public void GaudiProtocolException_CanBeCaughtAsGaudiHttpException()
    {
        GaudiHttpException? caughtException;

        try
        {
            throw new TestGaudiProtocolException("protocol");
        }
        catch (GaudiHttpException ex)
        {
            caughtException = ex;
        }

        Assert.NotNull(caughtException);
        Assert.IsType<TestGaudiProtocolException>(caughtException);
    }

    [Fact(Timeout = 5000)]
    public void MultipleExceptions_HaveIndependentStates()
    {
        var ex1 = new TestGaudiHttpException("message 1");
        var ex2 = new TestGaudiHttpException("message 2");

        Assert.NotEqual(ex1.Message, ex2.Message);
    }

    [Fact(Timeout = 5000)]
    public void ExceptionHierarchy_AllInheritFromGaudiHttpException()
    {
        var httpEx = new TestGaudiHttpException("http");
        var protocolEx = new TestGaudiProtocolException("protocol");
        var transportEx = new TestGaudiTransportException("transport");

        Assert.IsAssignableFrom<GaudiHttpException>(httpEx);
        Assert.IsAssignableFrom<GaudiHttpException>(protocolEx);
        Assert.IsAssignableFrom<GaudiHttpException>(transportEx);
    }

    [Fact(Timeout = 5000)]
    public void ExceptionToString_ContainsMessage()
    {
        var exception = new TestGaudiHttpException("test message");

        var exceptionString = exception.ToString();

        Assert.Contains("test message", exceptionString);
    }

    [Fact(Timeout = 5000)]
    public void ExceptionWithInnerException_ToStringContainsBoth()
    {
        var inner = new InvalidOperationException("inner message");
        var exception = new TestGaudiHttpException("outer message", inner);

        var exceptionString = exception.ToString();

        Assert.Contains("outer message", exceptionString);
        Assert.Contains("inner message", exceptionString);
    }
}