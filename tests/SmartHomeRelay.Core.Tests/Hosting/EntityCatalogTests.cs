using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Hosting;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Hosting;

public sealed class EntityCatalogTests
{
    [Fact]
    public void Build_ValidModules_IndexesEveryEntity()
    {
        var presence = new FakeModule("presence", FakeModule.Sensor("idle_seconds"), FakeModule.Sensor("presence_state"));
        var metrics = new FakeModule("metrics", FakeModule.Sensor("cpu_usage"));

        var catalog = EntityCatalog.Build([presence, metrics]);

        catalog.Rejections.ShouldBeEmpty();
        catalog.Modules.ShouldBe([presence, metrics]);
        catalog.Entries.Count().ShouldBe(3);
        catalog.TryGet("cpu_usage", out var entry).ShouldBeTrue();
        entry.Module.ShouldBe(metrics);
    }

    [Fact]
    public void Build_InvalidModule_RejectsOnlyThatModule()
    {
        var good = new FakeModule("good", FakeModule.Sensor("uptime"));
        var bad = new FakeModule("bad", new EntityDescriptor("Bad-Key", "Bad", EntityComponent.Sensor));

        var catalog = EntityCatalog.Build([good, bad]);

        catalog.Modules.ShouldBe([good]);
        catalog.Rejections.ShouldHaveSingleItem().ShouldContain("Module 'bad'");
        catalog.TryGet("uptime", out _).ShouldBeTrue();
    }

    [Fact]
    public void Build_KeyUsedByTwoModules_RejectsTheSecond()
    {
        var first = new FakeModule("first", FakeModule.Sensor("uptime"));
        var second = new FakeModule("second", FakeModule.Sensor("uptime"), FakeModule.Sensor("other"));

        var catalog = EntityCatalog.Build([first, second]);

        catalog.Modules.ShouldBe([first]);
        catalog.Rejections.ShouldHaveSingleItem().ShouldContain("already used by module 'first'");
        catalog.TryGet("other", out _).ShouldBeFalse();
    }

    [Fact]
    public void Build_DuplicateModuleId_RejectsTheSecond()
    {
        var catalog = EntityCatalog.Build([new FakeModule("power", FakeModule.Sensor("a")), new FakeModule("power", FakeModule.Sensor("b"))]);

        catalog.Modules.Count.ShouldBe(1);
        catalog.Rejections.ShouldHaveSingleItem().ShouldContain("already uses this ID");
    }

    [Fact]
    public void Build_DescribeEntitiesThrows_RejectsModuleWithoutThrowing()
    {
        var broken = new FakeModule("broken") { ThrowOnDescribe = new InvalidOperationException("boom") };
        var good = new FakeModule("good", FakeModule.Sensor("uptime"));

        var catalog = EntityCatalog.Build([broken, good]);

        catalog.Modules.ShouldBe([good]);
        catalog.Rejections.ShouldHaveSingleItem().ShouldContain("boom");
    }
}
