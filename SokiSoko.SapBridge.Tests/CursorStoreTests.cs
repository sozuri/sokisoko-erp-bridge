using SokiSoko.SapBridge.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SokiSoko.SapBridge.Tests;

public class CursorStoreTests
{
    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void Cursors_PersistAcrossInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sapbridge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var env = new FakeEnv { ContentRootPath = dir };
            var store = new CursorStore(env, NullLogger<CursorStore>.Instance);
            store.Set("product", "2026-09-10T13:45:00");
            store.Set("price", "2026-09-11T08:00:00");

            var reloaded = new CursorStore(env, NullLogger<CursorStore>.Instance);
            Assert.Equal("2026-09-10T13:45:00", reloaded.Get("product"));
            Assert.Equal("2026-09-11T08:00:00", reloaded.Get("price"));
            Assert.Equal("", reloaded.Get("customer"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Set_NeverMovesCursorBackwards()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sapbridge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new CursorStore(new FakeEnv { ContentRootPath = dir }, NullLogger<CursorStore>.Instance);
            store.Set("product", "2026-09-11T08:00:00");
            store.Set("product", "2026-09-10T13:45:00");
            Assert.Equal("2026-09-11T08:00:00", store.Get("product"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
