using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Features.Hardware;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Hardware;

/// <summary>Anti-spoofing (spec 5.8): only a trusted LibreHardwareMonitor is read.</summary>
public sealed class LhmTrustEvaluatorTests
{
    private const string LhmPath = @"C:\Program Files\LibreHardwareMonitor\LibreHardwareMonitor.exe";
    private static readonly Version V096 = new(0, 9, 6, 0);
    private static readonly LhmProcessInfo Lhm = new(LhmPath, V096, true);

    [Fact]
    public void HttpSysListener_WithProtectedLhm_IsTrusted()
    {
        var result = LhmTrustEvaluator.Evaluate(FakeLhmEndpoint.Trusted(V096), 8085);

        result.IsTrusted.ShouldBeTrue(result.Reason);
        result.Version.ShouldBe(V096);
    }

    [Fact]
    public void LhmItselfOwningThePort_FromAProtectedFolder_IsTrusted()
    {
        var snapshot = new LhmEndpointSnapshot(PortOwnerKind.Process, LhmPath, true, V096, [Lhm]);

        LhmTrustEvaluator.Evaluate(snapshot, 8085).IsTrusted.ShouldBeTrue();
    }

    [Fact]
    public void NoLhmProcess_IsNotRunning_EvenIfSomethingListens()
    {
        var snapshot = new LhmEndpointSnapshot(PortOwnerKind.HttpSys, null, false, null, []);

        LhmTrustEvaluator.Evaluate(snapshot, 8085).Problem.ShouldBe(HardwareStatus.NotRunning);
    }

    [Fact]
    public void NothingListening_IsNotRunning()
    {
        var snapshot = new LhmEndpointSnapshot(PortOwnerKind.None, null, false, null, [Lhm]);

        var result = LhmTrustEvaluator.Evaluate(snapshot, 8085);

        result.Problem.ShouldBe(HardwareStatus.NotRunning);
        result.Reason.ShouldContain("8085");
    }

    [Fact]
    public void AnotherProgramOnThePort_IsUntrusted()
    {
        // A standard-user program pretending to be LHM.
        var snapshot = new LhmEndpointSnapshot(PortOwnerKind.Process, @"C:\Users\me\AppData\Local\Temp\fake.exe", false, null, [Lhm]);

        var result = LhmTrustEvaluator.Evaluate(snapshot, 8085);

        result.Problem.ShouldBe(HardwareStatus.Untrusted);
        result.Reason.ShouldContain("fake.exe");
    }

    [Fact]
    public void ACopyNamedLibreHardwareMonitor_InAUserFolder_IsUntrusted()
    {
        const string userCopy = @"C:\Users\me\Downloads\LibreHardwareMonitor.exe";
        var snapshot = new LhmEndpointSnapshot(PortOwnerKind.Process, userCopy, false, V096, [new LhmProcessInfo(userCopy, V096, false)]);

        LhmTrustEvaluator.Evaluate(snapshot, 8085).Problem.ShouldBe(HardwareStatus.Untrusted);
    }

    [Fact]
    public void LhmRunningFromAWritableFolder_IsUntrusted_EvenOnHttpSys()
    {
        var snapshot = new LhmEndpointSnapshot(PortOwnerKind.HttpSys, null, false, null, [new LhmProcessInfo(@"C:\Users\me\LHM\LibreHardwareMonitor.exe", V096, false)]);

        var result = LhmTrustEvaluator.Evaluate(snapshot, 8085);

        result.Problem.ShouldBe(HardwareStatus.Untrusted);
        result.Reason.ShouldContain("Program Files");
    }

    [Fact]
    public void UnknownOwner_IsUntrusted()
    {
        var snapshot = new LhmEndpointSnapshot(PortOwnerKind.Unknown, null, false, null, [Lhm]);

        LhmTrustEvaluator.Evaluate(snapshot, 8085).Problem.ShouldBe(HardwareStatus.Untrusted);
    }

    [Fact]
    public void UnreadableVersion_IsUntrusted()
    {
        var snapshot = new LhmEndpointSnapshot(PortOwnerKind.HttpSys, null, false, null, [Lhm with { Version = null }]);

        LhmTrustEvaluator.Evaluate(snapshot, 8085).Problem.ShouldBe(HardwareStatus.Untrusted);
    }

    [Fact]
    public void TooOld_IsIncompatible_WithItsVersion()
    {
        var old = new Version(0, 9, 4, 0);

        var result = LhmTrustEvaluator.Evaluate(FakeLhmEndpoint.Trusted(old), 8085);

        result.Problem.ShouldBe(HardwareStatus.Incompatible);
        result.Version.ShouldBe(old);
        result.Reason.ShouldContain("0.9.5");
    }
}
