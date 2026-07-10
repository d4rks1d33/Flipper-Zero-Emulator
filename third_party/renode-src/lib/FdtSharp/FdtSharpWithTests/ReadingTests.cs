using System;
using NUnit.Framework;
using FdtSharp;
using System.IO;
using System.Linq;

namespace FdtSharpWithTests
{
	[TestFixture]
	public class ReadingTests
	{
		[Test]
		public void ShouldReadHeader()
		{
			var fdt = new FlattenedDeviceTree(File.ReadAllBytes(Utilities.GetBinaryLocation("xilinx_zynq_iic.dtb")));
			Assert.AreEqual(17, fdt.Version);
			Assert.AreEqual(16, fdt.LastCompatibleVersion);
		}

		[Test]
		public void ShouldReadReservationBlocks()
		{
			var fdt = new FlattenedDeviceTree(File.ReadAllBytes(Utilities.GetBinaryLocation("xilinx_zynq_iic.dtb")));
			Assert.AreEqual(0, fdt.ReservationBlocks.Count);
		}

		[Test]
		public void ShouldFindAllNodes()
		{
			var expectedNodeNames = new [] {
				"memory",
				"chosen",
				"soc",
				"amba@0",
				"intc@f8f01000",
				"pl310@f8f02000",
				"uart@e0001000",
				"timer@0xf8001000",
				"swdt@f8005000",
				"eth@e000b000",
				"phy@3",
				"slcr@f8000000",
				"i2c@e0004000",
				"touch@38",
				"usb@e0002000",
				"devcfg@f8007000",
				"tft@6C000000"
			};

			var fdt = new FlattenedDeviceTree(File.ReadAllBytes(Utilities.GetBinaryLocation("xilinx_zynq_iic.dtb")));
			var descendants = fdt.Root.Descendants;
			CollectionAssert.AreEquivalent(expectedNodeNames, descendants.Select(x => x.Name));
		}

		[Test]
		public void ShouldFindProperty()
		{
			var fdt = new FlattenedDeviceTree(File.ReadAllBytes(Utilities.GetBinaryLocation("xilinx_zynq_iic.dtb")));
			var model = fdt.Root.Properties.First(x => x.Name == "model").GetDataAsString();
			Assert.AreEqual("Enclustra MARS ZX3", model);
		}
	}
}

