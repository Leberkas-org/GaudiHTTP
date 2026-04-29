using Servus.Akka.Transport;

namespace Servus.Akka.Tests.Transport;

public sealed class PipeModeSpec
{
    [Fact(Timeout = 5000)]
    public void PipeMode_should_have_three_values()
    {
        var values = Enum.GetValues<PipeMode>();
        Assert.Equal(3, values.Length);
    }

    [Theory(Timeout = 5000)]
    [InlineData(PipeMode.Bidirectional, 0)]
    [InlineData(PipeMode.WriteOnly, 1)]
    [InlineData(PipeMode.ReadOnly, 2)]
    public void PipeMode_should_have_correct_ordinal(PipeMode mode, int expected)
    {
        Assert.Equal(expected, (int)mode);
    }
}
