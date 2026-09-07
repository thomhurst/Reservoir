using System.Reflection;
using System.Runtime.Versioning;
using Reservoir;
using ReservoirPackageConsumer;

namespace Reservoir.Package.Tests;

public class DestroyPolicyCompatibilityTests
{
    [Test]
    public async Task PrecompiledNetstandardPoliciesDestroyThroughModernAsset()
    {
        string? consumerFramework = typeof(DestroyPolicyConsumer).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        string? libraryFramework = typeof(IPooledObjectPolicy<>).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        string? hostFramework = typeof(DestroyPolicyCompatibilityTests).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        await Assert.That(consumerFramework).IsEqualTo(".NETStandard,Version=v2.0");
        await Assert.That(libraryFramework).IsEqualTo(hostFramework);
        MethodInfo? portableDestroy = typeof(IPooledObjectPolicy<>).GetMethod(
            "Destroy", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        await Assert.That(portableDestroy).IsNotNull();
        await Assert.That(portableDestroy!.ReturnType).IsEqualTo(typeof(void));
        await Assert.That(typeof(IPooledObjectDestroyPolicy<>).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)).IsEmpty();
        await Assert.That(DestroyPolicyConsumer.ExplicitGeneric()).IsEqualTo(1);
        await Assert.That(DestroyPolicyConsumer.ExplicitRuntime()).IsEqualTo(1);
        await Assert.That(DestroyPolicyConsumer.ImplicitGeneric()).IsEqualTo(1);
        await Assert.That(DestroyPolicyConsumer.ImplicitRuntime()).IsEqualTo(1);
        await Assert.That(DestroyPolicyConsumer.ExplicitStatefulGeneric()).IsTrue();
        await Assert.That(DestroyPolicyConsumer.ExplicitStatefulRuntime()).IsTrue();
        await Assert.That(DestroyPolicyConsumer.ExplicitConstrained()).IsEqualTo(2);
        await Assert.That(DestroyPolicyConsumer.DestroyDeclaringInterface().GetGenericTypeDefinition())
            .IsEqualTo(typeof(IPooledObjectPolicy<>));
    }
}
