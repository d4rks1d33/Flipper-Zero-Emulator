using System;
using System.Collections.Generic;

namespace FdtSharp
{
	public sealed class TreeNode
	{
		public TreeNode()
		{
			Properties = new List<Property>();
			Subnodes = new List<TreeNode>();
		}

		public string Name { get; set; }
		public ICollection<Property> Properties { get; private set; }
		public ICollection<TreeNode> Subnodes { get; private set; }

		public IEnumerable<TreeNode> Descendants
		{
			get
			{
				foreach(var subnode in Subnodes)
				{
					yield return subnode;
					foreach(var descendant in subnode.Descendants)
					{
						yield return descendant;
					}
				}
			}
		}

		public override string ToString()
		{
			return string.Format("[TreeNode: {0}, Properties: {1}, Subnodes: {2}]", Name, Properties.Count, Subnodes.Count);
		}
		
	}
}

