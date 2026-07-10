using System;
using NUnit.Framework;
using FdtSharp;
using System.IO;

namespace FdtSharpWithTests
{
	[TestFixture]
	public class WritingTests
	{
		[Test]
		public void ShouldRewriteProperTree()
		{
			var originalBlob = File.ReadAllBytes(Utilities.GetBinaryLocation("xilinx_zynq_iic.dtb"));
			var fdt = new FlattenedDeviceTree(originalBlob);
			var result = fdt.GetBinaryBlob();
			CollectionAssert.AreEqual(originalBlob, result);
		}
	}
}

