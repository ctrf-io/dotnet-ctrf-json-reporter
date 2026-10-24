using NUnit.Framework;
namespace ConformanceFixture;
[TestFixture]
public class Cases
{
    [Test] public void Passing() { TestContext.WriteLine("independent stdout"); Thread.Sleep(12); }
    [Test] public void Failing() { Assert.Fail("independent failure"); }
    [Test, Ignore("independent skip")] public void Skipped() { }
    [TestCase(1), TestCase(2)] public void Data(int value) { Assert.That(value, Is.GreaterThan(0)); }
}
