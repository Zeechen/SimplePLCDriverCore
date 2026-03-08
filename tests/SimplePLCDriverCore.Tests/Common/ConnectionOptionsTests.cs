using SimplePLCDriverCore.Common;

namespace SimplePLCDriverCore.Tests.Common;

public class ConnectionOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new ConnectionOptions();

        Assert.Equal(TimeSpan.FromSeconds(5), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.KeepAliveInterval);
        Assert.True(options.AutoReconnect);
        Assert.Equal(3, options.MaxReconnectAttempts);
        Assert.Equal(TimeSpan.FromSeconds(2), options.ReconnectDelay);
        Assert.Equal((byte)0, options.Slot);
        Assert.Equal(0, options.ConnectionSize);
        Assert.Null(options.ReconnectPolicy);
    }

    [Fact]
    public void GetEffectiveReconnectPolicy_NoCustom_ReturnsFixedDelay()
    {
        var options = new ConnectionOptions
        {
            MaxReconnectAttempts = 5,
            ReconnectDelay = TimeSpan.FromSeconds(3),
        };

        var policy = options.GetEffectiveReconnectPolicy();

        Assert.NotNull(policy);
        Assert.Equal(5, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(3), policy.BaseDelay);
        Assert.False(policy.UseExponentialBackoff);
    }

    [Fact]
    public void GetEffectiveReconnectPolicy_WithCustom_ReturnsCustom()
    {
        var custom = RetryPolicy.ExponentialBackoff(10, TimeSpan.FromSeconds(1));
        var options = new ConnectionOptions
        {
            ReconnectPolicy = custom,
        };

        var policy = options.GetEffectiveReconnectPolicy();

        Assert.Same(custom, policy);
        Assert.Equal(10, policy.MaxAttempts);
        Assert.True(policy.UseExponentialBackoff);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new ConnectionOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            RequestTimeout = TimeSpan.FromSeconds(30),
            KeepAliveInterval = TimeSpan.Zero,
            AutoReconnect = false,
            MaxReconnectAttempts = 10,
            ReconnectDelay = TimeSpan.FromSeconds(5),
            Slot = 2,
            ConnectionSize = 4002,
        };

        Assert.Equal(TimeSpan.FromSeconds(15), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RequestTimeout);
        Assert.Equal(TimeSpan.Zero, options.KeepAliveInterval);
        Assert.False(options.AutoReconnect);
        Assert.Equal(10, options.MaxReconnectAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ReconnectDelay);
        Assert.Equal((byte)2, options.Slot);
        Assert.Equal(4002, options.ConnectionSize);
    }
}
