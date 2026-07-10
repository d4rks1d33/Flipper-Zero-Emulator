using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FdtSharp
{
	public sealed class FlattenedDeviceTree
	{
		public FlattenedDeviceTree()
		{
			ReservationBlocks = new List<ReservationBlock>();
			Version = 17;
			LastCompatibleVersion = 16;
			Root = new TreeNode();
		}

		public FlattenedDeviceTree(byte[] treeData) : this()
		{
			ReadTree(treeData);
		}

		public uint Version { get; private set; }
		public uint LastCompatibleVersion { get; private set; }
		public uint BootCPUPhysicalId { get; set; }
		public TreeNode Root { get; private set; }
		public ICollection<ReservationBlock> ReservationBlocks { get; private set; }

		public byte[] GetBinaryBlob()
		{
			var stringWriter = new StringWriter();
			var treeStructure = new List<byte>();
			WriteTreeNode(treeStructure, Root, stringWriter, true);
			var reservationBlocks = WriteReservationBlocks();
			var stringData = stringWriter.Result;

			var result = new List<byte>();
			result.AddRange(Magic.BigEndian());
			var totalSize = HeaderSize + reservationBlocks.Count + treeStructure.Count + stringData.Count;
			result.AddRange(((uint)totalSize).BigEndian());
			result.AddRange(((uint)(HeaderSize + reservationBlocks.Count)).BigEndian());
			result.AddRange(((uint)(HeaderSize + reservationBlocks.Count + treeStructure.Count)).BigEndian());
			result.AddRange(((uint)HeaderSize).BigEndian());
			result.AddRange(Version.BigEndian());
			result.AddRange(LastCompatibleVersion.BigEndian());
			result.AddRange(BootCPUPhysicalId.BigEndian());
			result.AddRange(((uint)stringData.Count).BigEndian());
			result.AddRange(((uint)treeStructure.Count).BigEndian());

			result.AddRange(reservationBlocks);
			result.AddRange(treeStructure);
			result.AddRange(stringData);

			return result.ToArray();
		}

		private void ReadTree(byte[] treeData)
		{
			var magic = Utilities.ReadUintBigEndian(treeData, 0);
			if(magic != Magic)
			{
				throw new InvalidOperationException(string.Format("Wrong magic encountered: {0}, should be {1}.", magic, Magic));
			}
			Version = Utilities.ReadUintBigEndian(treeData, 20);
			LastCompatibleVersion = Utilities.ReadUintBigEndian(treeData, 24);
			BootCPUPhysicalId = Utilities.ReadUintBigEndian(treeData, 28);

			var totalSize = Utilities.ReadUintBigEndian(treeData, 4);
			if(treeData.Length < totalSize)
			{
				throw new InvalidOperationException(
					string.Format("Given array is only {0} bytes long while the whole structure needs {1} bytes.", treeData.Length, totalSize));
			}

			var stringsOffset = (int)Utilities.ReadUintBigEndian(treeData, 12);
			var stringsSize = (int)Utilities.ReadUintBigEndian(treeData, 32);
			var stringsData = new ArraySegment<byte>(treeData, stringsOffset, stringsSize);
			var stringHelper = new StringReader(stringsData);

			var reservationBlockOffset = (int)Utilities.ReadUintBigEndian(treeData, 16);
			ReadMemoryReservationBlocks(treeData, reservationBlockOffset);

			var structureOffset = (int)Utilities.ReadUintBigEndian(treeData, 8);
			var maximalStructureOffset = structureOffset + (int)Utilities.ReadUintBigEndian(treeData, 36);
			ReadStructureData(treeData, structureOffset, stringHelper, maximalStructureOffset);
		}

		private void ReadMemoryReservationBlocks(byte[] treeData, int reservationBlockOffset)
		{
			ReservationBlock currentBlock;
			while(true)
			{
				currentBlock = new ReservationBlock(Utilities.ReadUlongBigEndian(treeData, reservationBlockOffset),
					Utilities.ReadUlongBigEndian(treeData, reservationBlockOffset + 8));
				reservationBlockOffset += 16;
				if(currentBlock == default(ReservationBlock))
				{
					break;
				}
				ReservationBlocks.Add(currentBlock);
			}
		}

		private void ReadStructureData(byte[] treeData, int structureOffset, StringReader stringHelper, int maximalOffset)
		{
			var currentOffset = structureOffset;
			while(true)
			{
				var token = ReadToken(treeData, currentOffset);
				switch(token)
				{
				case Token.BeginNode:
					break;
				case Token.End:
					return;
				default:
					throw new InvalidOperationException(string.Format("Unexpected token {0} when BeginNode or End expected.", token));
				}
				Root = new TreeNode();
				currentOffset += 4;
				var nodeName = Utilities.ReadNullTerminatedString(treeData, ref currentOffset);
				Root.Name = nodeName;
				Pad(ref currentOffset);
				ParseNode(Root, treeData, ref currentOffset, stringHelper);
				if(currentOffset > maximalOffset)
				{
					throw new InvalidOperationException("Too much data read while parsing tree nodes.");
				}
			}
		}

		private void ParseNode(TreeNode treeNode, byte[] treeData, ref int currentOffset, StringReader stringHelper)
		{
			while(true)
			{
				var token = ReadToken(treeData, currentOffset);
				currentOffset += 4;
				switch(token)
				{
				case Token.EndNode:
					return;
				case Token.Property:
					var length = (int)Utilities.ReadUintBigEndian(treeData, currentOffset);
					currentOffset += 4;
					var nameOffset = Utilities.ReadUintBigEndian(treeData, currentOffset);
					currentOffset += 4;
					var name = stringHelper.GetString((int)nameOffset);
					var data = new byte[length];
					Array.Copy(treeData, currentOffset, data, 0, length);
					var property = new Property(name, data);
					treeNode.Properties.Add(property);
					currentOffset += length;
					Pad(ref currentOffset);
					break;
				case Token.BeginNode:
					var nodeName = Utilities.ReadNullTerminatedString(treeData, ref currentOffset);
					Pad(ref currentOffset);
					var subnode = new TreeNode { Name = nodeName };
					treeNode.Subnodes.Add(subnode);
					ParseNode(subnode, treeData, ref currentOffset, stringHelper);
					break;
				case Token.Nop:
					break;
				default:
					throw new InvalidOperationException(string.Format("Unexpected token {0} during node parsing.", token));
				}
			}
		}

		private void WriteTreeNode(List<byte> result, TreeNode node, StringWriter stringWriter, bool isRoot)
		{
			result.AddRange(((uint)Token.BeginNode).BigEndian());
			var nrt = node.Name.NullTerminated();
			result.AddRange(nrt);
			Pad(result);

			foreach(var property in node.Properties)
			{
				result.AddRange(((uint)Token.Property).BigEndian());
				result.AddRange(((uint)property.Data.Count).BigEndian());
				var index = (uint)stringWriter.PutString(property.Name);
				result.AddRange(index.BigEndian());
				result.AddRange(property.Data);
				Pad(result);
			}

			foreach(var subnode in node.Subnodes)
			{
				WriteTreeNode(result, subnode, stringWriter, false);
			}

			result.AddRange(((uint)Token.EndNode).BigEndian());
			if(isRoot)
			{
				result.AddRange(((uint)Token.End).BigEndian());
			}
		}

		private List<byte> WriteReservationBlocks()
		{
			var result = new List<byte>();
			foreach(var entry in ReservationBlocks.Union( new [] { default(ReservationBlock) }))
			{
				result.AddRange(entry.Address.BigEndian());
				result.AddRange(entry.Size.BigEndian());
			}
			return result;
		}

		private static void Pad(ref int value)
		{
			value = 4 * (value / 4) + ((value % 4 > 0) ? 4 : 0);
		}

		private static void Pad(List<byte> list)
		{
			var length = list.Count;
			Pad(ref length);
			list.AddRange(Enumerable.Repeat((byte)0, length - list.Count));
		}

		private static Token ReadToken(byte[] data, int offset)
		{
			return (Token)Utilities.ReadUintBigEndian(data, offset);
		}

		private const uint Magic = 0xd00dfeed;
		private const int HeaderSize = 40;
	}
}

