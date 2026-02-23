using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;

namespace SimplePLCDriverCore.Tests.Common;

public class ConnectionPoolTests
{
    [Fact]
    public void Register_AddsConnection()
    {
        using var pool = new ConnectionPool();

        pool.Register("PLC1", "192.168.1.100");

        Assert.Contains("PLC1", pool.RegisteredNames);
    }

    [Fact]
    public void Register_DuplicateName_Throws()
    {
        using var pool = new ConnectionPool();
        pool.Register("PLC1", "192.168.1.100");

        Assert.Throws<InvalidOperationException>(() =>
            pool.Register("PLC1", "192.168.1.200"));
    }

    [Fact]
    public void Register_CaseInsensitive()
    {
        using var pool = new ConnectionPool();
        pool.Register("PLC1", "192.168.1.100");

        Assert.Throws<InvalidOperationException>(() =>
            pool.Register("plc1", "192.168.1.200"));
    }

    [Fact]
    public async Task GetAsync_UnregisteredName_ThrowsKeyNotFound()
    {
        await using var pool = new ConnectionPool();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            pool.GetAsync("Unknown").AsTask());
    }

    [Fact]
    public void RegisteredNames_ReturnsAllRegistered()
    {
        using var pool = new ConnectionPool();
        pool.Register("PLC1", "192.168.1.100");
        pool.Register("PLC2", "192.168.1.101");
        pool.Register("PLC3", "192.168.1.102");

        var names = pool.RegisteredNames;
        Assert.Equal(3, names.Count);
        Assert.Contains("PLC1", names);
        Assert.Contains("PLC2", names);
        Assert.Contains("PLC3", names);
    }

    [Fact]
    public void IsConnected_Unregistered_ReturnsFalse()
    {
        using var pool = new ConnectionPool();

        Assert.False(pool.IsConnected("Nope"));
    }

    [Fact]
    public void IsConnected_RegisteredButNotConnected_ReturnsFalse()
    {
        using var pool = new ConnectionPool();
        pool.Register("PLC1", "192.168.1.100");

        Assert.False(pool.IsConnected("PLC1"));
    }

    [Fact]
    public async Task UnregisterAsync_RemovesRegistration()
    {
        await using var pool = new ConnectionPool();
        pool.Register("PLC1", "192.168.1.100");

        await pool.UnregisterAsync("PLC1");

        Assert.DoesNotContain("PLC1", pool.RegisteredNames);
    }

    [Fact]
    public async Task UnregisterAsync_NonExistent_NoError()
    {
        await using var pool = new ConnectionPool();

        // Should not throw
        await pool.UnregisterAsync("DoesNotExist");
    }

    [Fact]
    public async Task DisconnectAllAsync_ClearsActiveDrivers()
    {
        await using var pool = new ConnectionPool();
        pool.Register("PLC1", "192.168.1.100");

        await pool.DisconnectAllAsync();

        // Registrations preserved
        Assert.Contains("PLC1", pool.RegisteredNames);
        Assert.False(pool.IsConnected("PLC1"));
    }

    [Fact]
    public async Task DisposeAsync_DisposesCleanly()
    {
        var pool = new ConnectionPool();
        pool.Register("PLC1", "192.168.1.100");

        await pool.DisposeAsync();

        // Should throw ObjectDisposedException after disposal
        Assert.Throws<ObjectDisposedException>(() =>
            pool.Register("PLC2", "192.168.1.200"));
    }

    [Fact]
    public void Dispose_DisposesCleanly()
    {
        var pool = new ConnectionPool();
        pool.Register("PLC1", "192.168.1.100");

        pool.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            pool.Register("PLC2", "192.168.1.200"));
    }

    [Fact]
    public async Task DisposeAsync_DoubleDispose_NoError()
    {
        var pool = new ConnectionPool();
        await pool.DisposeAsync();
        await pool.DisposeAsync(); // Should not throw
    }

    [Fact]
    public void Register_NullName_Throws()
    {
        using var pool = new ConnectionPool();

        Assert.Throws<ArgumentNullException>(() => pool.Register(null!, "192.168.1.100"));
    }

    [Fact]
    public void Register_NullHost_Throws()
    {
        using var pool = new ConnectionPool();

        Assert.Throws<ArgumentNullException>(() => pool.Register("PLC1", null!));
    }

    [Fact]
    public async Task GetAsync_NullName_Throws()
    {
        await using var pool = new ConnectionPool();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            pool.GetAsync(null!).AsTask());
    }

    [Fact]
    public void Register_WithSlotAndOptions_Accepted()
    {
        using var pool = new ConnectionPool();
        var options = new ConnectionOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AutoReconnect = false
        };

        pool.Register("PLC1", "192.168.1.100", slot: 2, options: options);

        Assert.Contains("PLC1", pool.RegisteredNames);
    }

    [Fact]
    public void ConnectedNames_WhenNoneConnected_ReturnsEmpty()
    {
        using var pool = new ConnectionPool();
        pool.Register("PLC1", "192.168.1.100");

        Assert.Empty(pool.ConnectedNames);
    }
}
