namespace Server.Tests;

public class SerialTests
{
    [Fact]
    public void ZeroAndMinusOneAreDistinct()
    {
        Assert.NotEqual(Serial.Zero, Serial.MinusOne);
    }

    [Fact]
    public void ValueRoundTripsThroughConstructor()
    {
        var serial = (Serial)12345;

        Assert.Equal(12345, serial.Value);
    }

    [Fact]
    public void EqualityOperatorsMatchValue()
    {
        var a = (Serial)500;
        var b = (Serial)500;
        var c = (Serial)501;

        Assert.True(a == b);
        Assert.False(a == c);
        Assert.True(a != c);
    }
}
