using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace ConformanceFixture;
[TestClass]
public class Cases
{
    [TestMethod] public void Passing() { Console.WriteLine("independent stdout"); Thread.Sleep(12); }
    [TestMethod] public void Failing() { Assert.Fail("independent failure"); }
    [TestMethod, Ignore("independent skip")] public void Skipped() { }
    [DataTestMethod, DataRow(1), DataRow(2)] public void Data(int value) { Assert.IsTrue(value > 0); }
}
